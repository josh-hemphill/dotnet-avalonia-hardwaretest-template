using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Mixins;

/// Builds the Presentation mixin for OpenTAP Editor / AttachPresentation.
[Display("Presentation", Groups: ["HardwareTest"], Description: "Adds ChannelKey, DisplayRole, and YUnit for shell charts/gauges.")]
[MixinBuilder(typeof(ITestStep))]
public sealed class PresentationMixinBuilder : IMixinBuilder
{
    public string MemberName { get; set; } = "Presentation";

    public void Initialize(ITypeData type)
    {
        if (string.IsNullOrWhiteSpace(MemberName))
        {
            MemberName = "Presentation";
        }
    }

    public MixinMemberData ToDynamicMember(ITypeData targetType)
        => new(this, () => new PresentationMixin())
        {
            Name = "HardwareTest.Presentation:" + MemberName,
            TypeDescriptor = TypeData.FromType(typeof(PresentationMixin)),
            Attributes =
            [
                new DisplayAttribute(MemberName, "Presentation hints for shell charts and gauges.", Order: 110),
                new EmbedPropertiesAttribute(),
            ],
            DeclaringType = TypeData.FromType(typeof(ITestStep)),
        };
}
