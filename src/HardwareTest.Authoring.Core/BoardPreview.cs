using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public sealed record BoardPreviewTile(Guid NodeId, string Title, string Scope, MetricPreview Preview,
    AuthoringPreviewChrome Chrome);

/// Evaluates preceding dependency chains; each producer execution owns its own sample input.
public static partial class BoardPreviewBuilder
{
    public static IReadOnlyList<BoardPreviewTile> Build(ProgramDraft? program, TestRunRecord? recording = null)
    {
        if (program is null) return [];
        var tiles = new List<BoardPreviewTile>();
        void Scope(IReadOnlyList<MeasureNode> nodes)
        {
            var available = new Dictionary<string, IReadOnlyList<IReadOnlyList<StoredSample>>>(StringComparer.OrdinalIgnoreCase);
            var metrics = nodes.OfType<MetricNode>().Select(node => node.Metric).ToArray();
            foreach (var node in nodes)
            {
                if (node is RepeatNode repeat) { Scope(repeat.Children); continue; }
                if (node is RawStepNode raw)
                {
                    AddRaw(raw, recording, available, tiles);
                    continue;
                }
                if (node is not MetricNode metricNode) continue;
                var metric = metricNode.Metric;
                var groups = new List<IReadOnlyList<StoredSample>>();
                try
                {
                    if (recording is { IsSchemaReadOnly: true }) throw new AuthoringWorkspaceException($"Unsupported recording schema {recording.StoredSchemaVersion}.");
                    if (recording is not null && !string.IsNullOrWhiteSpace(recording.PlanId) && !string.Equals(recording.PlanId, program.PlanId, StringComparison.OrdinalIgnoreCase))
                        throw new AuthoringWorkspaceException("Recording belongs to another program.");
                    if (recording is null) PlanCompiler.RequireCompleteInput(node.NodeId, program.AuthoringState.IncompleteNumericText);
                    if (!PlanCompiler.PreviewEnabled(metric))
                    {
                        Add(metricNode, MetricPreviewBuilder.Empty with { ChannelKey = metric.ChannelKey, YUnit = metric.YUnit, Note = "Step is disabled in the source plan." }, recording is null ? "Example" : "Recording");
                        continue; // Disabled nodes do not replace a preceding enabled publisher's binding.
                    }
                    var inputs = Inputs(metric);
                    if (inputs.Count == 0)
                    {
                        if (recording is not null)
                        {
                            groups.AddRange(RunDatasetBinder.SeriesForProducer(recording, metric.ChannelKey, node.NodeId));
                            if (groups.Count == 0) throw new AuthoringWorkspaceException($"Missing recording channel '{metric.ChannelKey}' for this acquisition.");
                        }
                        else groups.Add(Example(metric));
                    }
                    else
                    {
                        if (recording is null && metric.Source is ExpressionAlgorithm && PlanCompiler.ScalarMeanPreviewExample(FormulaDeploymentClassifier.DeploymentContext(metric, program, node.NodeId), metric.ChannelKey, node.NodeId) is not null)
                        {
                            Add(metricNode, MetricPreviewBuilder.From(metric, null, null, program, node.NodeId), "Example");
                            continue;
                        }
                        // Recordings supply values, never a substitute for compiler publisher capabilities or scope.
                        PlanCompiler.ValidateFormulaInput(FormulaDeploymentClassifier.DeploymentContext(metric, program, node.NodeId).Measure, metric.ChannelKey, program.AuthoringState, node.NodeId);
                        var key = inputs[0];
                        if (!available.TryGetValue(key, out var inputGroups) || inputGroups.Count == 0)
                        {
                            if (recording is null)
                            {
                                var example = MetricPreviewBuilder.From(metric, null, null, program, node.NodeId);
                                if (example.CannedSamples.Count > 0) { Add(metricNode, example, "Example"); continue; }
                            }
                            throw new AuthoringWorkspaceException($"Missing input channel '{key}' for '{metric.ChannelKey}'.");
                        }
                        foreach (var input in inputGroups)
                        {
                            var bound = new Dictionary<string, IReadOnlyList<StoredSample>>(StringComparer.OrdinalIgnoreCase) { [key] = input };
                            foreach (var other in inputs.Skip(1))
                            {
                                if (!available.TryGetValue(other, out var otherGroups) || otherGroups.Count != 1)
                                    throw new AuthoringWorkspaceException($"Missing or ambiguous input channel '{other}'.");
                                bound[other] = otherGroups[0];
                            }
                            var preview = MetricPreviewBuilder.From(metric, metrics, bound);
                            if (preview.CannedSamples.Count == 0) throw new AuthoringWorkspaceException(preview.Note ?? "Calculation has no samples.");
                            var first = input[0];
                            groups.Add(preview.CannedSamples.Select((value, index) => new StoredSample
                            {
                                Channel = metric.ChannelKey,
                                MetricKey = metric.ChannelKey,
                                Value = value,
                                Unit = preview.YUnit,
                                ElapsedMs = index < preview.SampleElapsedMs.Count ? preview.SampleElapsedMs[index] : null,
                                ProducerStepId = input.Select(sample => sample.ProducerStepId).Distinct().Count() == 1 ? first.ProducerStepId : null,
                                StepRunId = input.Select(sample => sample.StepRunId).Distinct().Count() == 1 ? first.StepRunId : null,
                                LoopRunId = first.LoopRunId,
                                LoopPath = first.LoopPath,
                                IterationIndex = first.IterationIndex,
                            }).ToArray());
                            Add(metricNode, preview with { Note = recording is null ? null : preview.Note }, recording is null ? "Example" : "Computed offline from " + Describe(first, false), input);
                        }
                        available[metric.ChannelKey] = groups;
                        continue;
                    }
                    foreach (var group in groups)
                    {
                        var bound = new Dictionary<string, IReadOnlyList<StoredSample>>(StringComparer.OrdinalIgnoreCase) { [metric.ChannelKey] = group };
                        var preview = MetricPreviewBuilder.From(metric, metrics, bound);
                        preview = preview with { Passed = PublisherVerdict(metric, group) };
                        Add(metricNode, preview with
                        {
                            YUnit = group[0].Unit ?? metric.YUnit,
                            Note = recording is null ? "Example data; no hardware execution." : "Recording samples; no hardware execution."
                        }, Describe(group[0], recording is null), group);
                    }
                    BindPublisherGroups(metric.ChannelKey, PlanCompiler.PreviewStep(metric) is PublishTimedSampleStep, groups, recording, available);
                }
                catch (Exception error) when (error is AuthoringWorkspaceException or InvalidOperationException or FormatException or OverflowException or global::OpenTap.TestPlan.PlanLoadException or System.Xml.XmlException)
                {
                    available[metric.ChannelKey] = [];
                    Add(metricNode, MetricPreviewBuilder.Empty with { ChannelKey = metric.ChannelKey, YUnit = metric.YUnit, Note = error.Message }, recording is null ? "Example" : "Recording");
                }
            }
        }
        void Add(MetricNode node, MetricPreview preview, string scope, IReadOnlyList<StoredSample>? group = null)
            => tiles.Add(new(node.NodeId, node.Metric.Name, scope, preview, AuthoringPreviewChromeBuilder.From(preview, EventsForGroup(recording, group))));
        Scope(program.Measure);
        return tiles;
    }

    private static IReadOnlyList<StoredEvent>? EventsForGroup(TestRunRecord? recording, IReadOnlyList<StoredSample>? group)
    {
        if (recording is null) return null;
        if (group is null || group.Count == 0) return [];
        // Legacy marks have no execution identity; do not infer it from names or paths.
        if (!recording.Events.Any(mark => mark.StepRunId is not null || mark.LoopRunId is not null)) return recording.Events;
        var first = group[0];
        if (first.LoopRunId is not null)
            return recording.Events.Where(mark => mark.LoopRunId == first.LoopRunId && mark.IterationIndex == first.IterationIndex).ToArray();
        var runs = group.Where(sample => sample.StepRunId is not null).Select(sample => sample.StepRunId).ToHashSet();
        return recording.Events.Where(mark => mark.StepRunId is not null && runs.Contains(mark.StepRunId)).ToArray();
    }

    private static string Describe(StoredSample sample, bool example)
        => example ? $"Example · publisher channel {sample.Channel}" : $"Recording · publisher channel {sample.Channel} · producer {sample.ProducerStepId?.ToString() ?? "unknown"} · execution {sample.StepRunId?.ToString() ?? "unknown"} · loop execution {sample.LoopRunId?.ToString() ?? "none/unknown"} · iteration {sample.IterationIndex?.ToString() ?? "none/unknown"}";

    private static IReadOnlyList<string> Inputs(MetricDraft metric) => PlanCompiler.AuthoringInputChannels(metric.Source);

    private static bool? PublisherVerdict(MetricDraft metric, IReadOnlyList<StoredSample> samples)
    {
        var step = PlanCompiler.PreviewStep(metric);
        if (step is PublishBandScalarStep scalar)
        {
            AuthoringCriteria.Validate(metric);
            return !scalar.FailWhenOutOfBand || samples.All(sample => !SeriesComplianceModes.IsOutOfBand(sample.Value, scalar.LimitLow, scalar.LimitHigh));
        }
        var mode = step switch { AcquireVoltageStep acquire => acquire.SeriesCompliance, BitSweepAcquireStep sweep => sweep.SeriesCompliance, _ => null };
        if (!SeriesComplianceModes.IsEnabled(mode)) return null;
        AuthoringCriteria.Validate(metric);
        var (fail, low, high, dwell, interval) = step switch
        {
            AcquireVoltageStep acquire => (acquire.FailWhenOutOfBand, acquire.LimitLow, acquire.LimitHigh, acquire.DwellLimitMs, acquire.IntervalMs),
            BitSweepAcquireStep sweep => (sweep.FailWhenOutOfBand, sweep.LimitLow, sweep.LimitHigh, sweep.DwellLimitMs, sweep.IntervalMs),
            _ => (false, (double?)null, (double?)null, (double?)null, 0),
        };
        var dwellMs = 0d;
        var passed = true;
        foreach (var sample in samples)
            if (SeriesComplianceModes.ShouldFailSample(mode, fail, sample.Value, low, high, dwell, interval, ref dwellMs)) passed = false;
        return passed;
    }

    private static IReadOnlyList<StoredSample> Example(MetricDraft metric)
    {
        var step = PlanCompiler.PreviewStep(metric);
        if (!step.Enabled) throw new AuthoringWorkspaceException("Step is disabled in the source plan.");
        var nominal = metric.Limits is { Low: { } low, High: { } high } ? low / 2 + high / 2 : metric.Limits?.Threshold ?? 1;
        var (values, elapsed, unit) = step switch
        {
            PublishTimedSampleStep timed => (new[] { timed.Value }, new double?[] { timed.ElapsedMs }, metric.YUnit),
            PublishBandScalarStep scalar => (new[] { scalar.Value }, new double?[] { null }, string.IsNullOrWhiteSpace(metric.YUnit) ? scalar.Unit : metric.YUnit),
            AcquireVoltageStep acquire => (Enumerable.Repeat(nominal, Math.Max(0, acquire.SampleCount)).ToArray(), Enumerable.Range(0, Math.Max(0, acquire.SampleCount)).Select(index => (double?)(index * acquire.IntervalMs)).ToArray(), metric.YUnit),
            BitSweepAcquireStep sweep => (Enumerable.Repeat(nominal, Math.Max(0, sweep.BitCount)).ToArray(), Enumerable.Range(0, Math.Max(0, sweep.BitCount)).Select(index => (double?)(index * sweep.IntervalMs)).ToArray(), metric.YUnit),
            _ => (new[] { nominal }, new double?[] { null }, metric.YUnit),
        };
        if (values.Any(value => !double.IsFinite(value))) throw new AuthoringWorkspaceException("Example publisher requires finite values.");
        if (values.Length == 0) throw new AuthoringWorkspaceException("Example acquisition has no samples.");
        var publishedChannel = step is PublishBandScalarStep scalarPublisher ? scalarPublisher.MetricName : metric.ChannelKey;
        return values.Select((value, index) => new StoredSample { Channel = publishedChannel, MetricKey = metric.ChannelKey, Value = value, ElapsedMs = elapsed[index], Unit = unit }).ToArray();
    }
}
