using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;

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
    private string? _pendingStatus;
    private double _pendingPercent;
    private bool _pendingForceFlush;
    private bool _pendingAwaitingOperator;
    private string? _pendingOperatorPrompt;
    private OperatorInteractionRequest? _pendingInteractionRequest;
    private string? _pendingStepId;
    private string? _pendingStepPath;
    private string? _pendingStepName;
    private string? _pendingStatusText;
    private string? _pendingVerdict;
    private string? _pendingKeyValue;
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
                _pendingSample = sample;
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

            _pendingStepId = progress.StepId ?? _pendingStepId;
            _pendingStepPath = progress.StepPath ?? _pendingStepPath;
            _pendingStepName = progress.StepName ?? _pendingStepName;
            _pendingStatusText = progress.StatusText ?? _pendingStatusText;
            _pendingVerdict = progress.Verdict ?? _pendingVerdict;
            _pendingKeyValue = progress.KeyValue ?? _pendingKeyValue;
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

            if (progress.Sample is not null && !string.IsNullOrWhiteSpace(progress.StepPath))
            {
                _pendingSampleStepPath = progress.StepPath;
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
            _pendingSample = null;
            _pendingStatus = null;
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

    private async Task RunOnUiAsync(Action action)
    {
        if (UiScheduler is not null)
        {
            UiScheduler(action);
            return;
        }

        try
        {
            var dispatcher = Dispatcher.UIThread;
            if (dispatcher.CheckAccess() || Avalonia.Application.Current is null)
            {
                action();
                return;
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatcher.Post(
                () =>
                {
                    try
                    {
                        action();
                        tcs.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                },
                DispatcherPriority.Normal);
            await tcs.Task.ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            action();
        }
    }

    private async Task WaitForPendingFlushesAsync()
    {
        for (var i = 0; i < 50; i++)
        {
            if (Volatile.Read(ref _flushScheduled) == 0)
            {
                bool needsForce;
                lock (_progressSync)
                {
                    needsForce = _pendingForceFlush || _pendingSample is not null || _pendingDetails.Count > 0;
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

    private void PostToUi(Action action)
    {
        if (UiScheduler is not null)
        {
            UiScheduler(action);
            return;
        }

        try
        {
            var dispatcher = Dispatcher.UIThread;
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Post(action, DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            // Dispatcher is unavailable (e.g., application shutting down).
            // Do NOT run action() on the current (background) thread — that would mutate
            // UI-bound state off the UI thread. Log and no-op instead.
            Debug.WriteLine($"[PostToUi] Dispatcher unavailable; dropping UI flush. {ex.GetType().Name}: {ex.Message}");
        }
    }

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
                        _ =>
                        {
                            Interlocked.Exchange(ref _flushScheduled, 0);
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
                    if (_pendingForceFlush || _pendingSample is not null || _pendingAwaitingOperator)
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
                Interlocked.Exchange(ref _flushScheduled, 0);
                lock (_progressSync)
                {
                    if (_pendingForceFlush
                        || _pendingSample is not null
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
            if (_runControl.IsPaused)
            {
                Status = $"Paused — {frame.Status}";
            }
            else if (frame.Awaiting)
            {
                Status = $"Awaiting — {frame.Status}";
            }
            else
            {
                Status = frame.Status;
            }
        }

        if (frame.Awaiting)
        {
            Interaction.IsAwaitingOperator = true;
            Interaction.Apply(frame.InteractionRequest ?? _runSession.PendingInteraction, frame.Prompt);
            // Growing operator card shrinks the step list; keep the awaiting step visible.
            ScheduleScrollToCurrentStep();
        }

        if (frame.Details is not null)
        {
            StepDetail.AppendDetailLines(frame.Details, MaxDetailLinesPerFlush);
        }

        SyncHierarchyLive();
        if (!string.IsNullOrWhiteSpace(frame.StepName))
        {
            CurrentStepName = frame.StepName;
        }

        if (!string.IsNullOrWhiteSpace(frame.StepPath))
        {
            CurrentStepPath = frame.StepPath;
        }

        ApplyPendingStepLive(frame);
        if (frame.IterationText is not null)
        {
            IterationText = frame.IterationText;
        }

        RefreshHero();

        if (frame.Sample is not null)
        {
            if (Live.ApplySample(frame.Sample, frame.SampleStepPath, frame.StepPath, StepTree.SelectedStep))
            {
                PlotUiFlushCount++;
            }
        }
        else if (frame.Force)
        {
            PlotUiFlushCount++;
        }
    }

    private void ApplyPendingStepLive(PendingFrame frame)
    {
        var vm = StepTree.ApplyLiveStep(
            frame.StepId,
            frame.StepPath,
            frame.StatusText,
            frame.Verdict,
            frame.KeyValue);
        if (vm is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(frame.StepPath))
        {
            CurrentStepPath = frame.StepPath;
        }

        if (!string.IsNullOrWhiteSpace(vm.Name))
        {
            CurrentStepName = vm.Name;
        }

        StepTree.RollupParentStatuses();
        RefreshHero();
        if (string.Equals(vm.ChipText, "Fail", StringComparison.Ordinal))
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
                Force = _pendingForceFlush,
                Awaiting = _pendingAwaitingOperator,
                Prompt = _pendingOperatorPrompt,
                InteractionRequest = _pendingInteractionRequest,
                Details = DequeueDetailBatch_NoLock(MaxDetailLinesPerFlush),
                StepId = _pendingStepId,
                StepPath = _pendingStepPath,
                StepName = _pendingStepName,
                StatusText = _pendingStatusText,
                Verdict = _pendingVerdict,
                KeyValue = _pendingKeyValue,
                IterationText = _pendingIterationText,
            };

            _pendingForceFlush = false;
            _pendingAwaitingOperator = false;
            _pendingOperatorPrompt = null;
            _pendingInteractionRequest = null;
            _pendingStepId = null;
            _pendingStepPath = null;
            _pendingStepName = null;
            _pendingStatusText = null;
            _pendingVerdict = null;
            _pendingKeyValue = null;
            _pendingIterationText = null;
            _pendingSampleStepPath = null;
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
            if (frame.Sample is not null)
            {
                _pendingSample ??= frame.Sample;
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

            _pendingStepId ??= frame.StepId;
            _pendingStepPath ??= frame.StepPath;
            _pendingStepName ??= frame.StepName;
            _pendingStatusText ??= frame.StatusText;
            _pendingVerdict ??= frame.Verdict;
            if (frame.KeyValue is not null)
            {
                _pendingKeyValue ??= frame.KeyValue;
            }

            if (frame.IterationText is not null)
            {
                _pendingIterationText ??= frame.IterationText;
            }
        }
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
            _pendingDetails.Dequeue();
        }

        _pendingDetails.Enqueue(line);
    }

    /// One coalesced progress snapshot handed from the ingest lock to the UI thread.
    private sealed class PendingFrame
    {
        public string? Status { get; init; }
        public double Percent { get; init; }
        public MeasurementSampleEvent? Sample { get; init; }
        public string? SampleStepPath { get; init; }
        public bool Force { get; init; }
        public bool Awaiting { get; init; }
        public string? Prompt { get; init; }
        public OperatorInteractionRequest? InteractionRequest { get; init; }
        public List<string>? Details { get; init; }
        public string? StepId { get; init; }
        public string? StepPath { get; init; }
        public string? StepName { get; init; }
        public string? StatusText { get; init; }
        public string? Verdict { get; init; }
        public string? KeyValue { get; init; }
        public string? IterationText { get; init; }
    }
}
