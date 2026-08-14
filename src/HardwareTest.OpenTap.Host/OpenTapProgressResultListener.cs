using System.Diagnostics;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Time;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

internal sealed class ProgressResultListener : ResultListener
{
    private sealed class LoopContext
    {
        public required Guid StepId { get; init; }
        public int? Total { get; init; }
        public int Index { get; set; }
    }

    private static readonly long SampleReportIntervalTicks = TimeSpan.FromMilliseconds(50).Ticks;

    private readonly IProgress<OpenTapProgress>? _progress;
    private readonly List<StoredSample> _samples;
    private readonly List<StepResultRecord> _steps;
    private readonly Dictionary<string, DateTimeOffset> _stepStarted;
    private readonly Action<string, string?, string, string, string?> _updateNode;
    private readonly Func<string, string?, string?> _resolvePath;
    private readonly TestPlan _plan;
    private readonly IClock _clock;
    private readonly Stack<LoopContext> _loops = new();
    private int _stepIndex;
    private int _stepCount = 1;
    private long _lastSampleReportTimestamp;
    private MeasurementSampleEvent? _coalescedSample;
    private double _coalescedPercent;
    private string? _currentStepId;
    private string? _currentStepName;

    public ProgressResultListener(
        IProgress<OpenTapProgress>? progress,
        List<StoredSample> samples,
        List<StepResultRecord> steps,
        Dictionary<string, DateTimeOffset> stepStarted,
        Action<string, string?, string, string, string?> updateNode,
        Func<string, string?, string?> resolvePath,
        TestPlan plan,
        IClock clock)
    {
        _progress = progress;
        _samples = samples;
        _steps = steps;
        _stepStarted = stepStarted;
        _updateNode = updateNode;
        _resolvePath = resolvePath;
        _plan = plan;
        _clock = clock;
        Name = "HardwareTestProgress";
    }

    public override void OnTestPlanRunStart(TestPlanRun planRun)
    {
        _stepCount = OpenTapLoopProgress.CountEnabledLeaves(_plan);
        _stepIndex = 0;
        _loops.Clear();
        _lastSampleReportTimestamp = 0;
        _coalescedSample = null;
        _progress?.Report(new OpenTapProgress { Message = "Plan started", OverallPercent = 0 });
    }

    public override void OnTestStepRunStart(TestStepRun stepRun)
    {
        FlushCoalescedSample();
        var id = stepRun.TestStepId.ToString();
        _currentStepId = id;
        _currentStepName = stepRun.TestStepName;
        _stepStarted[id] = _clock.UtcNow;
        _updateNode(id, stepRun.TestStepName, "Running", "NotSet", null);

        var step = OpenTapLoopProgress.FindStepById(_plan, stepRun.TestStepId);
        if (step is not null && OpenTapLoopProgress.IsLoopStep(step))
        {
            _loops.Push(new LoopContext
            {
                StepId = step.Id,
                Total = OpenTapLoopProgress.TryGetLoopTotal(step),
                Index = 0,
            });
        }
        else if (step is not null && _loops.Count > 0)
        {
            var loopStep = OpenTapLoopProgress.FindStepById(_plan, _loops.Peek().StepId);
            if (loopStep is not null && IsFirstEnabledDirectChild(step, loopStep))
            {
                var loop = _loops.Peek();
                loop.Index++;
                if (loop.Total is > 0)
                {
                    loop.Index = Math.Min(loop.Index, loop.Total.Value);
                }
            }
        }

        ReportStepProgress(
            $"Running {stepRun.TestStepName}",
            stepRun.TestStepName,
            id,
            "Running",
            "NotSet");
    }

    public override void OnTestStepRunCompleted(TestStepRun stepRun)
    {
        FlushCoalescedSample();
        var id = stepRun.TestStepId.ToString();
        var started = _stepStarted.TryGetValue(id, out var s) ? s : _clock.UtcNow;
        var verdict = stepRun.Verdict.ToString();
        var passed = stepRun.Verdict is Verdict.Pass or Verdict.NotSet;
        _steps.Add(new StepResultRecord
        {
            StepId = id,
            StepType = stepRun.TestStepName,
            StepPath = _resolvePath(id, stepRun.TestStepName) ?? string.Empty,
            Passed = passed && stepRun.Verdict != Verdict.Fail && stepRun.Verdict != Verdict.Error,
            Message = verdict,
            StartedAt = started,
            CompletedAt = _clock.UtcNow,
        });
        _updateNode(id, stepRun.TestStepName, verdict, verdict, null);
        _stepIndex++;

        if (_loops.Count > 0 && _loops.Peek().StepId == stepRun.TestStepId)
        {
            _loops.Pop();
        }

        ReportStepProgress(
            $"{stepRun.TestStepName}: {stepRun.Verdict}",
            stepRun.TestStepName,
            id,
            verdict,
            verdict);
    }

    private static bool IsFirstEnabledDirectChild(ITestStep child, ITestStep parent)
    {
        foreach (var sibling in parent.ChildTestSteps)
        {
            if (!sibling.Enabled)
            {
                continue;
            }

            return ReferenceEquals(sibling, child);
        }

        return false;
    }

    private void ReportStepProgress(
        string message,
        string? stepName,
        string stepId,
        string statusText,
        string verdict)
    {
        int? iterationIndex = null;
        int? iterationTotal = null;
        string? iterationText = null;
        if (_loops.Count > 0)
        {
            var loop = _loops.Peek();
            if (loop.Index > 0)
            {
                iterationIndex = loop.Index;
                iterationTotal = loop.Total;
                iterationText = OpenTapLoopProgress.FormatIteration(loop.Index, loop.Total);
            }
        }

        var denominator = Math.Max(_stepCount, _stepIndex);
        _progress?.Report(new OpenTapProgress
        {
            Message = message,
            StepName = stepName,
            StepId = stepId,
            StepPath = _resolvePath(stepId, stepName),
            StatusText = statusText,
            Verdict = verdict,
            OverallPercent = (double)_stepIndex / denominator * 100,
            IterationIndex = iterationIndex,
            IterationTotal = iterationTotal,
            IterationText = iterationText,
        });
    }

    public override void OnResultPublished(Guid stepRunId, ResultTable result)
    {
        try
        {
            if (string.Equals(result.Name, "Sample", StringComparison.OrdinalIgnoreCase))
            {
                PublishSamples(result);
                return;
            }

            if (string.Equals(result.Name, "Scalar", StringComparison.OrdinalIgnoreCase))
            {
                PublishScalars(result);
                return;
            }

            if (string.Equals(result.Name, "Identity", StringComparison.OrdinalIgnoreCase))
            {
                var idn = result.Columns.FirstOrDefault(c => c.Name == "Idn");
                var key = idn is null || idn.Data.Length == 0 ? null : Convert.ToString(idn.Data.GetValue(0));
                if (_currentStepId is not null)
                {
                    _updateNode(_currentStepId, _currentStepName, "Running", "NotSet", key);
                }

                _progress?.Report(new OpenTapProgress
                {
                    Message = $"IDN {key}",
                    StepId = _currentStepId,
                    StepName = _currentStepName,
                    KeyValue = key,
                    StatusText = "Identity",
                });
                return;
            }

            if (string.Equals(result.Name, "Analyze", StringComparison.OrdinalIgnoreCase))
            {
                var meanCol = result.Columns.FirstOrDefault(c => c.Name == "Mean");
                var key = meanCol is null || meanCol.Data.Length == 0
                    ? null
                    : $"mean={Convert.ToDouble(meanCol.Data.GetValue(0)):F4}";
                if (_currentStepId is not null)
                {
                    _updateNode(_currentStepId, _currentStepName, "Running", "NotSet", key);
                }

                _progress?.Report(new OpenTapProgress
                {
                    Message = key ?? "Analyze",
                    StepId = _currentStepId,
                    StepName = _currentStepName,
                    KeyValue = key,
                    StatusText = "Analyze",
                });
                return;
            }

            if (string.Equals(result.Name, "OperatorPrompt", StringComparison.OrdinalIgnoreCase))
            {
                var msgCol = result.Columns.FirstOrDefault(c => c.Name == "Message");
                var msg = msgCol is null || msgCol.Data.Length == 0
                    ? "Awaiting operator"
                    : Convert.ToString(msgCol.Data.GetValue(0));
                // Do not set AwaitingOperator here — HandleInteraction already reports the
                // authoritative pause (with InteractionRequest). Emitting a second
                // AwaitingOperator=true frame without a request races Progress handlers.
                _progress?.Report(new OpenTapProgress
                {
                    Message = msg ?? "Awaiting operator",
                    StepId = _currentStepId,
                    StepName = _currentStepName,
                    OperatorPromptMessage = msg,
                    StatusText = "Operator prompt noted",
                    KeyValue = msg,
                });
            }
        }
        catch
        {
            // Ignore malformed result rows.
        }
    }

    private void PublishSamples(ResultTable result)
    {
        var channelCol = result.Columns.FirstOrDefault(c => c.Name == "Channel");
        var valueCol = result.Columns.FirstOrDefault(c => c.Name == "Value");
        var indexCol = result.Columns.FirstOrDefault(c => c.Name == "Index");
        if (valueCol is null)
        {
            return;
        }

        var hints = CurrentPresentationHints();
        for (var i = 0; i < valueCol.Data.Length; i++)
        {
            var value = Convert.ToDouble(valueCol.Data.GetValue(i));
            var channel = channelCol is null ? "VDC" : Convert.ToString(channelCol.Data.GetValue(i)) ?? "VDC";
            var index = indexCol is null ? i : Convert.ToInt32(indexCol.Data.GetValue(i));
            var ts = _clock.UtcNow;
            var stepPath = _resolvePath(_currentStepId ?? string.Empty, _currentStepName) ?? string.Empty;
            int? iterationIndex = null;
            string? loopPath = null;
            if (_loops.Count > 0)
            {
                var loop = _loops.Peek();
                if (loop.Index > 0)
                {
                    iterationIndex = loop.Index;
                    loopPath = _resolvePath(loop.StepId.ToString(), null);
                }
            }

            var stored = new StoredSample
            {
                Channel = channel,
                Timestamp = ts,
                Value = value,
                StepPath = stepPath,
                IterationIndex = iterationIndex,
                LoopPath = loopPath,
            };
            OpenTapPresentation.ApplySample(stored, channel, hints);
            _samples.Add(stored);

            var sampleEvent = MeasurementSampleEvent.FromStored(stored, index);
            var percent = (double)_stepIndex / _stepCount * 100;
            var nowTicks = Stopwatch.GetTimestamp();
            var elapsed = nowTicks - _lastSampleReportTimestamp;
            var interval = SampleReportIntervalTicks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
            if (_lastSampleReportTimestamp == 0 || elapsed >= interval)
            {
                _lastSampleReportTimestamp = nowTicks;
                _coalescedSample = null;
                _progress?.Report(new OpenTapProgress
                {
                    Message = "Sample",
                    Sample = sampleEvent,
                    StepId = _currentStepId,
                    StepName = _currentStepName,
                    StepPath = stepPath,
                    KeyValue = FormatSampleKey(stored),
                    OverallPercent = percent,
                });
            }
            else
            {
                _coalescedSample = sampleEvent;
                _coalescedPercent = percent;
            }
        }
    }

    private void PublishScalars(ResultTable result)
    {
        var nameCol = result.Columns.FirstOrDefault(c => c.Name == "Name");
        var valueCol = result.Columns.FirstOrDefault(c => c.Name == "Value");
        var unitCol = result.Columns.FirstOrDefault(c => c.Name == "Unit");
        var limitLowCol = result.Columns.FirstOrDefault(c => c.Name == "LimitLow");
        var limitHighCol = result.Columns.FirstOrDefault(c => c.Name == "LimitHigh");
        if (valueCol is null)
        {
            return;
        }

        var hints = CurrentPresentationHints();
        var stepPath = _resolvePath(_currentStepId ?? string.Empty, _currentStepName) ?? string.Empty;
        for (var i = 0; i < valueCol.Data.Length; i++)
        {
            var value = Convert.ToDouble(valueCol.Data.GetValue(i));
            var name = nameCol is null || i >= nameCol.Data.Length
                ? "Scalar"
                : Convert.ToString(nameCol.Data.GetValue(i)) ?? "Scalar";
            var unit = unitCol is null || i >= unitCol.Data.Length
                ? string.Empty
                : Convert.ToString(unitCol.Data.GetValue(i)) ?? string.Empty;
            var limitLow = TryReadOptionalDouble(limitLowCol, i);
            var limitHigh = TryReadOptionalDouble(limitHighCol, i);
            var stored = new StoredSample
            {
                Timestamp = _clock.UtcNow,
                Value = value,
                StepPath = stepPath,
            };
            OpenTapPresentation.ApplyScalar(stored, name, unit, hints, limitLow, limitHigh);
            _samples.Add(stored);

            if (_currentStepId is not null)
            {
                _updateNode(_currentStepId, _currentStepName, "Running", "NotSet", FormatSampleKey(stored));
            }

            _progress?.Report(new OpenTapProgress
            {
                Message = FormatSampleKey(stored),
                StepId = _currentStepId,
                StepName = _currentStepName,
                StepPath = stepPath,
                KeyValue = FormatSampleKey(stored),
                StatusText = stored.DisplayRole ?? "Scalar",
                Sample = MeasurementSampleEvent.FromStored(stored),
                OverallPercent = (double)_stepIndex / Math.Max(_stepCount, 1) * 100,
            });
        }
    }

    private static double? TryReadOptionalDouble(ResultColumn? column, int index)
    {
        if (column is null || index >= column.Data.Length)
        {
            return null;
        }

        var raw = column.Data.GetValue(index);
        if (raw is null || raw is DBNull)
        {
            return null;
        }

        try
        {
            var value = Convert.ToDouble(raw);
            return double.IsNaN(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private OpenTapPresentation.MixinHints? CurrentPresentationHints()
    {
        if (_currentStepId is null || !Guid.TryParse(_currentStepId, out var id))
        {
            return null;
        }

        return OpenTapPresentation.TryReadMixin(OpenTapLoopProgress.FindStepById(_plan, id));
    }

    private static string FormatSampleKey(StoredSample sample)
    {
        var key = sample.EffectiveMetricKey;
        var role = string.IsNullOrWhiteSpace(sample.DisplayRole) ? null : sample.DisplayRole;
        var unit = string.IsNullOrWhiteSpace(sample.Unit) ? null : sample.Unit;
        var value = sample.Value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
        if (role is null && unit is null)
        {
            return $"{key}={value}";
        }

        if (unit is null)
        {
            return $"{key} [{role}] {value}";
        }

        if (role is null)
        {
            return $"{key}={value} {unit}";
        }

        return $"{key} [{role}] {value} {unit}";
    }

    private void FlushCoalescedSample()
    {
        if (_coalescedSample is null)
        {
            return;
        }

        var sample = _coalescedSample;
        var percent = _coalescedPercent;
        _coalescedSample = null;
        _lastSampleReportTimestamp = Stopwatch.GetTimestamp();
        _progress?.Report(new OpenTapProgress
        {
            Message = "Sample",
            Sample = sample,
            StepId = _currentStepId,
            StepName = _currentStepName,
            OverallPercent = percent,
        });
    }
}
