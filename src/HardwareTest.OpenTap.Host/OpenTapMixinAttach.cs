using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;

namespace HardwareTest.OpenTap.Host;

/// Attaches demo mixins without relying on internal MixinFactory.
internal static class OpenTapMixinAttach
{
    /// Attach Annotation mixin to a step (same outcome as Editor Add Mixin).
    public static MixinMemberData AttachAnnotation(ITestStep step)
    {
        var builder = new AnnotationMixinBuilder();
        var type = TypeData.GetTypeData(step);
        builder.Initialize(type);
        var member = builder.ToDynamicMember(type);
        DynamicMember.AddDynamicMember(step, member);

        // AddDynamicMember registers the member; the embed owner instance must still be created.
        if (member.GetValue(step) is null)
        {
            member.SetValue(step, new AnnotationMixin());
        }

        return member;
    }
}
