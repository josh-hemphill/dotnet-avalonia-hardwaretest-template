using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Reads Presentation mixin hints (flattened EmbedProperties) and normalizes Sample/Scalar rows.
internal static class OpenTapPresentation
{
    public sealed record MixinHints(string ChannelKey, string DisplayRole, string YUnit);

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
                    return new MixinHints(
                        embed.ChannelKey?.Trim() ?? string.Empty,
                        string.IsNullOrWhiteSpace(embed.DisplayRole)
                            ? PresentationDisplayRoles.Timeseries
                            : embed.DisplayRole.Trim(),
                        embed.YUnit?.Trim() ?? string.Empty);
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
                yUnit ?? string.Empty);
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
    }

    public static void ApplyScalar(
        StoredSample sample,
        string publishedName,
        string publishedUnit,
        MixinHints? hints)
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
    }
}
