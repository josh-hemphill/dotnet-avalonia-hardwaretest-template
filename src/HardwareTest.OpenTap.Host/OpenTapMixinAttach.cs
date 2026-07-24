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

        if (member.GetValue(step) is null)
        {
            member.SetValue(step, new AnnotationMixin());
        }

        return member;
    }

    /// Attach Presentation mixin and set demo ChannelKey / DisplayRole / YUnit.
    public static MixinMemberData AttachPresentation(
        ITestStep step,
        string channelKey,
        string displayRole,
        string yUnit)
    {
        var builder = new PresentationMixinBuilder();
        var type = TypeData.GetTypeData(step);
        builder.Initialize(type);
        var member = builder.ToDynamicMember(type);
        DynamicMember.AddDynamicMember(step, member);

        var embed = member.GetValue(step) as PresentationMixin ?? new PresentationMixin();
        embed.ChannelKey = channelKey;
        embed.DisplayRole = displayRole;
        embed.YUnit = yUnit;
        member.SetValue(step, embed);
        return member;
    }
}
