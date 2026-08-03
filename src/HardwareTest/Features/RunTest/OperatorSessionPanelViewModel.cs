using System;
using System.Globalization;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using HardwareTest.UiThreading;

namespace HardwareTest.Features.RunTest;

/// DUT / technician confirmation panel: confirm, same-DUT, change session and stale prompts.
public partial class OperatorSessionPanelViewModel : ReactiveObject
{
    private readonly OperatorSession _session;
    private readonly AppSettings _settings;
    private readonly Action<string> _setStatus;
    private readonly Func<ProgramItemViewModel?> _getSelectedProgram;
    private readonly Action _onSessionCleared;
    private readonly System.Timers.Timer _idleTimer;

    /// Test seam: routes idle-timer UI work synchronously instead of through the Avalonia dispatcher.
    public Action<Action>? UiScheduler { get; set; }

    public OperatorSessionPanelViewModel(
        OperatorSession session,
        AppSettings settings,
        Action<string> setStatus,
        Func<ProgramItemViewModel?>? getSelectedProgram = null,
        Action? onSessionCleared = null)
    {
        _session = session;
        _settings = settings;
        _setStatus = setStatus;
        _getSelectedProgram = getSelectedProgram ?? (() => null);
        _onSessionCleared = onSessionCleared ?? (() => { });

        ConfirmSessionCommand = ReactiveCommand.Create(ConfirmSession);
        ConfirmSameDutCommand = ReactiveCommand.Create(ConfirmSameDut);
        ChangeSessionCommand = ReactiveCommand.Create(ChangeSession);

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

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ConfirmSessionCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ConfirmSameDutCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ChangeSessionCommand { get; }

    [Reactive] private string _dutSerialInput = string.Empty;
    [Reactive] private string _dutPartInput = string.Empty;
    [Reactive] private string _dutRevisionInput = string.Empty;
    [Reactive] private string _operatorInput = string.Empty;
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
    [Reactive] private string _operatorError = string.Empty;

    public bool HasDutSerialError => !string.IsNullOrWhiteSpace(DutSerialError);
    public bool HasOperatorError => !string.IsNullOrWhiteSpace(OperatorError);

    /// Marks the session stale once it has been idle past the configured window.
    public void ApplyIdleStaleCheck()
    {
        var minutes = OperatorSessionIdle.ClampMinutes(_settings.OperatorSessionIdleMinutes);
        if (minutes <= 0 && _settings.OperatorSessionIdleHours > 0)
        {
            minutes = OperatorSessionIdle.HoursToMinutes(_settings.OperatorSessionIdleHours);
        }

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
        var req = _getSelectedProgram()?.Requirements ?? ProgramRequirements.Sample;
        RequirePartNumber = req.RequirePartNumber;
        RequireRevision = req.RequireRevision;
        RequireOperator = req.RequireOperator;
        TechnicianPlaceholder = RequireOperator ? "Technician *" : "Technician";
    }

    public void RefreshSessionSummary()
    {
        NeedsDutConfirm = _session.State == OperatorSessionState.NeedsDut;
        IsStalePrompt = _session.State == OperatorSessionState.Stale;
        IsIdleWarningPrompt = _session.State == OperatorSessionState.Active && _session.IsIdleWarning;
        SessionBlocked = !_session.CanRun || IsIdleWarningPrompt;
        ShowSessionForm = NeedsDutConfirm || IsStalePrompt || IsIdleWarningPrompt || SessionBlocked;
        ShowStaleTechnicianField = (IsStalePrompt || IsIdleWarningPrompt)
            && RequireOperator
            && string.IsNullOrWhiteSpace(_session.OperatorName);
        RefreshIdleCountdown();

        var program = _session.ProgramDisplayName ?? "(none)";
        if (_session.CanRun && !IsIdleWarningPrompt)
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

        if (_session.State == OperatorSessionState.Stale)
        {
            SessionSummary = PendingConfirmEveryRun
                ? $"DUT {_session.DutSerial} — confirm Same DUT before next Run | {program}"
                : $"DUT {_session.DutSerial} (re-confirm) | {program}";
            return;
        }

        SessionSummary = RequireOperator
            ? $"Session blocked — confirm DUT + technician | {program}"
            : $"Session blocked — confirm DUT | {program}";
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
        var minutes = OperatorSessionIdle.ClampMinutes(
            _settings.OperatorSessionIdleMinutes > 0
                ? _settings.OperatorSessionIdleMinutes
                : OperatorSessionIdle.HoursToMinutes(_settings.OperatorSessionIdleHours));
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

    private void ConfirmSession()
    {
        var program = _getSelectedProgram();
        var req = program?.Requirements ?? ProgramRequirements.Sample;
        var family = program?.DutFamily ?? "generic";
        RefreshRequirementFlags();
        ClearFieldErrors();
        if (!_session.TryConfirm(req, DutSerialInput, DutPartInput, DutRevisionInput, OperatorInput, family, out var error))
        {
            ApplyFieldError(error);
            _setStatus(error);
            SessionBlocked = true;
            return;
        }

        PendingConfirmEveryRun = false;
        ShowSessionForm = false;
        RefreshSessionSummary();
        UpdateIdleTimer();
        _setStatus($"Session confirmed: {_session.DutSerial} / {_session.OperatorName ?? "—"}");
    }

    private void ConfirmSameDut()
    {
        RefreshRequirementFlags();
        ClearFieldErrors();
        if (RequireOperator
            && string.IsNullOrWhiteSpace(OperatorInput)
            && string.IsNullOrWhiteSpace(_session.OperatorName))
        {
            OperatorError = "Technician name is required.";
            this.RaisePropertyChanged(nameof(HasOperatorError));
            _setStatus(OperatorError);
            SessionBlocked = true;
            ShowSessionForm = true;
            ShowStaleTechnicianField = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(OperatorInput))
        {
            _session.OperatorName = OperatorInput.Trim();
        }

        _session.ConfirmSameDut();
        PendingConfirmEveryRun = false;
        DutSerialInput = _session.DutSerial;
        DutPartInput = _session.DutPartNumber ?? string.Empty;
        DutRevisionInput = _session.DutRevision ?? string.Empty;
        OperatorInput = _session.OperatorName ?? string.Empty;
        ShowSessionForm = !_session.CanRun;
        RefreshSessionSummary();
        UpdateIdleTimer();
        _setStatus(_session.CanRun ? $"Still testing {_session.DutSerial}." : "Confirm DUT, then Run.");
    }

    private void ChangeSession()
    {
        _session.ChangeSession();
        PendingConfirmEveryRun = false;
        ClearFieldErrors();
        _onSessionCleared();
        DutSerialInput = string.Empty;
        DutPartInput = string.Empty;
        DutRevisionInput = string.Empty;
        OperatorInput = string.Empty;
        ShowSessionForm = true;
        RefreshSessionSummary();
        UpdateIdleTimer();
        _setStatus("Confirm DUT, then Run.");
    }

    private void ClearFieldErrors()
    {
        DutSerialError = string.Empty;
        OperatorError = string.Empty;
        this.RaisePropertyChanged(nameof(HasDutSerialError));
        this.RaisePropertyChanged(nameof(HasOperatorError));
    }

    private void ApplyFieldError(string error)
    {
        if (error.Contains("serial", StringComparison.OrdinalIgnoreCase))
        {
            DutSerialError = error;
        }
        else if (error.Contains("Operator", StringComparison.OrdinalIgnoreCase)
                 || error.Contains("Technician", StringComparison.OrdinalIgnoreCase))
        {
            OperatorError = error;
        }
        else if (error.Contains("part", StringComparison.OrdinalIgnoreCase)
                 || error.Contains("revision", StringComparison.OrdinalIgnoreCase))
        {
            // Surface under serial as the primary confirm field when part/rev fail.
            DutSerialError = error;
        }
        else
        {
            DutSerialError = error;
        }

        this.RaisePropertyChanged(nameof(HasDutSerialError));
        this.RaisePropertyChanged(nameof(HasOperatorError));
    }

    private static string FormatRelative(DateTimeOffset when)
    {
        var delta = DateTimeOffset.UtcNow - when;
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
