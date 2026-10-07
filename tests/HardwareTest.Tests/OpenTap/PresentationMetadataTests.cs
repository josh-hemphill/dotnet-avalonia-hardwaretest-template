using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class PresentationMetadataTests
{
    [Fact]
    public void No_presentation_does_not_read_unrelated_getters()
    {
        var step = new AcquireVoltageStep();
        var unrelated = AddUnrelatedGetter(step);

        Assert.Null(OpenTapPresentation.TryReadMixin(step));
        Assert.Equal(0, unrelated.Reads);
        Assert.Null(OpenTapPresentation.TryReadMixin(null));
    }

    [Fact]
    public void Exact_typed_container_with_custom_name_preserves_every_hint()
    {
        var step = new AcquireVoltageStep();
        var unrelated = AddUnrelatedGetter(step);
        Add(step, "Vendor.CustomContainer", typeof(PresentationMixin), () => new PresentationMixin
        {
            ChannelKey = " rail.mean ",
            DisplayRole = PresentationDisplayRoles.Passband,
            YUnit = " V ",
            HistoryEnabled = false,
            HistoryWatchPercent = 2.5,
            HistoryAlertPercent = 7.5,
        });

        AssertHints(OpenTapPresentation.TryReadMixin(step));
        Assert.Equal(0, unrelated.Reads);
    }

    [Fact]
    public void Attached_current_builder_preserves_every_hint_without_unrelated_reads()
    {
        OpenTapPluginSearch.SearchSerialized();
        var step = new AcquireVoltageStep();
        var unrelated = AddUnrelatedGetter(step);
        var member = OpenTapMixinAttach.AttachPresentation(step, "rail.mean", PresentationDisplayRoles.Passband, "V");
        Assert.Equal(typeof(PresentationMixin).FullName, member.TypeDescriptor.Name);
        var embed = Assert.IsType<PresentationMixin>(member.GetValue(step));
        embed.HistoryEnabled = false;
        embed.HistoryWatchPercent = 2.5;
        embed.HistoryAlertPercent = 7.5;

        AssertHints(OpenTapPresentation.TryReadMixin(step));
        Assert.Equal(0, unrelated.Reads);
    }

    [Fact]
    public void Flattened_custom_prefix_and_case_preserve_hint_conversions()
    {
        var step = new AcquireVoltageStep();
        var unrelated = AddUnrelatedGetter(step);
        Add(step, "Vendor.channelkey", typeof(string), () => " rail.mean ");
        Add(step, "Vendor.displayrole", typeof(string), () => PresentationDisplayRoles.Passband);
        Add(step, "Vendor.yunit", typeof(string), () => " V ");
        Add(step, "Vendor.historyenabled", typeof(string), () => "False");
        Add(step, "Vendor.historywatchpercent", typeof(decimal), () => 2.5m);
        Add(step, "Vendor.historyalertpercent", typeof(int), () => 7);

        var hints = OpenTapPresentation.TryReadMixin(step);
        Assert.NotNull(hints);
        Assert.Equal("rail.mean", hints.ChannelKey);
        Assert.Equal(PresentationDisplayRoles.Passband, hints.DisplayRole);
        Assert.Equal("V", hints.YUnit);
        Assert.False(hints.HistoryEnabled);
        Assert.Equal(2.5, hints.HistoryWatchPercent);
        Assert.Equal(7, hints.HistoryAlertPercent);
        Assert.Equal(0, unrelated.Reads);
    }

    [Fact]
    public void Flattened_missing_and_invalid_optional_hints_keep_defaults()
    {
        var step = new AcquireVoltageStep();
        var unrelated = AddUnrelatedGetter(step);
        Add(step, "ChannelKey", typeof(string), () => " rail.mean ");
        Add(step, "DisplayRole", typeof(string), () => " ");
        Add(step, "HistoryWatchPercent", typeof(double), () => double.NaN);
        Add(step, "HistoryAlertPercent", typeof(string), () => "invalid");
        var throwingUnit = Add(step, "YUnit", typeof(string), () => throw new InvalidOperationException("unavailable hint"));

        var hints = OpenTapPresentation.TryReadMixin(step);
        Assert.NotNull(hints);
        Assert.Equal("rail.mean", hints.ChannelKey);
        Assert.Equal(PresentationDisplayRoles.Timeseries, hints.DisplayRole);
        Assert.Equal(string.Empty, hints.YUnit);
        Assert.True(hints.HistoryEnabled);
        Assert.Null(hints.HistoryWatchPercent);
        Assert.Null(hints.HistoryAlertPercent);
        Assert.Equal(1, throwingUnit.Reads);
        Assert.Equal(0, unrelated.Reads);
    }

    private static void AssertHints(OpenTapPresentation.MixinHints? hints)
    {
        Assert.NotNull(hints);
        Assert.Equal("rail.mean", hints.ChannelKey);
        Assert.Equal(PresentationDisplayRoles.Passband, hints.DisplayRole);
        Assert.Equal("V", hints.YUnit);
        Assert.False(hints.HistoryEnabled);
        Assert.Equal(2.5, hints.HistoryWatchPercent);
        Assert.Equal(7.5, hints.HistoryAlertPercent);
    }

    private static ProbeMember AddUnrelatedGetter(ITestStep step)
        => Add(step, "Vendor.UnrelatedValidation", typeof(string),
            () => throw new InvalidOperationException("Presentation lookup must not invoke general validation."));

    private static ProbeMember Add(ITestStep step, string name, Type valueType, Func<object?> getter)
    {
        var member = new ProbeMember(name, TypeData.GetTypeData(step), TypeData.FromType(valueType), getter);
        DynamicMember.AddDynamicMember(step, member);
        return member;
    }

    private sealed class ProbeMember(string name, ITypeData declaringType, ITypeData valueType, Func<object?> getter) : IMemberData
    {
        public string Name => name;
        public IEnumerable<object> Attributes => [];
        public ITypeData DeclaringType => declaringType;
        public ITypeData TypeDescriptor => valueType;
        public bool Readable => true;
        public bool Writable => false;
        public int Reads { get; private set; }

        public object? GetValue(object owner)
        {
            Reads++;
            return getter();
        }

        public void SetValue(object owner, object value) => throw new NotSupportedException();
    }
}
