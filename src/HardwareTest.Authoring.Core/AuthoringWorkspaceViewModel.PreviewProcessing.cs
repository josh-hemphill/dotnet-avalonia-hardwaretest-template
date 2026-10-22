namespace HardwareTest.Authoring;

internal sealed record AuthoringPreviewInput(Guid Session, long Generation, string? PlanId,
    AuthoringDocumentSnapshot? Document, Guid? NodeId, RunDataset? Dataset,
    IReadOnlyList<AuthoringDocumentSnapshot> Documents)
{
    internal AuthoringRecordingSnapshot? Recording { get; init; }
    internal string? HomeOverride { get; init; }
    internal bool EnvironmentPending { get; init; }
}

internal sealed record AuthoringPreviewPresentation(IReadOnlyList<BoardPreviewTile> Tiles, MetricPreview Preview,
    AuthoringPreviewChrome Chrome, FormulaDeploymentStatus? Deployment, string RecordingEvidence,
    IReadOnlyList<string> ExcludedFormulas, string SourceDetails, string FormulaError, FormulaSaveOutcome FormulaSave)
{
    internal static AuthoringPreviewPresentation Empty { get; } = new([], MetricPreviewBuilder.Empty,
        AuthoringPreviewChromeBuilder.Empty, null, string.Empty, [],
        "Example data · simulated values · no hardware execution", string.Empty,
        new(FormulaSaveOutcomeKind.None, string.Empty));
}

public sealed partial class AuthoringWorkspaceViewModel
{
    private Action<Action>? _previewDispatch;
    private Func<bool>? _previewOwner;
    private Func<AuthoringPreviewInput, AuthoringPreviewPresentation> _previewEvaluate = EvaluatePreview;
    private AuthoringPreviewPresentation _previewPresentation = AuthoringPreviewPresentation.Empty;
    private AuthoringPreviewInput? _publishedPreviewInput;
    private long _previewGeneration;
    private bool _previewQueued;
    private bool _previewRunning;
    private bool _previewPending;
    private bool _previewStopped;
    private bool _previewBusy;
    private string? _previewPlan;
    private Guid _previewSession;
    private TaskCompletionSource _previewIdle = CompletedPreview();

    private AuthoringPreviewPresentation CachedPreview
        => _publishedPreviewInput is { } input && input.Generation == _previewGeneration && PreviewInputCurrent(input)
            ? _previewPresentation : AuthoringPreviewPresentation.Empty;

    private bool PreviewInputCurrent(AuthoringPreviewInput input)
        => input.Session == WorkspaceSessionId && input.PlanId == SelectedProgram?.PlanId
            && input.NodeId == SelectedPreviewNodeId && ReferenceEquals(input.Document, SelectedDocument?.Snapshot)
            && ReferenceEquals(input.Dataset, SelectedDataset) && input.HomeOverride == Prefs.OpenTapHomeOverride
            && input.EnvironmentPending == EnvironmentInspectionPending;

    public bool PreviewBusy { get => _previewBusy; private set => SetField(ref _previewBusy, value); }
    public Task WaitForPreviewAsync(CancellationToken cancellationToken = default)
        => _previewIdle.Task.WaitAsync(cancellationToken);

    /// Desktop callers enable asynchronous presentation before binding any controls.
    /// Synchronous CLI/domain callers retain the existing on-demand preview APIs.
    public void ConfigurePreviewProcessing(Action<Action> dispatch, Func<bool> ownerIsCurrent)
        => ConfigurePreviewProcessing(dispatch, ownerIsCurrent, EvaluatePreview);

    internal void ConfigurePreviewProcessing(Action<Action> dispatch, Func<bool> ownerIsCurrent,
        Func<AuthoringPreviewInput, AuthoringPreviewPresentation> evaluate)
    {
        _previewDispatch = dispatch;
        _previewOwner = ownerIsCurrent;
        _previewEvaluate = evaluate;
        _previewStopped = false;
        RequestPreviewRefresh();
    }

    public void StopPreviewProcessing()
    {
        _previewStopped = true;
        _previewGeneration++;
        _previewPending = false;
        PreviewBusy = false;
        _previewIdle.TrySetResult();
    }

    private void RequestPreviewRefresh()
    {
        if (_previewDispatch is null || _previewStopped) return;
        _previewGeneration++;
        _previewPending = true;
        if (_previewIdle.Task.IsCompleted) _previewIdle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PreviewBusy = true;
        // Selection can notify before its recording and node are restored. Capture once
        // after that transaction, rather than evaluating each intermediate notification.
        if (_previewQueued || _previewRunning) return;
        _previewQueued = true;
        _previewDispatch(StartPreviewEvaluation);
    }

    private void StartPreviewEvaluation()
    {
        _previewQueued = false;
        if (_previewStopped || _previewOwner?.Invoke() != true)
        {
            _previewPending = false;
            PreviewBusy = false;
            _previewIdle.TrySetResult();
            return;
        }
        _previewPending = false;
        _previewRunning = true;
        var plan = SelectedProgram?.PlanId;
        var dataset = SelectedDataset;
        var input = new AuthoringPreviewInput(WorkspaceSessionId, _previewGeneration, plan,
            SelectedDocument?.Snapshot, SelectedPreviewNodeId, dataset,
            _documents.Values.Select(document => document.Snapshot).ToArray())
        {
            Recording = dataset?.RecordingSnapshot,
            HomeOverride = Prefs.OpenTapHomeOverride,
            EnvironmentPending = EnvironmentInspectionPending
        };
        if (_previewPlan != plan || _previewSession != input.Session)
        {
            _previewPlan = plan;
            _previewSession = input.Session;
            _previewPresentation = AuthoringPreviewPresentation.Empty;
            _publishedPreviewInput = null;
            PublishPreviewProperties();
        }
        _ = EvaluatePreviewAsync(input);
    }

    private async Task EvaluatePreviewAsync(AuthoringPreviewInput input)
    {
        AuthoringPreviewPresentation presentation;
        try { presentation = await Task.Run(() => _previewEvaluate(input)).ConfigureAwait(false); }
        catch (Exception error)
        {
            presentation = AuthoringPreviewPresentation.Empty with
            { Preview = MetricPreviewBuilder.Empty with { Note = $"Preview unavailable: {error.Message}" } };
        }
        _previewDispatch!(() =>
        {
            _previewRunning = false;
            if (_previewStopped || _previewOwner?.Invoke() != true)
            {
                _previewPending = false;
                PreviewBusy = false;
                _previewIdle.TrySetResult();
                return;
            }
            var current = input.Generation == _previewGeneration && PreviewInputCurrent(input)
                && input.Documents.SequenceEqual(_documents.Values.Select(document => document.Snapshot));
            if (current)
            {
                _previewPresentation = presentation;
                _publishedPreviewInput = input;
                PublishPreviewProperties();
            }
            if (_previewPending || !current) RequestPreviewRefresh();
            else { PreviewBusy = false; _previewIdle.TrySetResult(); }
        });
    }

    private void PublishPreviewProperties()
    {
        foreach (var property in new[] { nameof(BoardTiles), nameof(Preview), nameof(PreviewKind), nameof(PreviewChrome),
            nameof(PreviewNote), nameof(DataSourceDetails), nameof(FormulaDeploymentStatus), nameof(FormulaDeploymentLabel),
            nameof(FormulaLoweringTarget), nameof(FormulaDeploymentRequirements), nameof(FormulaDeploymentMessage),
            nameof(FormulaRecordingEvidence), nameof(ExcludedExplorationFormulas), nameof(FormulaError),
            nameof(FormulaSaveNote), nameof(FormulaSaveOutcomeKind) }) OnPropertyChanged(property);
    }

    internal static AuthoringPreviewPresentation EvaluatePreview(AuthoringPreviewInput input)
    {
        // Snapshot.Restore performs all document cloning away from the dispatcher. The
        // source snapshots are owned values and never expose their private draft graph.
        var program = input.Document?.Restore();
        if (input.Dataset is not null && input.Recording is null)
            throw new AuthoringWorkspaceException("Refresh this recording before previewing its source.");
        var recording = input.Recording?.Restore();
        var tiles = BoardPreviewBuilder.Build(program, recording);
        var selected = FindPreviewMetric(program?.Measure ?? [], input.NodeId);
        var preview = tiles.LastOrDefault(tile => tile.NodeId == input.NodeId)?.Preview ?? MetricPreviewBuilder.Empty;
        var deployment = selected?.Source is ExpressionAlgorithm ? FormulaDeploymentClassifier.Classify(selected, program, nodeId: input.NodeId) : null;
        var evidence = string.Empty;
        if (selected?.Source is ExpressionAlgorithm)
        {
            if (recording is not null && recording.Samples.Any(sample => sample.StepRunId is not null || sample.IterationIndex is not null))
            {
                var executions = tiles.Where(tile => tile.NodeId == input.NodeId).ToArray();
                var available = executions.Count(tile => tile.Preview.CannedSamples.Count > 0);
                var issues = executions.Where(tile => tile.Preview.CannedSamples.Count == 0).Select(tile => tile.Preview.Note).Distinct();
                evidence = $"Recording evidence: {available} source execution(s) evaluated separately. {string.Join(" ", issues)} Deployment requirements remain separate.";
            }
            else evidence = FormulaDeploymentClassifier.DescribeRecording(selected, recording is null ? null : RunDatasetBinder.SeriesByMetric(recording));
        }
        var excluded = input.Documents.SelectMany(document =>
        {
            var draft = ReferenceEquals(document, input.Document) ? program! : document.Restore();
            var ids = AuthoringFormulaDeployment.ExcludedNodes(draft).ToHashSet();
            return AuthoringDependencyIndex.Build(draft).Nodes.Where(node => ids.Contains(node.NodeId))
                .Select(node => $"{draft.PlanId}: {node.ProducedChannel} (exploration)");
        }).ToArray();
        var error = selected?.Source is ExpressionAlgorithm expression && !FormulaParser.TryParse(expression.Source, out _, out var parseError)
            ? parseError ?? string.Empty : string.Empty;
        var save = selected?.Source is ExpressionAlgorithm expr && error.Length == 0
            ? FormulaLowerer.DescribeSaveOutcome(expr.Source, selected.Limits) : new FormulaSaveOutcome(FormulaSaveOutcomeKind.None, string.Empty);
        var source = AuthoringPreviewPresentation.Empty.SourceDetails;
        if (recording is not null)
        {
            var time = recording.Samples.Count > 0 && recording.Samples.All(sample => sample.ElapsedMs is { } elapsed && double.IsFinite(elapsed)) ? "elapsed time available" : "elapsed time missing or incomplete";
            source = $"Recording · {input.Dataset!.Path}\nRun {recording.RunId} · DUT {(string.IsNullOrWhiteSpace(recording.DutSerial) ? "unspecified" : recording.DutSerial)} · {recording.Samples.Count} samples · {time}\nChannels: {string.Join(", ", recording.Samples.Select(sample => sample.EffectiveMetricKey).Distinct(StringComparer.OrdinalIgnoreCase))} · no hardware execution";
        }
        return new(tiles, preview, AuthoringPreviewChromeBuilder.From(preview, recording?.Events), deployment, evidence, excluded, source, error, save);
    }

    private static MetricDraft? FindPreviewMetric(IReadOnlyList<MeasureNode> nodes, Guid? nodeId)
    {
        foreach (var node in nodes)
        {
            if (node is MetricNode metric && node.NodeId == nodeId) return metric.Metric;
            var children = node switch { RepeatNode repeat => repeat.Children, SectionNode section => section.Children, _ => null };
            if (children is not null && FindPreviewMetric(children, nodeId) is { } found) return found;
        }
        return null;
    }

    private static TaskCompletionSource CompletedPreview()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
