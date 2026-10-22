using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace HardwareTest.Authoring;

/// One-time authoring-service migration. Pure DTO conversion and compiler decompilation
/// retain their existing representation; only opening an editor establishes this layout.
public static class AuthoringMeasureSections
{
    public const string DefaultSectionName = "Measure";
    internal static readonly XName CompiledLayoutAttribute = XName.Get("measureSections", "urn:hardwaretest:authoring");

    internal static bool HasCompiledLayout(string path)
        => (string?)XDocument.Load(path).Root?.Attribute(CompiledLayoutAttribute) == "true";

    internal static void WriteCompiledLayout(Stream serializedPlan, Stream destination)
    {
        serializedPlan.Position = 0;
        var xml = XDocument.Load(serializedPlan, LoadOptions.PreserveWhitespace);
        xml.Root!.SetAttributeValue(CompiledLayoutAttribute, "true");
        xml.Save(destination, SaveOptions.DisableFormatting);
    }

    public static ProgramDraft Initialize(ProgramDraft draft)
    {
        if (draft.Measure.Any(node => node is SectionNode)) return draft;
        var used = AuthoringSequence.Flatten(draft).Where(row => row.NodeId is not null)
            .Select(row => row.NodeId!.Value).ToHashSet();
        Guid identity;
        var salt = 0;
        do
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"HardwareTest.MeasureSection/{draft.PlanId}/{salt++}"));
            identity = new Guid(hash.AsSpan(0, 16));
        } while (identity == Guid.Empty || used.Contains(identity));
        return draft with { Measure = [new SectionNode(DefaultSectionName, draft.Measure.ToArray()) { NodeId = identity }] };
    }

    public static AuthoringDocumentDto Normalize(AuthoringDocumentDto source)
    {
        if (source.HasConfigurableMeasureSections) return source;
        var original = source.ToDraft();
        var normalized = Initialize(original);
        return source with
        {
            HasConfigurableMeasureSections = true,
            Measure = normalized.Measure.Select(AuthoringMeasureDto.From).ToArray(),
            RequiresCompilation = source.RequiresCompilation || !ReferenceEquals(normalized, original),
        };
    }

    public static AuthoringDocumentLoadResult Normalize(AuthoringDocumentLoadResult source)
        => source is { IsReadOnly: false, Document: { } document }
            ? source with { Document = Normalize(document) } : source;
}
