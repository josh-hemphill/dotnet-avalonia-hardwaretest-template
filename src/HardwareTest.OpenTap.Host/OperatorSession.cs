using System.ComponentModel;
using System.Runtime.CompilerServices;
using HardwareTest.Core.Time;

namespace HardwareTest.OpenTap.Host;

public enum OperatorSessionState
{
    NeedsDut = 0,
    Active = 1,
    Stale = 2,
}

/// Bench context: DUT identity + selected program without over-prompting.
public sealed class OperatorSession(IClock? clock = null) : INotifyPropertyChanged
{
    private readonly IClock _clock = clock ?? SystemClock.Instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SessionId
    {
        get;
        private set => Set(ref field, value);
    } = Guid.NewGuid().ToString("N");

    public string DutSerial
    {
        get;
        private set => Set(ref field, value);
    } = string.Empty;

    public string? DutPartNumber
    {
        get;
        private set => Set(ref field, value);
    }

    public string? DutRevision
    {
        get;
        private set => Set(ref field, value);
    }

    public string DutFamily
    {
        get;
        private set => Set(ref field, value);
    } = "generic";

    public string? ProgramId
    {
        get;
        private set => Set(ref field, value);
    }

    public string? ProgramPath
    {
        get;
        private set => Set(ref field, value);
    }

    public string? ProgramDisplayName
    {
        get;
        private set => Set(ref field, value);
    }

    public string? OperatorName
    {
        get;
        set => Set(ref field, value);
    }

    /// Card serial captured from a chip insert or contactless tap.
    public string? OperatorCredentialSerial
    {
        get;
        private set => Set(ref field, value);
    }

    /// `contact` or `contactless` when a badge has been presented.
    public string? OperatorCredentialTransport
    {
        get;
        private set => Set(ref field, value);
    }

    /// When the DUT identity was last confirmed (not refreshed by activity touches).
    public DateTimeOffset? ConfirmedAt
    {
        get;
        private set => Set(ref field, value);
    }

    /// Last meaningful operator activity used for idle / stale timing.
    public DateTimeOffset? LastActivityAt
    {
        get;
        private set => Set(ref field, value);
    }

    /// True when idle elapsed has reached the soft-warn fraction but not hard Stale.
    public bool IsIdleWarning
    {
        get;
        private set => Set(ref field, value);
    }

    public TimeSpan? TimeUntilSoftWarn
    {
        get;
        private set => Set(ref field, value);
    }

    public TimeSpan? TimeUntilStale
    {
        get;
        private set => Set(ref field, value);
    }

    public OperatorSessionState State
    {
        get;
        private set => Set(ref field, value);
    } = OperatorSessionState.NeedsDut;

    /// Current program requirements. Default Sample still requires a DUT serial.
    public ProgramRequirements Requirements { get; private set; } = ProgramRequirements.Sample;

    public bool CanRun => State == OperatorSessionState.Active
        && (!Requirements.RequireSerial || !string.IsNullOrWhiteSpace(DutSerial));

    public DutIdentity ToDutIdentity() => new(DutSerial, DutPartNumber, DutRevision, DutFamily);

    /// Validates required session fields for the active program and activates the session.
    public bool TryConfirm(ProgramRequirements requirements, string serial, string? partNumber, string? revision, string? operatorName, string family, out string error)
    {
        if (requirements.RequireSerial && string.IsNullOrWhiteSpace(serial))
        {
            error = "DUT serial is required.";
            return false;
        }

        if (requirements.RequirePartNumber && string.IsNullOrWhiteSpace(partNumber))
        {
            error = "DUT part number is required for this program.";
            return false;
        }

        if (requirements.RequireRevision && string.IsNullOrWhiteSpace(revision))
        {
            error = "DUT revision is required for this program.";
            return false;
        }

        if (requirements.RequireOperator && string.IsNullOrWhiteSpace(operatorName))
        {
            error = "Operator name is required for this program.";
            return false;
        }

        ApplyProgramRequirements(requirements);
        ConfirmDut(serial, partNumber, revision, family);
        OperatorName = string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim();
        error = string.Empty;
        return true;
    }

    /// Updates CanRun when the selected program's session requirements change.
    public void ApplyProgramRequirements(ProgramRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        Requirements = requirements;
        Raise(nameof(CanRun));
    }

    /// Records the presented badge without replacing DUT identity.
    public void ApplyOperatorCredential(string serial, string transport, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Credential serial is required.", nameof(serial));
        }

        OperatorCredentialSerial = serial.Trim();
        OperatorCredentialTransport = string.IsNullOrWhiteSpace(transport) ? null : transport.Trim();
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            OperatorName = displayName.Trim();
        }
    }

    public void ConfirmDut(string serial, string? partNumber = null, string? revision = null, string family = "generic")
    {
        var now = _clock.UtcNow;
        DutSerial = string.IsNullOrWhiteSpace(serial) ? string.Empty : serial.Trim();
        DutPartNumber = string.IsNullOrWhiteSpace(partNumber) ? null : partNumber.Trim();
        DutRevision = string.IsNullOrWhiteSpace(revision) ? null : revision.Trim();
        DutFamily = string.IsNullOrWhiteSpace(family) ? "generic" : family.Trim();
        ConfirmedAt = now;
        LastActivityAt = now;
        ClearIdleCountdown();
        State = OperatorSessionState.Active;
        Raise(nameof(CanRun));
    }

    public void ChangeSession()
    {
        ChangeDut();
        OperatorName = null;
        OperatorCredentialSerial = null;
        OperatorCredentialTransport = null;
    }

    public void ChangeDut()
    {
        DutSerial = string.Empty;
        DutPartNumber = null;
        DutRevision = null;
        ConfirmedAt = null;
        LastActivityAt = null;
        ClearIdleCountdown();
        State = OperatorSessionState.NeedsDut;
        SessionId = Guid.NewGuid().ToString("N");
        Raise(nameof(CanRun));
    }

    public void SelectProgram(string id, string path, string displayName, string? dutFamily = null)
    {
        if (!string.IsNullOrWhiteSpace(dutFamily)
            && State == OperatorSessionState.Active
            && !string.Equals(DutFamily, dutFamily, StringComparison.OrdinalIgnoreCase))
        {
            State = OperatorSessionState.Stale;
            ClearIdleCountdown();
            Raise(nameof(CanRun));
        }

        ProgramId = id;
        ProgramPath = path;
        ProgramDisplayName = displayName;
        if (!string.IsNullOrWhiteSpace(dutFamily))
        {
            DutFamily = dutFamily;
        }
    }

    public void MarkStale()
    {
        if (State is OperatorSessionState.Active or OperatorSessionState.Stale)
        {
            State = OperatorSessionState.Stale;
            ClearIdleCountdown();
            Raise(nameof(CanRun));
        }
    }

    public void ConfirmSameDut()
    {
        if (Requirements.RequireSerial && string.IsNullOrWhiteSpace(DutSerial))
        {
            State = OperatorSessionState.NeedsDut;
            ClearIdleCountdown();
        }
        else
        {
            var now = _clock.UtcNow;
            ConfirmedAt = now;
            LastActivityAt = now;
            ClearIdleCountdown();
            State = OperatorSessionState.Active;
        }

        Raise(nameof(CanRun));
    }

    /// Refreshes last activity only (identity confirm time stays unchanged).
    public void TouchActivity(DateTimeOffset? now = null)
    {
        if (State != OperatorSessionState.Active)
        {
            return;
        }

        LastActivityAt = now ?? _clock.UtcNow;
    }

    /// Marks session Stale when idle longer than the configured timeout (uses last activity).
    public void CheckIdleStale(TimeSpan idleTimeout, DateTimeOffset? now = null)
        => EvaluateIdle(idleTimeout, warnPercent: 100, now);

    /// Updates soft-warn / remaining-time and may mark Stale from last activity.
    public void EvaluateIdle(TimeSpan idleTimeout, int warnPercent, DateTimeOffset? now = null)
    {
        if (State != OperatorSessionState.Active || LastActivityAt is null)
        {
            ClearIdleCountdown();
            return;
        }

        if (idleTimeout <= TimeSpan.Zero)
        {
            MarkStale();
            return;
        }

        var clock = now ?? _clock.UtcNow;
        var elapsed = clock - LastActivityAt.Value;
        var remainingStale = idleTimeout - elapsed;
        TimeUntilStale = remainingStale > TimeSpan.Zero ? remainingStale : TimeSpan.Zero;

        var clampedWarn = Math.Clamp(warnPercent, 50, 95);
        var softAt = TimeSpan.FromTicks(idleTimeout.Ticks * clampedWarn / 100);
        var remainingWarn = softAt - elapsed;
        TimeUntilSoftWarn = remainingWarn > TimeSpan.Zero ? remainingWarn : TimeSpan.Zero;

        if (elapsed >= idleTimeout)
        {
            MarkStale();
            return;
        }

        IsIdleWarning = elapsed >= softAt;
    }

    private void ClearIdleCountdown()
    {
        IsIdleWarning = false;
        TimeUntilSoftWarn = null;
        TimeUntilStale = null;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        Raise(name);
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
