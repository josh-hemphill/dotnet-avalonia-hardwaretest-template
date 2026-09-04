using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HardwareTest.OpenTap.Host;

/// Text / JSON / SARIF renderers for plan-contract reports.
internal static class PlanContractFormatWriter
{
    public static string Write(PlanContractBatchReport batch, PlanContractFormat format)
        => format switch
        {
            PlanContractFormat.Json => WriteJson(batch),
            PlanContractFormat.Sarif => WriteSarif(batch),
            _ => PlanContractValidator.Format(batch),
        };

    private static string WriteJson(PlanContractBatchReport batch)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("hasErrors", batch.HasErrors);
            writer.WriteNumber("errorCount", batch.ErrorCount);
            writer.WriteNumber("warningCount", batch.WarningCount);
            writer.WriteStartArray("plans");
            foreach (var plan in batch.Plans)
            {
                writer.WriteStartObject();
                writer.WriteString("path", plan.TargetPath);
                writer.WriteBoolean("hasErrors", plan.HasErrors);
                writer.WriteNumber("errorCount", plan.ErrorCount);
                writer.WriteNumber("warningCount", plan.WarningCount);
                writer.WriteStartArray("findings");
                foreach (var finding in plan.Findings)
                {
                    WriteFinding(writer, finding);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    private static string WriteSarif(PlanContractBatchReport batch)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", "https://json.schemastore.org/sarif-2.1.0.json");
            writer.WriteString("version", "2.1.0");
            writer.WriteStartArray("runs");
            writer.WriteStartObject();
            writer.WriteStartObject("tool");
            writer.WriteStartObject("driver");
            writer.WriteString("name", "HardwareTest.PlanValidate");
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteStartArray("results");
            foreach (var plan in batch.Plans)
            {
                foreach (var finding in plan.Findings)
                {
                    writer.WriteStartObject();
                    writer.WriteString("ruleId", finding.Code);
                    writer.WriteString(
                        "level",
                        finding.Severity == PlanContractSeverity.Error ? "error" : "warning");
                    writer.WriteStartObject("message");
                    writer.WriteString("text", finding.Message);
                    writer.WriteEndObject();
                    writer.WriteStartArray("locations");
                    writer.WriteStartObject();
                    writer.WriteStartObject("physicalLocation");
                    writer.WriteStartObject("artifactLocation");
                    writer.WriteString("uri", ToUri(plan.TargetPath));
                    writer.WriteEndObject();
                    if (!string.IsNullOrWhiteSpace(finding.Path))
                    {
                        writer.WriteStartObject("region");
                        writer.WriteStartObject("message");
                        writer.WriteString("text", finding.Path);
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    private static void WriteFinding(Utf8JsonWriter writer, PlanContractFinding finding)
    {
        writer.WriteStartObject();
        writer.WriteString("severity", finding.Severity.ToString());
        writer.WriteString("code", finding.Code);
        writer.WriteString("message", finding.Message);
        if (!string.IsNullOrWhiteSpace(finding.Path))
        {
            writer.WriteString("path", finding.Path);
        }

        writer.WriteEndObject();
    }

    private static string ToUri(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute) && absolute.IsFile)
        {
            return absolute.AbsoluteUri;
        }

        var full = Path.GetFullPath(path);
        return new Uri(full).AbsoluteUri;
    }
}
