using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace HardwareTest.OpenTap.Host;

/// Identifies the exact compiled pair published for a local bench handoff.
public sealed record BenchProgramReceipt
{
    public int SchemaVersion { get; init; } = 1;
    public string PlanId { get; init; } = string.Empty;
    public string ExportId { get; init; } = string.Empty;
    public DateTimeOffset ExportedAt { get; init; }
    public long? SourceRevision { get; init; }
    public string PlanSha256 { get; init; } = string.Empty;
    public string SidecarSha256 { get; init; } = string.Empty;
    public string RequiredOperatorCapability { get; init; } = string.Empty;
}

public static class BenchProgramHandoff
{
    public const string ReceiptFileName = "bench-program.json";

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static void RequireCustomPlanId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id != Path.GetFileName(id) || id is "." or ".."
            || id.StartsWith('.') || id.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new InvalidOperationException("Choose a valid unique program ID before preparing bench files.");
        if (id.Equals("sample", StringComparison.OrdinalIgnoreCase)
            || id.Equals("board-demo", StringComparison.OrdinalIgnoreCase)
            || id.Equals("sweep-demo", StringComparison.OrdinalIgnoreCase)
            || id.Equals("timing-demo", StringComparison.OrdinalIgnoreCase)
            || id.Equals("envelope-sweep-demo", StringComparison.OrdinalIgnoreCase)
            || id.Equals("station-health", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Program ID '{id}' belongs to a built-in factory. Create a test plan with a unique ID to prepare a custom bench program.");
    }

    public static IEnumerable<string> EnumeratePlanFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.TapPlan")) yield return file;
        foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(directory).StartsWith('.')) continue;
            if (!File.Exists(Path.Combine(directory, ReceiptFileName))) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.TapPlan")) yield return file;
        }
    }

    /// Legacy loose plans remain supported; a present receipt must match both files.
    public static BenchProgramReceipt? ValidateProgram(string planPath)
    {
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(planPath))!, ReceiptFileName);
        if (!File.Exists(path)) return null;
        var receipt = JsonSerializer.Deserialize(File.ReadAllText(path), BenchHandoffJsonContext.Default.BenchProgramReceipt)
            ?? throw new InvalidOperationException("The bench handoff receipt is empty. Prepare and copy the complete program again.");
        if (receipt.SchemaVersion != 1)
            throw new InvalidOperationException("This bench handoff receipt uses an unsupported version. Prepare the program with a supported authoring app.");
        if (receipt.RequiredOperatorCapability is not ("" or "bench-references-v1"))
            throw new InvalidOperationException("This program requires operator features that this app does not support. Use the matching operator app and authoring build.");
        RequireCustomPlanId(receipt.PlanId);
        if (receipt.PlanId != Path.GetFileNameWithoutExtension(planPath)
            || string.IsNullOrWhiteSpace(receipt.ExportId))
            throw new InvalidOperationException("The bench handoff receipt belongs to another program. Copy the complete prepared program folder again.");
        var sidecar = Path.ChangeExtension(planPath, ".program.json");
        if (!File.Exists(sidecar) || Hash(File.ReadAllBytes(planPath)) != receipt.PlanSha256
            || Hash(File.ReadAllBytes(sidecar)) != receipt.SidecarSha256)
            throw new InvalidOperationException("The compiled plan and operator settings do not match their bench receipt. Prepare and copy the complete program folder again.");
        if (receipt.RequiredOperatorCapability == "bench-references-v1")
        {
            var guard = typeof(HardwareTest.OpenTap.Plugins.Basic.RequireOperatorReferenceAcceptanceStep).FullName;
            var hasGuard = XDocument.Load(planPath).Descendants().Attributes()
                .Any(attribute => attribute.Name.LocalName == "type"
                    && attribute.Value.Split(',')[0].Trim().Replace("emb:", string.Empty, StringComparison.Ordinal) == guard);
            if (!hasGuard)
                throw new InvalidOperationException("The reference-dependent program has no runtime compatibility guard. Recompile it with the matching authoring app and prepare the complete bench copy again.");
        }
        return receipt;
    }

    public static string Describe(BenchProgramReceipt receipt)
        => $"Program: {receipt.PlanId}\nExport: {receipt.ExportId}\nPrepared: {receipt.ExportedAt:O}\n"
            + $"Source revision: {receipt.SourceRevision?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Imported compiled files"}\n"
            + $"Required operator features: {(receipt.RequiredOperatorCapability == "bench-references-v1" ? "Golden-reference acceptance" : "Standard program execution")}\n"
            + $"Plan SHA-256: {receipt.PlanSha256}\nSettings SHA-256: {receipt.SidecarSha256}";
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BenchProgramReceipt))]
public partial class BenchHandoffJsonContext : JsonSerializerContext;
