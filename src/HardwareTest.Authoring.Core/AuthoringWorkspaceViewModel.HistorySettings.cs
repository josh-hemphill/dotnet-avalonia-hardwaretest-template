namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public string HistoryWatchPercent
    {
        get => NumericText(nameof(HistoryWatchPercent), SelectedMetric?.History?.WatchPercent);
        set => SetNumericText(nameof(HistoryWatchPercent), value,
            parsed => UpdateOptionalHistory(parsed, current => current with { WatchPercent = parsed }));
    }

    public string HistoryAlertPercent
    {
        get => NumericText(nameof(HistoryAlertPercent), SelectedMetric?.History?.AlertPercent);
        set => SetNumericText(nameof(HistoryAlertPercent), value,
            parsed => UpdateOptionalHistory(parsed, current => current with { AlertPercent = parsed }));
    }

    private void UpdateOptionalHistory(double? parsed, Func<HistorySpec, HistorySpec> mutate)
    {
        if (parsed is null && SelectedMetric?.History is null)
            UpdateSelectedMetric(metric => metric); // Clears incomplete authoring text without creating runtime history.
        else UpdateHistory(mutate);
    }

    /// Mixin default when History is omitted; PresentationAttach leaves HistoryEnabled=true.
    private const bool DefaultHistoryEnabled = true;

    private void UpdateHistory(Func<HistorySpec, HistorySpec> mutate)
        => UpdateSelectedMetric(metric =>
        {
            var current = metric.History ?? new HistorySpec(DefaultHistoryEnabled, null, null);
            return metric with { History = mutate(current) };
        });

    private IReadOnlyList<AuthoringSettingRow> ToSettingRows(
        IReadOnlyDictionary<string, string> settings,
        string functionId)
        => settings
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => AuthoringMetricSettingCatalog.CreateRow(functionId, pair.Key, pair.Value, ChannelKeys))
            .ToArray();

    private static IReadOnlyDictionary<string, string> WithSetting(
        IReadOnlyDictionary<string, string> settings,
        string key,
        string value)
    {
        var next = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase)
        {
            [key] = value ?? string.Empty,
        };
        return next;
    }

}
