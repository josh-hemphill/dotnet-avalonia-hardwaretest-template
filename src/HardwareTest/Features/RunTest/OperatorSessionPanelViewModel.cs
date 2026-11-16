using System;
using System.Globalization;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;
using HardwareTest.OpenTap.Host;
using HardwareTest.UiThreading;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// DUT / technician confirmation panel: confirm, same-DUT, change session and stale prompts.
public partial class OperatorSessionPanelViewModel : ReactiveObject
{
    private readonly OperatorSession _session;
    private readonly AppSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly Func<ProgramItemViewModel?> _getSelectedProgram;
    private readonly Action _onSessionCleared;
    private readonly IClock _clock;
    private readonly IOperatorCredentialBroker? _credentialBroker;
    private readonly Func<bool> _isRunning;
    private readonly System.Timers.Timer _idleTimer;
    private bool _requiresFullConfirmation;

    /// Test seam: routes idle-timer UI work synchronously instead of through the Avalonia dispatcher.
    public Action<Action>? UiScheduler { get; set; }

    public OperatorSessionPanelViewModel(
        OperatorSession session,
        AppSettings settings,
        Action<string> setStatus,
        Func<ProgramItemViewModel?>? getSelectedProgram = null,
        Action? onSessionCleared = null,
        IClock? clock = null,
        IOperatorCredentialBroker? credentialBroker = null,
        Func<bool>? isRunning = null)
    {
        _session = session;
        _settings = settings;
        _setStatus = setStatus;
        _getSelectedProgram = getSelectedProgram ?? (() => null);
        _onSessionCleared = onSessionCleared ?? (() => { });
        _clock = clock ?? SystemClock.Instance;
        _credentialBroker = credentialBroker;
        _isRunning = isRunning ?? (() => false);

        ConfirmSessionCommand = ReactiveCommand.Create(ConfirmSession);
        ConfirmSameDutCommand = ReactiveCommand.Create(ConfirmSameDut);
        ChangeSessionCommand = ReactiveCommand.Create(ChangeSession);
        CaptureCredentialCommand = ReactiveCommand.CreateFromTask(CaptureCredentialAsync);
        ShowCredentialCapture = _credentialBroker is not null;
        CredentialStatus = _credentialBroker?.StatusText ?? string.Empty;
        PropertyChanged += (_, change) =>
        {
            switch (change.PropertyName)
            {
                case nameof(DutSerialInput): DutSerialError = string.Empty; this.RaisePropertyChanged(nameof(HasDutSerialError)); break;
                case nameof(DutPartInput): DutPartError = string.Empty; this.RaisePropertyChanged(nameof(HasDutPartError)); break;
                case nameof(DutRevisionInput): DutRevisionError = string.Empty; this.RaisePropertyChanged(nameof(HasDutRevisionError)); break;
                case nameof(OperatorInput): OperatorError = string.Empty; this.RaisePropertyChanged(nameof(HasOperatorError)); break;
            }
        };

        _idleTimer = new System.Timers.Timer(15_000) { AutoReset = true };
        _idleTimer.Elapsed += (_, _) =>
        {
            try
            {
                UiDispatch.Post(
                    () =>
                    {
                        try
                        {
                            ApplyIdleStaleCheck();
                            RefreshSessionSummary();
                        }
                        catch
                        {
                            // Timer must not crash the process (Post runs later on the UI thread).
                        }
                    },
                    UiScheduler);
            }
            catch
            {
                // Timer must not crash the process.
            }
        };
        _session.PropertyChanged += (_, e) =>
        {
            if ((e.PropertyName == nameof(OperatorSession.State) && _session.State == OperatorSessionState.Stale)
                || e.PropertyName is nameof(OperatorSession.SessionId)
                    or nameof(OperatorSession.ProgramId)
                    or nameof(OperatorSession.ProgramPath))
            {
                InvalidateCredentialCapture();
            }

            if (e.PropertyName is nameof(OperatorSession.State)
                or nameof(OperatorSession.CanRun)
                or nameof(OperatorSession.IsIdleWarning))
            {
                UpdateIdleTimer();
            }
        };
        UpdateIdleTimer();
    }

    public OperatorSession Session => _session;

    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> ConfirmSessionCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> ConfirmSameDutCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> ChangeSessionCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> CaptureCredentialCommand { get; }

    /// Raised when the DUT serial field should take keyboard/scanner focus.
    public event EventHandler? RequestFocusDutSerial;
    public event Action<string>? RequestFocusSessionField;
    public event EventHandler? RequestFocusRun;

    [Reactive] private string _dutSerialInput = string.Empty;
    [Reactive] private string _dutPartInput = string.Empty;
    [Reactive] private string _dutRevisionInput = string.Empty;
    [Reactive] private string _operatorInput = string.Empty;
    [Reactive] private bool _requireSerial = true;
    [Reactive] private string _dutSerialPlaceholder = "DUT serial *";
    [Reactive] private bool _requirePartNumber;
    [Reactive] private bool _requireRevision;
    [Reactive] private bool _requireOperator = true;
    [Reactive] private string _technicianPlaceholder = "Technician *";
    [Reactive] private bool _showSessionForm = true;
    [Reactive] private bool _sessionBlocked = true;
    [Reactive] private string _sessionSummary = "Session: (confirm required)";
    [Reactive] private bool _needsDutConfirm = true;
    [Reactive] private bool _isStalePrompt;
    [Reactive] private bool _isIdleWarningPrompt;
    [Reactive] private bool _showStaleTechnicianField;
    [Reactive] private string _idleCountdownText = string.Empty;
    [Reactive] private bool _showIdleCountdown;
    [Reactive] private bool _pendingConfirmEveryRun;
    [Reactive] private string _dutSerialError = string.Empty;
    [Reactive] private string _dutPartError = string.Empty;
    [Reactive] private string _dutRevisionError = string.Empty;
    [Reactive] private string _operatorError = string.Empty;
    [Reactive] private string _credentialStatus = string.Empty;
    [Reactive] private bool _isCapturingCredential;
    [Reactive] private bool _showCredentialCapture;

    public bool HasDutSerialError => !string.IsNullOrWhiteSpace(DutSerialError);
    public bool HasDutPartError => !string.IsNullOrWhiteSpace(DutPartError);
    public bool HasDutRevisionError => !string.IsNullOrWhiteSpace(DutRevisionError);
    public bool HasOperatorError => !string.IsNullOrWhiteSpace(OperatorError);
    public bool ShowSameDutPrompt => IsStalePrompt || IsIdleWarningPrompt;
    public bool ShowFullFormChangeSession => NeedsDutConfirm && _session.ConfirmedAt is not null;

    public string PromptTitle
    {
        get
        {
            if (IsIdleWarningPrompt)
            {
                return "Session idle soon — still testing this DUT?";
            }

            if (IsStalePrompt)
            {
                return PendingConfirmEveryRun
                    ? "Confirm Same DUT before the next Run"
                    : "Re-confirm this DUT before Run";
            }

            return "Session confirmation required before Run";
        }
    }

    public const string SessionFieldDutSerial = "DutSerial";
    public const string SessionFieldDutPart = "DutPart";
    public const string SessionFieldDutRevision = "DutRevision";
    public const string SessionFieldTechnician = "Technician";
    public const string SessionFieldCredential = "Credential";

    /// First required confirm field that is still empty, or null when Confirm Session can run.
    public string? NextIncompleteSessionField()
    {
        if (RequireSerial && string.IsNullOrWhiteSpace(NormalizeScan(DutSerialInput)))
        {
            return SessionFieldDutSerial;
        }

        if (RequirePartNumber && string.IsNullOrWhiteSpace(NormalizeScan(DutPartInput)))
        {
            return SessionFieldDutPart;
        }

        if (RequireRevision && string.IsNullOrWhiteSpace(NormalizeScan(DutRevisionInput)))
        {
            return SessionFieldDutRevision;
        }

        if (RequireOperator
            && string.IsNullOrWhiteSpace(NormalizeScan(OperatorInput))
            && string.IsNullOrWhiteSpace(_session.OperatorName))
        {
            return SessionFieldTechnician;
        }

        return null;
    }

    /// Marks the session stale once it has been idle past the configured window.
    public void ApplyIdleStaleCheck()
    {
        if (_isRunning())
        {
            // A live run is operator activity — otherwise a long suite trips idle the instant it ends.
            _session.TouchActivity();
            return;
        }
        var minutes = OperatorSessionIdle.ClampMinutes(_settings.OperatorSessionIdleMinutes);
        var warn = OperatorSessionIdle.ClampWarnPercent(_settings.OperatorSessionIdleWarnPercent);
        _session.EvaluateIdle(TimeSpan.FromMinutes(minutes), warn);
    }

    /// Bumps last-activity while the session is Active (navigation / meaningful page use).
    public void TouchActivity() => _session.TouchActivity();

    /// After a terminal run when station policy requires re-confirm.
    public void ApplyConfirmEveryRunPolicy(bool runReachedTerminal)
    {
        if (!runReachedTerminal || !_settings.RequireDutConfirmEveryRun)
        {
            return;
        }

        _session.MarkStale();
        PendingConfirmEveryRun = true;
        ShowSessionForm = true;
        RefreshSessionSummary();
    }

    public void RefreshRequirementFlags()
    {
        var req = CurrentRequirements;
        _session.ApplyProgramRequirements(req);
        ApplyRequirementFlags(req);
        RefreshSessionSummary();
    }

    private ProgramRequirements CurrentRequirements => _getSelectedProgram()?.Requirements ?? _session.Requirements;

    private void ApplyRequirementFlags(ProgramRequirements req)
    {
        RequireSerial = req.RequireSerial;
        DutSerialPlaceholder = RequireSerial ? "DUT serial *" : "DUT serial";
        RequirePartNumber = req.RequirePartNumber;
        RequireRevision = req.RequireRevision;
        RequireOperator = req.RequireOperator;
        TechnicianPlaceholder = RequireOperator ? "Technician *" : "Technician";
    }

    public void RefreshSessionSummary()
    {
        var req = CurrentRequirements;
        ApplyRequirementFlags(req);
        if (!_isRunning() && _session.State != OperatorSessionState.NeedsDut && !_requiresFullConfirmation)
        {
            var missingDutInfo = (req.RequireSerial && string.IsNullOrWhiteSpace(_session.DutSerial))
                || (req.RequirePartNumber && string.IsNullOrWhiteSpace(_session.DutPartNumber))
                || (req.RequireRevision && string.IsNullOrWhiteSpace(_session.DutRevision));
            var missingActiveIdentity = _session.State == OperatorSessionState.Active
                && ((req.RequireOperator && string.IsNullOrWhiteSpace(_session.OperatorName))
                    || !TryRequireCredential(req, out _));
            if (missingDutInfo || missingActiveIdentity)
            {
                // Filling a new requirement or presenting a badge does not itself
                // reconfirm a context whose earlier confirmation was insufficient.
                _requiresFullConfirmation = true;
                RetainConfirmedInputs();
            }
        }

        NeedsDutConfirm = _session.State == OperatorSessionState.NeedsDut || _requiresFullConfirmation;
        IsStalePrompt = !NeedsDutConfirm && _session.State == OperatorSessionState.Stale;
        IsIdleWarningPrompt = !_isRunning()
            && !NeedsDutConfirm
            && _session.State == OperatorSessionState.Active
            && _session.IsIdleWarning;
        SessionBlocked = !_isRunning() && (NeedsDutConfirm || !_session.CanRun || IsIdleWarningPrompt);
        ShowSessionForm = NeedsDutConfirm || IsStalePrompt || IsIdleWarningPrompt || SessionBlocked;
        ShowStaleTechnicianField = (IsStalePrompt || IsIdleWarningPrompt)
            && RequireOperator
            && string.IsNullOrWhiteSpace(_session.OperatorName);
        RefreshIdleCountdown();
        this.RaisePropertyChanged(nameof(ShowSameDutPrompt));
        this.RaisePropertyChanged(nameof(ShowFullFormChangeSession));
        this.RaisePropertyChanged(nameof(PromptTitle));

        var program = _session.ProgramDisplayName ?? "(none)";
        if (!SessionBlocked && _session.CanRun && !IsIdleWarningPrompt)
        {
            var tech = string.IsNullOrWhiteSpace(_session.OperatorName) ? "—" : _session.OperatorName;
            SessionSummary = $"DUT {_session.DutSerial} | Tech {tech} | {program}";
            return;
        }

        if (IsIdleWarningPrompt)
        {
            SessionSummary = $"Still testing {_session.DutSerial}? Session idle soon — Same DUT or Change Session.";
            return;
        }

        if (IsStalePrompt)
        {
            SessionSummary = PendingConfirmEveryRun
                ? $"DUT {_session.DutSerial} — confirm Same DUT before next Run | {program}"
                : $"DUT {_session.DutSerial} (re-confirm) | {program}";
            return;
        }

        SessionSummary = _requiresFullConfirmation
            ? $"Program or station policy changed — confirm session details | {program}"
            : RequireOperator
            ? $"Session blocked — confirm DUT + technician | {program}"
            : $"Session blocked — confirm DUT | {program}";
    }

    private void RetainConfirmedInputs()
    {
        if (string.IsNullOrWhiteSpace(DutSerialInput)) DutSerialInput = _session.DutSerial;
        if (string.IsNullOrWhiteSpace(DutPartInput)) DutPartInput = _session.DutPartNumber ?? string.Empty;
        if (string.IsNullOrWhiteSpace(DutRevisionInput)) DutRevisionInput = _session.DutRevision ?? string.Empty;
        if (string.IsNullOrWhiteSpace(OperatorInput)) OperatorInput = _session.OperatorName ?? string.Empty;
    }

    /// A changed execution reference is new session context, not a new DUT.
    public void RequireContextConfirmation()
    {
        if (_isRunning()) return;
        _requiresFullConfirmation = true;
        RetainConfirmedInputs();
        RefreshSessionSummary();
    }

    private void RefreshIdleCountdown()
    {
        if (_settings.RequireDutConfirmEveryRun && IsStalePrompt)
        {
            ShowIdleCountdown = false;
            IdleCountdownText = string.Empty;
            return;
        }

        if (_session.State != OperatorSessionState.Active || _session.LastActivityAt is null)
        {
            ShowIdleCountdown = false;
            IdleCountdownText = string.Empty;
            return;
        }

        var parts = new List<string>();
        if (_session.LastActivityAt is { } activity)
        {
            parts.Add($"Last activity {FormatRelative(activity)}");
        }

        if (_session.TimeUntilSoftWarn is { } warn && warn > TimeSpan.Zero && !_session.IsIdleWarning)
        {
            parts.Add($"soft-warn in {FormatDuration(warn)}");
        }

        if (_session.TimeUntilStale is { } stale && stale > TimeSpan.Zero)
        {
            parts.Add($"stale in {FormatDuration(stale)}");
        }

        IdleCountdownText = string.Join(" · ", parts);
        ShowIdleCountdown = parts.Count > 0;
    }

    private void UpdateIdleTimer()
    {
        var minutes = OperatorSessionIdle.ClampMinutes(_settings.OperatorSessionIdleMinutes);
        // Poll every 15–60s, or ≤10% of idle window.
        var intervalMs = Math.Clamp(minutes * 60_000 / 10, 15_000, 60_000);
        _idleTimer.Interval = intervalMs;
        if (_session.State == OperatorSessionState.Active)
        {
            _idleTimer.Start();
        }
        else
        {
            _idleTimer.Stop();
        }
    }

    /// Trims keyboard-wedge scanner suffixes without starting a run.
    public static string NormalizeScan(string value)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Trim().Trim('\0');

    private void ClearFieldErrors()
    {
        DutSerialError = string.Empty;
        DutPartError = string.Empty;
        DutRevisionError = string.Empty;
        OperatorError = string.Empty;
        this.RaisePropertyChanged(nameof(HasDutSerialError));
        this.RaisePropertyChanged(nameof(HasDutPartError));
        this.RaisePropertyChanged(nameof(HasDutRevisionError));
        this.RaisePropertyChanged(nameof(HasOperatorError));
    }

    private void ApplyFieldError(string error, string? field)
    {
        if (field == SessionFieldDutPart)
        {
            DutPartError = error;
        }
        else if (field == SessionFieldDutRevision)
        {
            DutRevisionError = error;
        }
        else if (field == SessionFieldDutSerial)
        {
            DutSerialError = error;
        }
        else if (field is SessionFieldTechnician or SessionFieldCredential)
        {
            OperatorError = error;
        }
        else
        {
            DutSerialError = error;
        }

        this.RaisePropertyChanged(nameof(HasDutSerialError));
        this.RaisePropertyChanged(nameof(HasDutPartError));
        this.RaisePropertyChanged(nameof(HasDutRevisionError));
        this.RaisePropertyChanged(nameof(HasOperatorError));
        RequestFocusSessionField?.Invoke(field ?? SessionFieldDutSerial);
    }

    private string FormatRelative(DateTimeOffset when)
    {
        var delta = _clock.UtcNow - when;
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{(int)delta.TotalMinutes}m ago";
        }

        if (delta < TimeSpan.FromDays(1))
        {
            return $"{(int)delta.TotalHours}h ago";
        }

        return when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalHours >= 2)
        {
            return $"{(int)span.TotalHours}h";
        }

        if (span.TotalMinutes >= 2)
        {
            return $"{(int)span.TotalMinutes}m";
        }

        return $"{Math.Max(1, (int)span.TotalSeconds)}s";
    }
}
