using System.Globalization;
using System.Reflection;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.Authoring;

/// Writes Presentation mixin fields and step limit properties for a metric leaf.
public static class PresentationAttach
{
    /// Limits go on the step; history goes on the mixin. Do not call the 3-arg demo helper alone.
    public static void Apply(ITestStep step, MetricDraft metric)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(metric);

        var member = OpenTapMixinAttach.AttachPresentation(
            step,
            metric.ChannelKey,
            metric.DisplayRole,
            metric.YUnit);

        if (metric.History is { } history && member.GetValue(step) is PresentationMixin embed)
        {
            embed.HistoryEnabled = history.Enabled;
            embed.HistoryWatchPercent = history.WatchPercent;
            embed.HistoryAlertPercent = history.AlertPercent;
            member.SetValue(step, embed);
        }

        ApplyLimits(step, metric.Limits);
    }

    internal static void ApplyLimits(ITestStep step, LimitSpec? limits)
    {
        if (limits is null)
        {
            return;
        }

        SetProperty(step, "LimitLow", limits.Low);
        SetProperty(step, "LimitHigh", limits.High);
        SetProperty(step, "Threshold", limits.Threshold);
        SetProperty(step, "OffsetLimitLow", limits.Low);
        SetProperty(step, "OffsetLimitHigh", limits.High);
    }

    private static void SetProperty(ITestStep step, string name, double? value)
    {
        if (value is null)
        {
            return;
        }

        var prop = step.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (prop is null || !prop.CanWrite)
        {
            return;
        }

        var target = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        if (target == typeof(double))
        {
            prop.SetValue(step, value.Value);
            return;
        }

        if (target == typeof(string))
        {
            prop.SetValue(step, value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
