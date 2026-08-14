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
public sealed class OperatorSession : INotifyPropertyChanged
{
    private string _sessionId = Guid.NewGuid().ToString("N");
    private string _dutSerial = string.Empty;
    private string? _dutPartNumber;
    private string? _dutRevision;
    private string _dutFamily = "generic";
    private string? _programId;
    private string? _programPath;
    private string? _programDisplayName;
    private string? _operatorName;
    private DateTimeOffset? _confirmedAt;
    private DateTimeOffset? _lastActivityAt;
    private bool _isIdleWarning;
    private TimeSpan? _timeUntilSoftWarn;
    private TimeSpan? _timeUntilStale;
    private OperatorSessionState _state = OperatorSessionState.NeedsDut;
    private readonly IClock _clock;

    public event PropertyChangedEventHandler? PropertyChanged;

    public OperatorSession(IClock? clock = null)
    {
        _clock = clock ?? SystemClock.Instance;
    }

    public string SessionId
    {
        get => _sessionId;
        private set => Set(ref _sessionId, value);
    }

    public string DutSerial
    {
        get => _dutSerial;
        private set => Set(ref _dutSerial, value);
    }

    public string? DutPartNumber
    {
        get => _dutPartNumber;
        private set => Set(ref _dutPartNumber, value);
    }

    public string? DutRevision
    {
        get => _dutRevision;
        private set => Set(ref _dutRevision, value);
    }

    public string DutFamily
    {
        get => _dutFamily;
        private set => Set(ref _dutFamily, value);
    }

    public string? ProgramId
    {
        get => _programId;
        private set => Set(ref _programId, value);
    }

    public string? ProgramPath
    {
        get => _programPath;
        private set => Set(ref _programPath, value);
    }

    public string? ProgramDisplayName
    {
        get => _programDisplayName;
        private set => Set(ref _programDisplayName, value);
    }

    public string? OperatorName
    {
        get => _operatorName;
        set => Set(ref _operatorName, value);
    }

    /// When the DUT identity was last confirmed (not refreshed by activity touches).
    public DateTimeOffset? ConfirmedAt
    {
        get => _confirmedAt;
        private set => Set(ref _confirmedAt, value);
    }

    /// Last meaningful operator activity used for idle / stale timing.
    public DateTimeOffset? LastActivityAt
    {
        get => _lastActivityAt;
        private set => Set(ref _lastActivityAt, value);
    }

    /// True when idle elapsed has reached the soft-warn fraction but not hard Stale.
    public bool IsIdleWarning
    {
        get => _isIdleWarning;
        private set => Set(ref _isIdleWarning, value);
    }

    public TimeSpan? TimeUntilSoftWarn
    {
        get => _timeUntilSoftWarn;
        private set => Set(ref _timeUntilSoftWarn, value);
    }

    public TimeSpan? TimeUntilStale
    {
        get => _timeUntilStale;
        private set => Set(ref _timeUntilStale, value);
    }

    public OperatorSessionState State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    public bool CanRun => State == OperatorSessionState.Active && !string.IsNullOrWhiteSpace(DutSerial);

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

        ConfirmDut(serial, partNumber, revision, family);
        OperatorName = string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim();
        error = string.Empty;
        return true;
    }

    public void ConfirmDut(string serial, string? partNumber = null, string? revision = null, string family = "generic")
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("DUT serial is required.", nameof(serial));
        }

        var now = _clock.UtcNow;
        DutSerial = serial.Trim();
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
        if (State == OperatorSessionState.Active || State == OperatorSessionState.Stale)
        {
            State = OperatorSessionState.Stale;
            ClearIdleCountdown();
            Raise(nameof(CanRun));
        }
    }

    public void ConfirmSameDut()
    {
        if (string.IsNullOrWhiteSpace(DutSerial))
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
