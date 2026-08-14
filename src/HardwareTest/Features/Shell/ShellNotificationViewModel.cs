using System.Windows.Input;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Shell;

/// Severity for the reserved MainWindow notification strip (Phase 17).
public enum ShellNotificationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3,
}

/// Optional strip button (Export, Open folder, Dismiss, …).
public sealed class ShellNotificationAction
{
    public required string Label { get; init; }
    public required ICommand Command { get; init; }
    public bool IsVisible { get; init; } = true;
}

/// Cross-page notification host: reserved-height strip; higher severity wins until cleared.
public partial class ShellNotificationViewModel : ReactiveObject
{
    public const string SourceRun = "run";
    public const string SourceStorage = "storage";
    public const string SourceCrash = "crash";
    public const string SourceHistory = "history";
    public const string SourceClock = "clock";

    private string? _sourceKey;
    private Action? _onDismissed;

    public ShellNotificationViewModel()
    {
        DismissCommand = ReactiveCommand.Create(Dismiss);
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> DismissCommand { get; }

    [Reactive] private bool _hasContent;
    [Reactive] private ShellNotificationSeverity _severity = ShellNotificationSeverity.Info;
    [Reactive] private string _message = string.Empty;
    [Reactive] private bool _isDismissible = true;
    [Reactive] private string _primaryLabel = string.Empty;
    [Reactive] private string _secondaryLabel = string.Empty;
    [Reactive] private bool _hasPrimaryAction;
    [Reactive] private bool _hasSecondaryAction;

    public ICommand? PrimaryCommand { get; private set; }
    public ICommand? SecondaryCommand { get; private set; }

    /// Idle caption when no notification is active (keeps strip height stable).
    public string IdleHint { get; } = "Ready";

    /// Publishes a notification. Lower severity does not replace a higher one from another source.
    public void Publish(
        ShellNotificationSeverity severity,
        string message,
        bool dismissible = true,
        string sourceKey = SourceRun,
        ShellNotificationAction? primary = null,
        ShellNotificationAction? secondary = null,
        Action? onDismissed = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (HasContent
            && !string.Equals(_sourceKey, sourceKey, StringComparison.Ordinal)
            && Rank(severity) < Rank(Severity))
        {
            return;
        }

        _sourceKey = sourceKey;
        _onDismissed = onDismissed;
        Severity = severity;
        Message = message.Trim();
        IsDismissible = dismissible;
        ApplyActions(primary, secondary);
        HasContent = true;
        this.RaisePropertyChanged(nameof(PrimaryCommand));
        this.RaisePropertyChanged(nameof(SecondaryCommand));
    }

    /// Clears the current notification when it matches <paramref name="sourceKey"/> (or any when null).
    public void Clear(string? sourceKey = null)
    {
        if (!HasContent)
        {
            return;
        }

        if (sourceKey is not null
            && !string.Equals(_sourceKey, sourceKey, StringComparison.Ordinal))
        {
            return;
        }

        ResetContent();
    }

    public void Dismiss()
    {
        if (!IsDismissible)
        {
            return;
        }

        var callback = _onDismissed;
        ResetContent();
        callback?.Invoke();
    }

    private void ResetContent()
    {
        _sourceKey = null;
        _onDismissed = null;
        HasContent = false;
        Message = string.Empty;
        Severity = ShellNotificationSeverity.Info;
        IsDismissible = true;
        ApplyActions(null, null);
        this.RaisePropertyChanged(nameof(PrimaryCommand));
        this.RaisePropertyChanged(nameof(SecondaryCommand));
    }

    private void ApplyActions(ShellNotificationAction? primary, ShellNotificationAction? secondary)
    {
        PrimaryCommand = primary is { IsVisible: true } ? primary.Command : null;
        PrimaryLabel = primary is { IsVisible: true } ? primary.Label : string.Empty;
        HasPrimaryAction = PrimaryCommand is not null;
        SecondaryCommand = secondary is { IsVisible: true } ? secondary.Command : null;
        SecondaryLabel = secondary is { IsVisible: true } ? secondary.Label : string.Empty;
        HasSecondaryAction = SecondaryCommand is not null;
    }

    private static int Rank(ShellNotificationSeverity severity) => (int)severity;
}
