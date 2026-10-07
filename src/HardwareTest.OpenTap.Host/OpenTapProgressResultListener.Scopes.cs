using HardwareTest.Core.Runs;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

internal sealed partial class ProgressResultListener
{
    private sealed record Execution(Guid Producer, string Path, string? Loop, int? Iteration, Guid? LoopRunId);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Execution> _executions = new();

    private void RememberExecution(TestStepRun run)
    {
        var loop = _loops.Count > 0 ? _loops.Peek() : null;
        _executions[run.Id] = new(run.TestStepId, _resolvePath(run.TestStepId.ToString(), run.TestStepName) ?? string.Empty,
            loop is null ? null : _resolvePath(loop.StepId.ToString(), null), loop?.Index > 0 ? loop.Index : null, loop?.RunId);
    }

    private OpenTapPresentation.MixinHints? ExecutionPresentationHints(Guid stepRunId)
        => _executions.TryGetValue(stepRunId, out var execution)
            ? OpenTapPresentation.TryReadMixin(OpenTapLoopProgress.FindStepById(_plan, execution.Producer))
            : null;

    private void StampExecution(StoredSample sample, Guid stepRunId)
    {
        if (!_executions.TryGetValue(stepRunId, out var execution)) return;
        sample.ProducerStepId = execution.Producer;
        sample.StepRunId = stepRunId;
        sample.LoopRunId = execution.LoopRunId;
        sample.StepPath = execution.Path;
        sample.LoopPath = execution.Loop;
        sample.IterationIndex = execution.Iteration;
    }
    private void StampExecution(StoredEvent mark, Guid stepRunId)
    {
        if (!_executions.TryGetValue(stepRunId, out var execution)) return;
        mark.ProducerStepId = execution.Producer;
        mark.StepRunId = stepRunId;
        mark.LoopRunId = execution.LoopRunId;
        mark.StepPath = execution.Path;
        mark.LoopPath = execution.Loop;
        mark.IterationIndex = execution.Iteration;
    }

}
