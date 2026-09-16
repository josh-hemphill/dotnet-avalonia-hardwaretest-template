using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Mixins;
using OpenTap;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class MixinMemberNameTests
{
    [Fact]
    public void Mixin_member_names_are_xml_safe()
    {
        var type = TypeData.FromType(typeof(ITestStep));
        var presentation = new PresentationMixinBuilder();
        presentation.Initialize(type);
        Assert.Equal("HardwareTest.Presentation", presentation.ToDynamicMember(type).Name);

        var annotation = new AnnotationMixinBuilder();
        annotation.Initialize(type);
        Assert.Equal("HardwareTest.Annotation", annotation.ToDynamicMember(type).Name);
    }

    [Fact]
    public void Committed_TapPlans_do_not_hex_encode_colons_in_mixin_elements()
    {
        var programs = Path.Combine(AppContext.BaseDirectory, "Programs");
        Assert.True(Directory.Exists(programs), programs);
        var plans = Directory.EnumerateFiles(programs, "*.TapPlan", SearchOption.AllDirectories).ToList();
        Assert.Contains(plans, p => p.EndsWith("sample.TapPlan", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plans, p => p.EndsWith("board-demo.TapPlan", StringComparison.OrdinalIgnoreCase));
        foreach (var plan in plans)
        {
            var xml = File.ReadAllText(plan);
            Assert.DoesNotContain("_x003A_", xml);
            Assert.DoesNotContain("HardwareTest.Presentation:", xml);
            Assert.DoesNotContain("HardwareTest.Annotation:", xml);
        }
    }

    [Fact]
    public void Sample_TapPlan_deserializes_presentation_and_annotation_mixins()
    {
        OpenTapPluginSearch.SearchSerialized();
        var path = Path.Combine(AppContext.BaseDirectory, "Programs", "sample.TapPlan");
        Assert.True(File.Exists(path), path);
        var plan = TestPlan.Load(path);
        var acquire = FindStep(plan, "Acquire VDC");
        var identity = FindStep(plan, "Identity Check");
        Assert.NotNull(acquire);
        Assert.NotNull(identity);

        var presentation = OpenTapPresentation.TryReadMixin(acquire);
        Assert.NotNull(presentation);
        Assert.Equal("VDC", presentation.ChannelKey);
        Assert.Equal(PresentationDisplayRoles.Timeseries, presentation.DisplayRole);

        var type = TypeData.GetTypeData(identity);
        var names = type.GetMembers().Select(m => m.Name).ToArray();
        Assert.Contains("HardwareTest.Annotation.Note", names);
        Assert.Contains("HardwareTest.Annotation.IncludeInReport", names);
    }

    [Fact]
    public void Saving_an_attached_mixin_writes_xml_safe_element_names()
    {
        OpenTapPluginSearch.SearchSerialized();
        var step = new HardwareTest.OpenTap.Plugins.Basic.AcquireVoltageStep { Name = "Acquire" };
        OpenTapMixinAttach.AttachPresentation(step, "VDC", PresentationDisplayRoles.Timeseries, "V");
        var plan = new TestPlan();
        plan.ChildTestSteps.Add(step);
        var path = Path.Combine(Path.GetTempPath(), "ht-mixin-" + Guid.NewGuid().ToString("N") + ".TapPlan");
        try
        {
            plan.Save(path);
            var xml = File.ReadAllText(path);
            Assert.DoesNotContain("_x003A_", xml);
            Assert.Contains("<HardwareTest.Presentation", xml, StringComparison.Ordinal);
            Assert.Contains("HardwareTest.Presentation.ChannelKey", xml, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static ITestStep? FindStep(TestPlan plan, string name)
        => plan.ChildTestSteps.SelectMany(Walk).FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.Ordinal));

    private static IEnumerable<ITestStep> Walk(ITestStep step)
    {
        yield return step;
        foreach (var child in step.ChildTestSteps.SelectMany(Walk))
        {
            yield return child;
        }
    }
}
