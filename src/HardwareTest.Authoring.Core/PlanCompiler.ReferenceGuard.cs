using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public sealed partial class PlanCompiler
{
    private static Guid ReferenceGuardId(string planId)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes("HardwareTest/operator-reference-acceptance/" + planId)).AsSpan(0, 16));

    private static bool HasCompiledReferenceGuard(string path)
    {
        if (!File.Exists(path)) return false;
        var type = typeof(RequireOperatorReferenceAcceptanceStep).FullName!;
        var xml = File.ReadAllText(path);
        // Ordinary legacy sidecar-only persistence does not need a parseable
        // plan. Inspect structured XML only when the capability type is present.
        if (!xml.Contains(type, StringComparison.Ordinal)) return false;
        return XDocument.Parse(xml).Descendants().Any(element => element.Name.LocalName == "TestStep"
            && element.Attribute("type") is { } serialized
            && ResourceTypeId(serialized.Value.Split(',')[0].Trim()) == type);
    }
}
