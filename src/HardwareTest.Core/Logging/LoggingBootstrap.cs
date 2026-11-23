using System.Diagnostics;
using HardwareTest.Core.Settings;
using Serilog;
using Serilog.Events;
using SerilogTracing;
using SerilogTracing.Configuration;

namespace HardwareTest.Core.Logging;

public interface ILoggingBootstrap : IDisposable
{
    IDisposable ActivityListener { get; }
}

/// Configures Serilog sinks in code (AoT-safe) and bridges Activities.
public sealed class LoggingBootstrap : ILoggingBootstrap
{
    private LoggingBootstrap(IDisposable activityListener) => ActivityListener = activityListener;

    public IDisposable ActivityListener { get; }

    public static LoggingBootstrap Initialize(AppSettings settings, string logDirectory)
    {
        _ = Directory.CreateDirectory(logDirectory);
        var level = ParseLevel(settings.LogMinimumLevel);
        var ring = new RingBufferSink();
        RingBufferSink.Shared = ring;

        var config = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Avalonia", LogEventLevel.Warning)
            .Filter.ByExcluding(IsNoisyAvaloniaLog)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "HardwareTest")
            .WriteTo.Sink(ring)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] ({TestRunId}) {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(logDirectory, "hardware-test-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true);

        if (settings.EnableOsEventSink && OperatingSystem.IsWindows())
        {
            _ = config.WriteTo.EventLog(
                source: "HardwareTest",
                logName: "Application",
                manageEventSource: false);
        }

        if (settings.EnableSyslogOnUnix
            && !OperatingSystem.IsWindows()
            && !string.IsNullOrWhiteSpace(settings.SyslogHost))
        {
            _ = config.WriteTo.UdpSyslog(
                settings.SyslogHost,
                port: settings.SyslogPort,
                appName: "HardwareTest",
                format: Serilog.Sinks.Syslog.SyslogFormat.RFC5424);
        }

        Log.Logger = config.CreateLogger();

        IDisposable listener = new ActivityListenerConfiguration()
            .TraceToSharedLogger();

        Log.Information("Logging initialized at {Level}; directory {LogDirectory}", level, logDirectory);
        return new LoggingBootstrap(listener);
    }

    public void Dispose()
    {
        ActivityListener.Dispose();
        Log.CloseAndFlush();
    }

    private static LogEventLevel ParseLevel(string? level)
    {
        return Enum.TryParse(level, ignoreCase: true, out LogEventLevel parsed)
            ? parsed
            : LogEventLevel.Information;
    }

    /// Avalonia DeveloperTools / style system emits thousands of Information events that drown the console.
    private static bool IsNoisyAvaloniaLog(LogEvent logEvent)
    {
        if (logEvent.Level > LogEventLevel.Information)
        {
            return false;
        }

        var text = logEvent.MessageTemplate.Text;
        return text.StartsWith("Avalonia.", StringComparison.Ordinal)
               || text.Contains("FindingResource", StringComparison.Ordinal)
               || text.Contains("AttachingStyle", StringComparison.Ordinal)
               || text.Contains("PerformingHitTest", StringComparison.Ordinal)
               || text.Contains("RaisingRoutedEvent", StringComparison.Ordinal);
    }
}

/// Creates correlated Activities for test runs and steps.
public static class TestTracing
{
    public static readonly ActivitySource Source = new("HardwareTest");

    public static Activity? StartRun(string runId, string planName, string? dutSerial)
    {
        var activity = Source.StartActivity("TestRun", ActivityKind.Internal);
        _ = (activity?.SetTag("run.id", runId));
        _ = (activity?.SetTag("plan.name", planName));
        if (!string.IsNullOrWhiteSpace(dutSerial))
        {
            _ = (activity?.SetTag("dut.serial", dutSerial));
        }

        return activity;
    }

    public static Activity? StartStep(string stepId, string stepType)
    {
        var activity = Source.StartActivity("TestStep", ActivityKind.Internal);
        _ = (activity?.SetTag("step.id", stepId));
        _ = (activity?.SetTag("step.type", stepType));
        return activity;
    }

    public static Activity? StartVisa(string operation, string resource)
    {
        var activity = Source.StartActivity("VisaIo", ActivityKind.Client);
        _ = (activity?.SetTag("visa.operation", operation));
        _ = (activity?.SetTag("visa.resource", resource));
        return activity;
    }
}
