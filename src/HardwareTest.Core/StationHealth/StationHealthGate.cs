using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;

namespace HardwareTest.Core.StationHealth;

/// Reads the station store and applies DUT sidecar + optional station override.
public sealed class StationHealthGate : IStationHealthGate
{
    public const double DefaultMaxAgeHours = 24;

    private readonly IStationHealthStore _store;
    private readonly AppSettings _settings;

    public StationHealthGate(IStationHealthStore store, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        _store = store;
        _settings = settings;
    }

    /// Off when the program is health, sidecar omitted the keys, or the station pin is off.
    public StationHealthGateResult Evaluate(StationHealthGateRequest program, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(clock);

        if (ProgramKinds.IsStationHealth(program.ProgramKind) || !program.RequireStationHealth)
        {
            return Off();
        }

        var pin = _settings.StationHealthGateOverride?.Trim();
        if (string.Equals(pin, StationHealthGateOverrides.Off, StringComparison.OrdinalIgnoreCase))
        {
            return Off();
        }

        var enforcement = ResolveEnforcement(pin, program.Gate);
        var profileId = FirstNonEmpty(
            program.ProfileId,
            _settings.StationHealthProfileId,
            FileStationHealthStore.DefaultProfileId);
        var record = _store.TryRead(profileId);
        if (record is null)
        {
            return Decision(enforcement, age: null, verdict: null, missing: true);
        }

        var maxHours = ResolveMaxAgeHours(program.MaxAgeHours, record.MaxAgeHours);
        var age = clock.UtcNow - record.MeasuredAt;
        var stale = !string.Equals(record.Verdict, StationHealthVerdicts.Pass, StringComparison.OrdinalIgnoreCase)
                    || age > TimeSpan.FromHours(maxHours);
        if (!stale)
        {
            return new StationHealthGateResult
            {
                Level = StationHealthGateLevels.Ok,
                Age = age,
                Verdict = record.Verdict,
            };
        }

        return Decision(enforcement, age, record.Verdict, missing: false);
    }

    private static StationHealthGateResult Off()
        => new() { Level = StationHealthGateLevels.Off };

    private static StationHealthGateResult Decision(
        string enforcement,
        TimeSpan? age,
        string? verdict,
        bool missing)
    {
        var level = string.Equals(enforcement, StationHealthGates.Block, StringComparison.OrdinalIgnoreCase)
            ? StationHealthGateLevels.Block
            : StationHealthGateLevels.Warn;
        return new StationHealthGateResult
        {
            Level = level,
            Message = FormatMessage(age, verdict, missing),
            Age = age,
            Verdict = verdict,
        };
    }

    /// Operator-facing stale / missing copy used on the Run banner and tip.
    public static string FormatMessage(TimeSpan? age, string? verdict, bool missing)
    {
        if (missing)
        {
            return "Station health missing. Run Station health or wait for a passing cal.";
        }

        var ageText = FormatAge(age);
        if (!string.IsNullOrWhiteSpace(verdict)
            && !string.Equals(verdict, StationHealthVerdicts.Pass, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrEmpty(ageText)
                ? $"Station health {verdict}. Run Station health or wait for a passing cal."
                : $"Station health {verdict} ({ageText}). Run Station health or wait for a passing cal.";
        }

        return string.IsNullOrEmpty(ageText)
            ? "Station health stale. Run Station health or wait for a passing cal."
            : $"Station health stale ({ageText}). Run Station health or wait for a passing cal.";
    }

    public static string FormatAge(TimeSpan? age)
    {
        if (age is not { } span || span < TimeSpan.Zero)
        {
            return string.Empty;
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours} h";
        }

        return $"{Math.Max(1, (int)span.TotalMinutes)} m";
    }

    /// DUT sidecar max age wins when set; otherwise the stored record; otherwise 24 h.
    private static double ResolveMaxAgeHours(double? sidecarHours, double? recordHours)
    {
        if (sidecarHours is > 0)
        {
            return sidecarHours.Value;
        }

        if (recordHours is > 0)
        {
            return recordHours.Value;
        }

        return DefaultMaxAgeHours;
    }

    private static string ResolveEnforcement(string? pin, string? sidecarGate)
    {
        if (string.Equals(pin, StationHealthGateOverrides.Warn, StringComparison.OrdinalIgnoreCase)
            || string.Equals(pin, StationHealthGateOverrides.Block, StringComparison.OrdinalIgnoreCase))
        {
            return pin!;
        }

        return string.IsNullOrWhiteSpace(sidecarGate) ? StationHealthGates.Warn : sidecarGate.Trim();
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return FileStationHealthStore.DefaultProfileId;
    }
}
