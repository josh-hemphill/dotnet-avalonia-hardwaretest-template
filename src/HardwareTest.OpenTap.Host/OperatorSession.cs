using System.ComponentModel;
using System.Runtime.CompilerServices;

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
    private OperatorSessionState _state = OperatorSessionState.NeedsDut;

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public DateTimeOffset? ConfirmedAt
    {
        get => _confirmedAt;
        private set => Set(ref _confirmedAt, value);
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

        DutSerial = serial.Trim();
        DutPartNumber = string.IsNullOrWhiteSpace(partNumber) ? null : partNumber.Trim();
        DutRevision = string.IsNullOrWhiteSpace(revision) ? null : revision.Trim();
        DutFamily = string.IsNullOrWhiteSpace(family) ? "generic" : family.Trim();
        ConfirmedAt = DateTimeOffset.UtcNow;
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
        if (State == OperatorSessionState.Active)
        {
            State = OperatorSessionState.Stale;
            Raise(nameof(CanRun));
        }
    }

    public void ConfirmSameDut()
    {
        if (string.IsNullOrWhiteSpace(DutSerial))
        {
            State = OperatorSessionState.NeedsDut;
        }
        else
        {
            ConfirmedAt = DateTimeOffset.UtcNow;
            State = OperatorSessionState.Active;
        }

        Raise(nameof(CanRun));
    }

    public void TouchActivity()
    {
        if (State == OperatorSessionState.Active)
        {
            ConfirmedAt = DateTimeOffset.UtcNow;
        }
    }

    /// Marks session Stale when idle longer than the configured timeout.
    public void CheckIdleStale(TimeSpan idleTimeout, DateTimeOffset? now = null)
    {
        if (State != OperatorSessionState.Active || ConfirmedAt is null)
        {
            return;
        }

        var clock = now ?? DateTimeOffset.UtcNow;
        if (clock - ConfirmedAt.Value >= idleTimeout)
        {
            MarkStale();
        }
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
