using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Mixins;

/// Builds the Annotation mixin for OpenTAP Editor / MixinFactory.LoadMixin.
[Display("Annotation", Groups: ["HardwareTest"], Description: "Adds Note and Include-in-report settings to a test step.")]
[MixinBuilder(typeof(ITestStep))]
public sealed class AnnotationMixinBuilder : IMixinBuilder
{
    public string MemberName { get; set; } = "Annotation";

    public void Initialize(ITypeData type)
    {
        if (string.IsNullOrWhiteSpace(MemberName))
        {
            MemberName = "Annotation";
        }
    }

    public MixinMemberData ToDynamicMember(ITypeData targetType)
        => new(this, () => new AnnotationMixin())
        {
            Name = "HardwareTest.Annotation:" + MemberName,
            TypeDescriptor = TypeData.FromType(typeof(AnnotationMixin)),
            Attributes =
            [
                new DisplayAttribute(MemberName, "Station annotation for this step.", Order: 100),
                new EmbedPropertiesAttribute(),
            ],
            DeclaringType = TypeData.FromType(typeof(ITestStep)),
        };
}
