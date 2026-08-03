using System.Globalization;
using System.Text;
using OpenTap;
using Serilog;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host;

/// Writes OpenTAP published ResultTables as CSV under a run folder (MES / QA handoff).
internal sealed class OpenTapFileResultExportListener : ResultListener
{
    private readonly string _outputDirectory;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, StreamWriter> _writers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _headersWritten = new(StringComparer.OrdinalIgnoreCase);

    public OpenTapFileResultExportListener(string outputDirectory, ILogger? logger = null)
    {
        _outputDirectory = outputDirectory;
        _logger = logger ?? Serilog.Log.ForContext<OpenTapFileResultExportListener>();
        Name = "HardwareTestFileExport";
    }

    public override void OnTestPlanRunStart(TestPlanRun planRun)
    {
        try
        {
            Directory.CreateDirectory(_outputDirectory);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OpenTAP result export: could not create {Directory}", _outputDirectory);
        }
    }

    public override void OnResultPublished(Guid stepRunId, ResultTable result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Name))
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                WriteTable(stepRunId, result);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OpenTAP result export failed for table {Table}", result.Name);
        }
    }

    public override void OnTestPlanRunCompleted(TestPlanRun planRun, Stream logStream)
        => FlushAndClose();

    public override void Close()
    {
        FlushAndClose();
        base.Close();
    }

    private void WriteTable(Guid stepRunId, ResultTable result)
    {
        var columns = result.Columns ?? [];
        var writer = GetOrCreateWriter(result.Name, columns);
        if (writer is null)
        {
            return;
        }

        var rowCount = result.Rows > 0
            ? result.Rows
            : columns.Length == 0
                ? 0
                : columns.Max(c => c.Data?.Length ?? 0);

        if (rowCount == 0)
        {
            writer.Write(Escape(stepRunId.ToString()));
            for (var c = 0; c < columns.Length; c++)
            {
                writer.Write(',');
            }

            writer.WriteLine();
            return;
        }

        for (var row = 0; row < rowCount; row++)
        {
            writer.Write(Escape(stepRunId.ToString()));
            foreach (var column in columns)
            {
                writer.Write(',');
                var value = column.Data is not null && row < column.Data.Length
                    ? FormatCell(column.Data.GetValue(row))
                    : string.Empty;
                writer.Write(Escape(value));
            }

            writer.WriteLine();
        }
    }

    private StreamWriter? GetOrCreateWriter(string tableName, ResultColumn[] columns)
    {
        if (_writers.TryGetValue(tableName, out var existing))
        {
            return existing;
        }

        try
        {
            Directory.CreateDirectory(_outputDirectory);
            var path = Path.Combine(_outputDirectory, SanitizeFileName(tableName) + ".csv");
            var writer = new StreamWriter(File.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8)
            {
                AutoFlush = true,
            };
            _writers[tableName] = writer;

            if (_headersWritten.Add(tableName))
            {
                writer.Write("StepRunId");
                foreach (var column in columns)
                {
                    writer.Write(',');
                    writer.Write(Escape(column.Name ?? string.Empty));
                }

                writer.WriteLine();
            }

            return writer;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OpenTAP result export: could not open CSV for {Table}", tableName);
            return null;
        }
    }

    private void FlushAndClose()
    {
        lock (_sync)
        {
            foreach (var writer in _writers.Values)
            {
                try
                {
                    writer.Flush();
                    writer.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "OpenTAP result export: flush/close failed");
                }
            }

            _writers.Clear();
        }
    }

    private static string FormatCell(object? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    internal static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "results" : name;
    }
}
