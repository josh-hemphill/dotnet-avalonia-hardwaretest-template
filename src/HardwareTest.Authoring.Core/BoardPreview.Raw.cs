using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public static partial class BoardPreviewBuilder
{
    private static void AddRaw(RawStepNode raw, TestRunRecord? recording,
        Dictionary<string, IReadOnlyList<IReadOnlyList<StoredSample>>> available, List<BoardPreviewTile> tiles)
    {
        string? publisherKey = null;
        try
        {
            if (recording is { IsSchemaReadOnly: true }) throw new AuthoringWorkspaceException($"Unsupported recording schema {recording.StoredSchemaVersion}.");
            var step = PlanCompiler.PreviewRawStep(raw);
            if (!step.Enabled) return;
            var hints = OpenTapPresentation.TryReadMixin(step);
            var channel = step switch { PublishTimedSampleStep timed => timed.Channel, PublishBandScalarStep scalar => scalar.MetricName, _ => null };
            if (channel is null) throw new AuthoringWorkspaceException("This imported step does not expose a supported preview publisher.");
            var key = string.IsNullOrWhiteSpace(hints?.ChannelKey) ? channel : hints.ChannelKey;
            publisherKey = key;
            IReadOnlyList<IReadOnlyList<StoredSample>> groups;
            if (recording is not null)
            {
                groups = RunDatasetBinder.SeriesForProducer(recording, key, raw.NodeId);
                if (groups.Count == 0) throw new AuthoringWorkspaceException($"Missing recording channel '{key}' for this imported publisher.");
            }
            else
            {
                var (value, elapsed, unit) = step switch
                {
                    PublishTimedSampleStep timed => (timed.Value, timed.ElapsedMs, hints?.YUnit ?? string.Empty),
                    PublishBandScalarStep scalar => (scalar.Value, (double?)null, hints?.YUnit ?? scalar.Unit),
                    _ => (0d, (double?)null, string.Empty),
                };
                if (!double.IsFinite(value)) throw new AuthoringWorkspaceException("Imported publisher requires a finite value.");
                groups = [new[] { new StoredSample { Channel = channel, MetricKey = key, Value = value, ElapsedMs = elapsed, Unit = unit } }];
            }
            var role = hints?.DisplayRole ?? (step is PublishBandScalarStep ? PresentationRoles.Scalar : PresentationRoles.Timeseries);
            foreach (var group in groups)
            {
                var preview = new MetricPreview(key, role, PresentationRoles.TryMapRole(role), group[0].Unit ?? string.Empty,
                    group[^1].Value, group.Select(sample => sample.Value).ToArray(), group.Select(sample => sample.ElapsedMs).ToArray(),
                    group[0].LimitLow, group[0].LimitHigh, null, recording is null ? null : "Recording samples; no hardware execution.");
                tiles.Add(new(raw.NodeId, step.Name, Describe(group[0], recording is null), preview, AuthoringPreviewChromeBuilder.From(preview, EventsForGroup(recording, group))));
            }
            BindPublisherGroups(key, step is PublishTimedSampleStep, groups, recording, available);
        }
        catch (Exception error) when (error is AuthoringWorkspaceException or FormatException or OverflowException or global::OpenTap.TestPlan.PlanLoadException or System.Xml.XmlException)
        {
            if (publisherKey is not null) available[publisherKey] = [];
            var preview = MetricPreviewBuilder.Empty with { ChannelKey = publisherKey ?? string.Empty, Note = error.Message };
            tiles.Add(new(raw.NodeId, raw.TypeName, recording is null ? "Example" : "Recording", preview, AuthoringPreviewChromeBuilder.From(preview)));
        }
    }
}
