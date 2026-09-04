using System.Text.Json;
using HardwareTest.Core.Runs;

namespace HardwareTest.OpenTap.Host;

/// Session/DUT/report sidecar checks for `{planId}.program.json`.
internal static class PlanContractSidecar
{
    public static readonly string[] KnownReportKinds =
    [
        ReportKinds.Status,
        ReportKinds.Certification,
    ];

    private static readonly HashSet<string> KnownProperties = new(StringComparer.Ordinal)
    {
        "$schema",
        "displayName",
        "dutFamily",
        "requireSerial",
        "requirePartNumber",
        "requireRevision",
        "requireOperator",
        "reportKinds",
        "defaultReportKind",
        "selectionIncludesCleanup",
    };

    public static ProgramSidecar? Analyze(
        string sidecarPath,
        string planId,
        List<PlanContractFinding> findings)
    {
        string raw;
        try
        {
            raw = File.ReadAllText(sidecarPath);
        }
        catch (Exception ex)
        {
            findings.Add(Error(
                PlanContractValidator.Codes.SidecarInvalid,
                $"Invalid sidecar {planId}.program.json: {ex.Message}"));
            return null;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            findings.Add(Error(
                PlanContractValidator.Codes.SidecarEmpty,
                $"Empty sidecar {planId}.program.json"));
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (Exception ex)
        {
            findings.Add(Error(
                PlanContractValidator.Codes.SidecarInvalid,
                $"Invalid sidecar {planId}.program.json: {ex.Message}"));
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                findings.Add(Error(
                    PlanContractValidator.Codes.SidecarEmpty,
                    $"Empty sidecar {planId}.program.json"));
                return null;
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                findings.Add(Error(
                    PlanContractValidator.Codes.SidecarInvalid,
                    $"Sidecar {planId}.program.json must be a JSON object."));
                return null;
            }

            AnalyzeMembers(document.RootElement, planId, findings);
        }

        ProgramSidecar? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(raw, ProgramCatalogJsonContext.Default.ProgramSidecar);
        }
        catch (Exception ex)
        {
            findings.Add(Error(
                PlanContractValidator.Codes.SidecarInvalid,
                $"Invalid sidecar {planId}.program.json: {ex.Message}"));
            return null;
        }

        if (parsed is null)
        {
            findings.Add(Error(
                PlanContractValidator.Codes.SidecarEmpty,
                $"Empty sidecar {planId}.program.json"));
            return null;
        }

        AnalyzeReportKinds(parsed, planId, findings);
        return parsed;
    }

    private static void AnalyzeMembers(JsonElement root, string planId, List<PlanContractFinding> findings)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!KnownProperties.Contains(property.Name))
            {
                findings.Add(Warning(
                    PlanContractValidator.Codes.SidecarUnknownProperty,
                    $"Sidecar {planId}.program.json has unknown property '{property.Name}'. Instrument requirements belong in package Dependencies, not the sidecar."));
            }
        }
    }

    private static void AnalyzeReportKinds(ProgramSidecar parsed, string planId, List<PlanContractFinding> findings)
    {
        var declared = parsed.ReportKinds;
        if (declared is { Length: 0 })
        {
            findings.Add(Error(
                PlanContractValidator.Codes.SidecarReportKinds,
                $"Sidecar {planId}.program.json reportKinds must not be empty."));
            return;
        }

        if (declared is not null)
        {
            foreach (var kind in declared)
            {
                if (!IsKnownReportKind(kind))
                {
                    findings.Add(Error(
                        PlanContractValidator.Codes.SidecarReportKinds,
                        $"Sidecar {planId}.program.json reportKinds contains unknown '{kind}'. Use status or certification."));
                }
            }
        }

        var effective = declared is { Length: > 0 }
            ? declared
            : [ReportKinds.Status];

        if (string.IsNullOrWhiteSpace(parsed.DefaultReportKind))
        {
            return;
        }

        if (!IsKnownReportKind(parsed.DefaultReportKind))
        {
            findings.Add(Error(
                PlanContractValidator.Codes.SidecarDefaultReportKind,
                $"Sidecar {planId}.program.json defaultReportKind '{parsed.DefaultReportKind}' is unknown. Use status or certification."));
            return;
        }

        if (!effective.Any(k => string.Equals(k, parsed.DefaultReportKind, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(Error(
                PlanContractValidator.Codes.SidecarDefaultReportKind,
                $"Sidecar {planId}.program.json defaultReportKind '{parsed.DefaultReportKind}' must be listed in reportKinds."));
        }
    }

    public static bool IsKnownReportKind(string? kind)
        => !string.IsNullOrWhiteSpace(kind)
           && KnownReportKinds.Any(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase));

    private static PlanContractFinding Error(string code, string message)
        => new(PlanContractSeverity.Error, code, message);

    private static PlanContractFinding Warning(string code, string message)
        => new(PlanContractSeverity.Warning, code, message);
}
