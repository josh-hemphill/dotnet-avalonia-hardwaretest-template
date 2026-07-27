using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Reads Presentation mixin hints (flattened EmbedProperties) and normalizes Sample/Scalar rows.
internal static class OpenTapPresentation
{
    public sealed record MixinHints(
        string ChannelKey,
        string DisplayRole,
        string YUnit,
        bool HistoryEnabled = true,
        double? HistoryWatchPercent = null,
        double? HistoryAlertPercent = null);

    public static MixinHints? TryReadMixin(ITestStep? step)
    {
        if (step is null)
        {
            return null;
        }

        try
        {
            var typeData = TypeData.GetTypeData(step);
            string? channelKey = null;
            string? displayRole = null;
            string? yUnit = null;
            bool? historyEnabled = null;
            double? historyWatch = null;
            double? historyAlert = null;
            var found = false;

            foreach (var member in typeData.GetMembers())
            {
                if (!member.Readable)
                {
                    continue;
                }

                object? raw;
                try
                {
                    raw = member.GetValue(step);
                }
                catch
                {
                    continue;
                }

                // Container embed (when not flattened yet).
                if (raw is PresentationMixin embed)
                {
                    return FromEmbed(embed);
                }

                // Flattened EmbedProperties children on the step (OpenTAP parameter bridge path).
                if (member.Name.EndsWith("ChannelKey", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(member.Name, "ChannelKey", StringComparison.OrdinalIgnoreCase))
                {
                    channelKey = Convert.ToString(raw)?.Trim();
                    found = true;
                }
                else if (member.Name.EndsWith("DisplayRole", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(member.Name, "DisplayRole", StringComparison.OrdinalIgnoreCase))
                {
                    displayRole = Convert.ToString(raw)?.Trim();
                    found = true;
                }
                else if (member.Name.EndsWith("YUnit", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(member.Name, "YUnit", StringComparison.OrdinalIgnoreCase))
                {
                    yUnit = Convert.ToString(raw)?.Trim();
                    found = true;
                }
                else if (member.Name.EndsWith("HistoryEnabled", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(member.Name, "HistoryEnabled", StringComparison.OrdinalIgnoreCase))
                {
                    historyEnabled = Convert.ToBoolean(raw);
                    found = true;
                }
                else if (member.Name.EndsWith("HistoryWatchPercent", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(member.Name, "HistoryWatchPercent", StringComparison.OrdinalIgnoreCase))
                {
                    historyWatch = TryToNullableDouble(raw);
                    found = true;
                }
                else if (member.Name.EndsWith("HistoryAlertPercent", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(member.Name, "HistoryAlertPercent", StringComparison.OrdinalIgnoreCase))
                {
                    historyAlert = TryToNullableDouble(raw);
                    found = true;
                }
            }

            if (!found)
            {
                return null;
            }

            return new MixinHints(
                channelKey ?? string.Empty,
                string.IsNullOrWhiteSpace(displayRole)
                    ? PresentationDisplayRoles.Timeseries
                    : displayRole,
                yUnit ?? string.Empty,
                historyEnabled ?? true,
                historyWatch,
                historyAlert);
        }
        catch
        {
            return null;
        }
    }

    private static MixinHints FromEmbed(PresentationMixin embed) => new(
        embed.ChannelKey?.Trim() ?? string.Empty,
        string.IsNullOrWhiteSpace(embed.DisplayRole)
            ? PresentationDisplayRoles.Timeseries
            : embed.DisplayRole.Trim(),
        embed.YUnit?.Trim() ?? string.Empty,
        embed.HistoryEnabled,
        embed.HistoryWatchPercent,
        embed.HistoryAlertPercent);

    private static double? TryToNullableDouble(object? raw)
    {
        if (raw is null || raw is DBNull)
        {
            return null;
        }

        if (raw is double d)
        {
            return double.IsNaN(d) ? null : d;
        }

        try
        {
            return Convert.ToDouble(raw);
        }
        catch
        {
            return null;
        }
    }

    public static void ApplySample(
        StoredSample sample,
        string publishedChannel,
        MixinHints? hints)
    {
        sample.Channel = publishedChannel;
        if (hints is not null && !string.IsNullOrWhiteSpace(hints.ChannelKey))
        {
            sample.MetricKey = hints.ChannelKey;
        }
        else
        {
            sample.MetricKey = publishedChannel;
        }

        sample.DisplayRole = hints?.DisplayRole ?? PresentationDisplayRoles.Timeseries;
        if (hints is not null && !string.IsNullOrWhiteSpace(hints.YUnit))
        {
            sample.Unit = hints.YUnit;
        }

        ApplyHistoryHints(sample, hints);
    }

    public static void ApplyScalar(
        StoredSample sample,
        string publishedName,
        string publishedUnit,
        MixinHints? hints,
        double? limitLow = null,
        double? limitHigh = null)
    {
        sample.Channel = publishedName;
        if (hints is not null && !string.IsNullOrWhiteSpace(hints.ChannelKey))
        {
            sample.MetricKey = hints.ChannelKey;
        }
        else
        {
            sample.MetricKey = publishedName;
        }

        sample.DisplayRole = hints is not null && !string.IsNullOrWhiteSpace(hints.DisplayRole)
            ? hints.DisplayRole
            : PresentationDisplayRoles.Scalar;

        if (!string.IsNullOrWhiteSpace(publishedUnit))
        {
            sample.Unit = publishedUnit;
        }
        else if (hints is not null && !string.IsNullOrWhiteSpace(hints.YUnit))
        {
            sample.Unit = hints.YUnit;
        }

        sample.LimitLow = limitLow;
        sample.LimitHigh = limitHigh;
        ApplyHistoryHints(sample, hints);
    }

    private static void ApplyHistoryHints(StoredSample sample, MixinHints? hints)
    {
        if (hints is null)
        {
            sample.HistoryEnabled = true;
            sample.HistoryWatchPercent = null;
            sample.HistoryAlertPercent = null;
            return;
        }

        sample.HistoryEnabled = hints.HistoryEnabled;
        sample.HistoryWatchPercent = hints.HistoryWatchPercent;
        sample.HistoryAlertPercent = hints.HistoryAlertPercent;
    }
}
