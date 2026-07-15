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
    private readonly IDisposable _activityListener;

    private LoggingBootstrap(IDisposable activityListener)
    {
        _activityListener = activityListener;
    }

    public IDisposable ActivityListener => _activityListener;

    public static LoggingBootstrap Initialize(AppSettings settings, string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        var level = ParseLevel(settings.LogMinimumLevel);

        var config = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "HardwareTest")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] ({TestRunId}) {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(logDirectory, "hardware-test-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true);

        if (settings.EnableOsEventSink && OperatingSystem.IsWindows())
        {
            config.WriteTo.EventLog(
                source: "HardwareTest",
                logName: "Application",
                manageEventSource: false);
        }

        if (settings.EnableSyslogOnUnix
            && !OperatingSystem.IsWindows()
            && !string.IsNullOrWhiteSpace(settings.SyslogHost))
        {
            config.WriteTo.UdpSyslog(
                settings.SyslogHost!,
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
        _activityListener.Dispose();
        Log.CloseAndFlush();
    }

    private static LogEventLevel ParseLevel(string? level)
    {
        return Enum.TryParse(level, ignoreCase: true, out LogEventLevel parsed)
            ? parsed
            : LogEventLevel.Information;
    }
}

/// Creates correlated Activities for test runs and steps.
public static class TestTracing
{
    public static readonly ActivitySource Source = new("HardwareTest");

    public static Activity? StartRun(string runId, string planName, string? dutSerial)
    {
        var activity = Source.StartActivity("TestRun", ActivityKind.Internal);
        activity?.SetTag("run.id", runId);
        activity?.SetTag("plan.name", planName);
        if (!string.IsNullOrWhiteSpace(dutSerial))
        {
            activity?.SetTag("dut.serial", dutSerial);
        }

        return activity;
    }

    public static Activity? StartStep(string stepId, string stepType)
    {
        var activity = Source.StartActivity("TestStep", ActivityKind.Internal);
        activity?.SetTag("step.id", stepId);
        activity?.SetTag("step.type", stepType);
        return activity;
    }

    public static Activity? StartVisa(string operation, string resource)
    {
        var activity = Source.StartActivity("VisaIo", ActivityKind.Client);
        activity?.SetTag("visa.operation", operation);
        activity?.SetTag("visa.resource", resource);
        return activity;
    }
}
