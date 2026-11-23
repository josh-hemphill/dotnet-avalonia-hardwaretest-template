using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HardwareTest.Features.Presentation;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.UiThreading;

namespace HardwareTest.Features.RunTest;

/// Progress ingest and the throttled UI flush pump. Stays on the coordinator so children are dispatcher-agnostic.
public partial class RunTestViewModel
{
    private const int DetailCap = 200;
    private const int MaxDetailLinesPerFlush = 16;

    private readonly object _progressSync = new();
    private readonly Queue<string> _pendingDetails = new();

    private long _lastUiFlushTicks;
    private MeasurementSampleEvent? _pendingSample;
    private string? _pendingSampleStepPath;
    private readonly Dictionary<string, (MeasurementSampleEvent Sample, string? Path)> _pendingBands =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<MeasurementEventMark> _pendingEvents = new();
    private string? _pendingStatus;
    private double _pendingPercent;
    private bool _pendingForceFlush;
    private bool _pendingAwaitingOperator;
    private string? _pendingOperatorPrompt;
    private OperatorInteractionRequest? _pendingInteractionRequest;
    private readonly Dictionary<string, StepUpdate> _pendingSteps = new(StringComparer.OrdinalIgnoreCase);
    private string? _pendingStepPath;
    private string? _pendingStepName;
    private string? _pendingIterationText;
    private int _flushScheduled;

    public int PlotUiFlushCount { get; private set; }

    /// Test seam: routes UI work synchronously instead of through the Avalonia dispatcher.
    public Action<Action>? UiScheduler { get; set; }

    public void IngestProgress(OpenTapProgress progress)
    {
        lock (_progressSync)
        {
            _pendingPercent = progress.OverallPercent;
            if (!string.IsNullOrWhiteSpace(progress.Message))
            {
                _pendingStatus = progress.Message;
            }

            if (progress.Sample is { } sample)
            {
                if (PresentationRoleMap.IsRunGaugeSample(sample))
                {
                    _pendingBands[sample.EffectiveMetricKey] = (sample, progress.StepPath);
                }
                else
                {
                    _pendingSample = sample;
                    _pendingSampleStepPath = progress.StepPath;
                }
            }

            if (progress.Event is { } mark)
            {
                _pendingEvents.Enqueue(mark);
                _pendingForceFlush = true;
            }

            if (!string.IsNullOrWhiteSpace(progress.StepName))
            {
                EnqueueDetail_NoLock($"{progress.StepName}: {progress.Message}");
            }

            if (progress.AwaitingOperator)
            {
                _pendingAwaitingOperator = true;
                _pendingOperatorPrompt = progress.OperatorPromptMessage ?? progress.Message;
                _pendingInteractionRequest = progress.InteractionRequest ?? _runSession.PendingInteraction;
            }

            _pendingStepPath = progress.StepPath ?? _pendingStepPath;
            _pendingStepName = progress.StepName ?? _pendingStepName;
            RememberStepUpdate(progress);
            if (progress.IterationText is not null || progress.IterationIndex is not null)
            {
                _pendingIterationText = progress.IterationText
                    ?? OpenTapLoopProgress.FormatIteration(
                        progress.IterationIndex ?? 0,
                        progress.IterationTotal);
                _pendingForceFlush = true;
            }

            if (progress.Sample is null
                && (!string.IsNullOrWhiteSpace(progress.StepPath) || !string.IsNullOrWhiteSpace(progress.StepName))
                && (!string.IsNullOrWhiteSpace(progress.StatusText)
                    || !string.IsNullOrWhiteSpace(progress.Verdict)
                    || progress.AwaitingOperator))
            {
                _pendingForceFlush = true;
            }

            if (progress.IsCompleted)
            {
                _pendingForceFlush = true;
                _pendingAwaitingOperator = false;
            }
        }

        ScheduleUiFlush();
    }

    private void ResetPumpForRun()
    {
        PlotUiFlushCount = 0;
        _lastUiFlushTicks = 0;
        lock (_progressSync)
        {
            _pendingDetails.Clear();
            _pendingEvents.Clear();
            _pendingBands.Clear();
            _pendingSample = null;
            _pendingSampleStepPath = null;
            _pendingStatus = null;
            _pendingPercent = 0;
            _pendingSteps.Clear();
            _pendingStepPath = null;
            _pendingStepName = null;
            _pendingIterationText = null;
            _pendingForceFlush = false;
            _pendingAwaitingOperator = false;
            _pendingOperatorPrompt = null;
            _pendingInteractionRequest = null;
        }
    }

    private void ForceUiFlush()
    {
        lock (_progressSync)
        {
            _pendingForceFlush = true;
        }

        ScheduleUiFlush();
    }

    private void ClearPendingOperatorState()
    {
        lock (_progressSync)
        {
            _pendingAwaitingOperator = false;
            _pendingOperatorPrompt = null;
            _pendingInteractionRequest = null;
        }
    }

    /// Marshals onto the UI thread (or <see cref="UiScheduler"/>). When the dispatcher is
    /// unavailable under a live app, drops the action instead of mutating off-thread.
    private Task RunOnUiAsync(Action action) => UiDispatch.RunAsync(action, UiScheduler);

    private async Task WaitForPendingFlushesAsync()
    {
        for (var i = 0; i < 50; i++)
        {
            if (Volatile.Read(ref _flushScheduled) == 0)
            {
                bool needsForce;
                lock (_progressSync)
                {
                    needsForce = _pendingForceFlush || _pendingSample is not null || _pendingBands.Count > 0 || _pendingSteps.Count > 0 || _pendingDetails.Count > 0;
                }

                if (!needsForce)
                {
                    return;
                }

                ScheduleUiFlush();
            }

            await Task.Delay(5).ConfigureAwait(false);
        }

        await RunOnUiAsync(DrainUiFlush).ConfigureAwait(false);
    }

    private void ScheduleUiFlush()
    {
        if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0)
        {
            return;
        }

        PostToUi(DrainUiFlush);
    }

    private void PostToUi(Action action) => UiDispatch.Post(action, UiScheduler);

    private void DrainUiFlush()
    {
        var keepScheduledForDelay = false;
        try
        {
            while (true)
            {
                var frame = TakePendingFrame();

                var hz = Math.Clamp(_settings.PlotRefreshHz, 1, 120);
                var intervalTicks = Stopwatch.Frequency / hz;
                var now = Stopwatch.GetTimestamp();
                if (!frame.Force && _lastUiFlushTicks != 0 && now - _lastUiFlushTicks < intervalTicks)
                {
                    var delayMs = Math.Max(
                        1,
                        (int)((_lastUiFlushTicks + intervalTicks - now) * 1000.0 / Stopwatch.Frequency));
                    RequeueFrame(frame);
                    keepScheduledForDelay = true;
                    _ = Task.Delay(delayMs).ContinueWith(
                        completedDelay =>
                        {
                            _ = Interlocked.Exchange(ref _flushScheduled, 0);
                            ScheduleUiFlush();
                        },
                        TaskScheduler.Default);
                    return;
                }

                _lastUiFlushTicks = now;
                PublishFrame(frame);

                lock (_progressSync)
                {
                    // Leftover detail lines are flushed on a later UI frame (see finally).
                    if (_pendingForceFlush || _pendingSample is not null || _pendingBands.Count > 0 || _pendingSteps.Count > 0 || _pendingEvents.Count > 0 || _pendingAwaitingOperator)
                    {
                        continue;
                    }
                }

                break;
            }
        }
        finally
        {
            if (!keepScheduledForDelay)
            {
                _ = Interlocked.Exchange(ref _flushScheduled, 0);
                lock (_progressSync)
                {
                    if (_pendingForceFlush
                        || _pendingSample is not null
                        || _pendingBands.Count > 0
                        || _pendingSteps.Count > 0
                        || _pendingEvents.Count > 0
                        || _pendingDetails.Count > 0
                        || _pendingAwaitingOperator)
                    {
                        ScheduleUiFlush();
                    }
                }
            }
        }
    }

    private void PublishFrame(PendingFrame frame)
    {
        OverallPercent = frame.Percent;
        if (frame.Status is not null)
        {
            Status = _runControl.IsPaused ? $"Paused — {frame.Status}" : frame.Awaiting ? $"Awaiting — {frame.Status}" : frame.Status;
        }

        if (frame.Awaiting)
        {
            Interaction.IsAwaitingOperator = true;
            Interaction.Apply(frame.InteractionRequest ?? _runSession.PendingInteraction, frame.Prompt);
        }

        if (frame.Details is not null)
        {
            _ = StepDetail.AppendDetailLines(frame.Details, MaxDetailLinesPerFlush);
        }

        SyncHierarchyLive();
        if (!PinWaitingStep())
        {
            if (!string.IsNullOrWhiteSpace(frame.StepName))
                CurrentStepName = frame.StepName;
            if (!string.IsNullOrWhiteSpace(frame.StepPath))
                CurrentStepPath = frame.StepPath;
        }

        if (frame.Awaiting)
        {
            // Keep the operator's stage selection until they choose Show this step.
            ScheduleScrollToCurrentStep(changeScope: false);
        }

        ApplyPendingStepUpdates(frame.StepUpdates);
        if (frame.IterationText is not null)
        {
            IterationText = frame.IterationText;
        }

        RefreshHero();

        if (frame.Events is { Count: > 0 })
        {
            foreach (var mark in frame.Events)
            {
                Live.ApplyEvent(mark);
            }
        }

        var refreshPlot = frame.Sample is not null
            && Live.ApplySample(frame.Sample, frame.SampleStepPath, frame.StepPath, StepTree.SelectedStep);
        foreach (var (sample, path) in frame.Bands)
        {
            refreshPlot |= Live.ApplySample(sample, path, frame.StepPath, StepTree.SelectedStep);
        }

        if (refreshPlot || (frame.Force && frame.Sample is null))
        {
            PlotUiFlushCount++;
        }
    }

    private void RememberStepUpdate(OpenTapProgress progress)
    {
        var key = progress.StepId ?? progress.StepPath;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        // Measurement and event labels describe evidence. The step verdict describes execution.
        var status = progress.Sample is null && progress.Event is null ? progress.StatusText : null;
        if (status is null && progress.Verdict is null && progress.KeyValue is null)
        {
            return;
        }

        var update = new StepUpdate(progress.StepId, progress.StepPath, status, progress.Verdict, progress.KeyValue);
        _pendingSteps[key] = _pendingSteps.TryGetValue(key, out var earlier) ? update.WithFallback(earlier) : update;
    }

    private void ApplyPendingStepUpdates(IReadOnlyList<StepUpdate> updates)
    {
        if (updates.Count == 0)
        {
            return;
        }

        var hasFailure = false;
        foreach (var update in updates)
        {
            var step = StepTree.ApplyLiveStep(update.Id, update.Path, update.Status, update.Verdict, update.KeyValue);
            hasFailure |= step?.ChipText == "Fail";
        }

        StepTree.RollupParentStatuses();
        if (hasFailure)
        {
            StepTree.MaybeAutoFocusFail();
        }
    }

    private PendingFrame TakePendingFrame()
    {
        lock (_progressSync)
        {
            var frame = new PendingFrame
            {
                Status = _pendingStatus,
                Percent = _pendingPercent,
                Sample = _pendingSample,
                SampleStepPath = _pendingSampleStepPath,
                Bands = [.. _pendingBands.Values],
                Events = DequeueEvents_NoLock(),
                Force = _pendingForceFlush,
                Awaiting = _pendingAwaitingOperator,
                Prompt = _pendingOperatorPrompt,
                InteractionRequest = _pendingInteractionRequest,
                Details = DequeueDetailBatch_NoLock(MaxDetailLinesPerFlush),
                StepUpdates = [.. _pendingSteps.Values],
                StepPath = _pendingStepPath,
                StepName = _pendingStepName,
                IterationText = _pendingIterationText,
            };

            _pendingForceFlush = false;
            _pendingAwaitingOperator = false;
            _pendingOperatorPrompt = null;
            _pendingInteractionRequest = null;
            _pendingSteps.Clear();
            _pendingStepPath = null;
            _pendingStepName = null;
            _pendingIterationText = null;
            _pendingSampleStepPath = null;
            _pendingBands.Clear();
            if (frame.Sample is not null)
            {
                _pendingSample = null;
            }

            return frame;
        }
    }

    /// Puts a throttled frame back so the next flush window publishes it instead of dropping it.
    private void RequeueFrame(PendingFrame frame)
    {
        lock (_progressSync)
        {
            if (frame.Events is { Count: > 0 })
            {
                foreach (var mark in frame.Events)
                {
                    _pendingEvents.Enqueue(mark);
                }
            }

            if (frame.Sample is not null && _pendingSample is null)
            {
                _pendingSample = frame.Sample;
                _pendingSampleStepPath = frame.SampleStepPath;
            }

            foreach (var band in frame.Bands)
            {
                _ = _pendingBands.TryAdd(band.Sample.EffectiveMetricKey, band);
            }

            if (frame.Details is not null)
            {
                foreach (var line in frame.Details)
                {
                    EnqueueDetail_NoLock(line);
                }
            }

            if (frame.Force)
            {
                _pendingForceFlush = true;
            }

            if (frame.Awaiting)
            {
                _pendingAwaitingOperator = true;
                _pendingOperatorPrompt = frame.Prompt;
                _pendingInteractionRequest = frame.InteractionRequest;
            }

            _pendingStepPath ??= frame.StepPath;
            _pendingStepName ??= frame.StepName;
            foreach (var update in frame.StepUpdates)
            {
                var key = (update.Id ?? update.Path)!;
                _pendingSteps[key] = _pendingSteps.TryGetValue(key, out var newer) ? newer.WithFallback(update) : update;
            }

            if (frame.IterationText is not null)
            {
                _pendingIterationText ??= frame.IterationText;
            }
        }
    }

    private List<MeasurementEventMark>? DequeueEvents_NoLock()
    {
        if (_pendingEvents.Count == 0)
        {
            return null;
        }

        var batch = new List<MeasurementEventMark>(_pendingEvents.Count);
        while (_pendingEvents.Count > 0)
        {
            batch.Add(_pendingEvents.Dequeue());
        }

        return batch;
    }

    private List<string>? DequeueDetailBatch_NoLock(int maxCount)
    {
        if (_pendingDetails.Count == 0 || maxCount <= 0)
        {
            return null;
        }

        var batch = new List<string>(Math.Min(maxCount, _pendingDetails.Count));
        while (batch.Count < maxCount && _pendingDetails.Count > 0)
        {
            batch.Add(_pendingDetails.Dequeue());
        }

        return batch;
    }

    private void EnqueueDetail_NoLock(string line)
    {
        while (_pendingDetails.Count >= DetailCap)
        {
            _ = _pendingDetails.Dequeue();
        }

        _pendingDetails.Enqueue(line);
    }

    private sealed record StepUpdate(string? Id, string? Path, string? Status, string? Verdict, string? KeyValue)
    {
        public StepUpdate WithFallback(StepUpdate earlier) => new(
            Id ?? earlier.Id, Path ?? earlier.Path, Status ?? earlier.Status,
            Verdict ?? earlier.Verdict, KeyValue ?? earlier.KeyValue);
    }

    /// One coalesced progress snapshot handed from the ingest lock to the UI thread.
    private sealed class PendingFrame
    {
        public string? Status { get; init; }
        public double Percent { get; init; }
        public MeasurementSampleEvent? Sample { get; init; }
        public string? SampleStepPath { get; init; }
        public (MeasurementSampleEvent Sample, string? Path)[] Bands { get; init; } = [];
        public List<MeasurementEventMark>? Events { get; init; }
        public bool Force { get; init; }
        public bool Awaiting { get; init; }
        public string? Prompt { get; init; }
        public OperatorInteractionRequest? InteractionRequest { get; init; }
        public List<string>? Details { get; init; }
        public StepUpdate[] StepUpdates { get; init; } = [];
        public string? StepPath { get; init; }
        public string? StepName { get; init; }
        public string? IterationText { get; init; }
    }
}
