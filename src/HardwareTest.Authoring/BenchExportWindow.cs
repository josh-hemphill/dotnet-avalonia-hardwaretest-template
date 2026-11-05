using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// A local compiled-file handoff; it does not install packages or control a running bench.
public sealed partial class BenchExportWindow : Window
{
    private readonly AuthoringWorkspaceViewModel _viewModel;
    private readonly AuthoringWorkspace _workspace;
    private readonly string _planId;
    private readonly Guid _session;
    private readonly AuthoringDocumentSession? _document;
    private readonly long? _revision;
    private readonly Func<bool> _ownerIsCurrent;
    private readonly TextBox _destination;
    private readonly TextBlock _status;
    private readonly TextBox _receipt;
    private readonly Button _prepare;
    private readonly Button _browse;
    private readonly Button _cancel;
    private readonly Button _close;
    private readonly CancellationTokenSource _cancellation = new();
    private CancellationTokenSource? _preparation;
    private int _preparationId;
    private bool _busy;
    private int _closed;
    internal Func<OfflineBenchExportOptions, string, Func<bool>, CancellationToken, OfflineBenchExportResult>? OfflineExporter { get; set; }

    public BenchExportWindow(AuthoringWorkspaceViewModel viewModel, Func<bool> ownerIsCurrent)
    {
        _viewModel = viewModel;
        _workspace = viewModel.Workspace ?? throw new InvalidOperationException("Open a workspace first.");
        _planId = viewModel.SelectedProgram?.PlanId ?? throw new InvalidOperationException("Select a program first.");
        _session = viewModel.WorkspaceSessionId;
        _document = viewModel.SelectedDocument;
        _revision = _document?.Revision;
        _ownerIsCurrent = ownerIsCurrent;
        Title = "Prepare bench files"; Width = 680; Height = 620; MinWidth = 420; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(24), RowSpacing = 12 };
        layout.Children.Add(new TextBlock { Text = "Prepare bench files", FontSize = 22, FontWeight = FontWeight.SemiBold });
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = $"Selected program: {_planId}", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock { Text = "Prepare the saved compiled plan and operator settings together. Choose the bench Programs folder or a local transfer folder.", TextWrapping = TextWrapping.Wrap });
        AddOfflineOptions(body);
        body.Children.Add(new TextBlock { Text = "Destination folder" });
        _destination = new TextBox { MinHeight = 40 };
        _destination.TextChanged += (_, _) => InvalidatePreparationFeedback();
        AutomationProperties.SetName(_destination, "Bench files destination folder");
        body.Children.Add(_destination);
        _browse = new Button { Content = "Choose folder…", MinHeight = 40 };
        AutomationProperties.SetName(_browse, "Choose bench files destination");
        _browse.Click += async (_, _) => await BrowseAsync();
        body.Children.Add(_browse);
        body.Children.Add(new TextBlock { Text = "A prepared folder for this program ID will be replaced. Unrelated or manually changed files are preserved and require another destination.", TextWrapping = TextWrapping.Wrap });
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(_status, "Bench file preparation status");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        Grid.SetRow(_status, 2); layout.Children.Add(_status);
        _receipt = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        ScrollViewer.SetVerticalScrollBarVisibility(_receipt, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_receipt, "Prepared bench program receipt"); body.Children.Add(_receipt);
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var footer = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _close = new Button { Content = "Close", MinHeight = 40, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(_close, "Close bench file preparation"); _close.Click += (_, _) => Close();
        _cancel = new Button { Content = "Cancel preparation", MinHeight = 40, IsVisible = false, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(_cancel, "Cancel bench file preparation");
        _cancel.Click += (_, _) => { _preparation?.Cancel(); _cancel.IsEnabled = false; _status.Text = "Cancelling preparation…"; };
        _prepare = new Button { Content = "Prepare / replace bench copy", MinHeight = 40, IsDefault = true };
        AutomationProperties.SetName(_prepare, "Prepare bench copy"); _prepare.Classes.Add("accent");
        _prepare.Click += async (_, _) => await PrepareAsync(); footer.Children.Add(_cancel); footer.Children.Add(_close); footer.Children.Add(_prepare);
        Grid.SetRow(footer, 3); layout.Children.Add(footer); Content = layout;
        UpdateOfflineMode();
        Opened += (_, _) => _destination.Focus();
        Closed += (_, _) =>
        {
            Interlocked.Exchange(ref _closed, 1); _cancellation.Cancel();
            if (!_busy) _cancellation.Dispose();
        };
    }

    private bool ContextStillSelected() => Volatile.Read(ref _closed) == 0
        && _session == _viewModel.WorkspaceSessionId
        && _document?.Revision == _revision
        && ReferenceEquals(_workspace, _viewModel.Workspace) && _viewModel.SelectedProgram?.PlanId == _planId;

    private bool Current() => ContextStillSelected() && _ownerIsCurrent();

    private async Task BrowseAsync()
    {
        if (_busy || !Current()) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Bench Programs or transfer folder", AllowMultiple = false });
            if (Current() && folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _destination.Text = path;
        }
        catch (Exception error) { if (Current()) ShowPreparationError(error.Message); }
        finally { if (Current()) _destination.Focus(); }
    }

    private async Task PrepareAsync()
    {
        if (_busy) return;
        if (!Current())
        {
            if (IsVisible)
            {
                _status.Text = "The selected revision changed. Close this dialog and reopen Prepare bench files for the intended program.";
                _receipt.IsVisible = false;
                _status.BringIntoView();
            }
            return;
        }
        if (_viewModel.HasUnsavedChanges || _viewModel.OperationBusy || _viewModel.OperationCleanupPending)
        { _status.Text = "Save and compile the selected revision, then finish the active operation before preparing bench files."; return; }
        if (string.IsNullOrWhiteSpace(_destination.Text))
        { _status.Text = "Choose a destination folder."; _destination.Focus(); return; }
        var destination = _destination.Text;
        var offline = _includePackages.IsChecked == true;
        var options = CaptureOfflineOptions();
        var preparationId = ++_preparationId;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
        _preparation = operation;
        _busy = true;
        string? publishedDirectory = null;
        SetPreparationBusy(true);
        _status.Text = "Checking saved files and preparing the bench copy…"; _receipt.IsVisible = false;
        try
        {
            if (offline)
            {
                options = options with
                {
                    Progress = message => Dispatcher.UIThread.Post(() =>
                {
                    if (_busy && preparationId == _preparationId && Current()) _status.Text = message;
                })
                };
                var result = await Task.Run(() => OfflineExporter is { } export
                    ? export(options, destination, ContextStillSelected, operation.Token)
                    : OfflineBenchBundleExporter.Export(_workspace, _planId, destination, options, ContextStillSelected, operation.Token));
                publishedDirectory = result.Directory;
                if (!Current()) return;
                _status.Text = $"Offline bundle prepared at {result.Directory}. Copy the complete folder to the bench. Check it in Settings, then use its helper to prepare a separate installation with fresh bench data.";
                AutomationProperties.SetName(_receipt, "Prepared offline bundle details");
                _receipt.Text = result.Summary + Environment.NewLine + Environment.NewLine
                    + OfflineBenchBundle.Describe(result.Manifest);
            }
            else
            {
                var result = await Task.Run(() => BenchProgramExporter.Export(_workspace, _planId, destination,
                    ContextStillSelected, operation.Token));
                publishedDirectory = result.Directory;
                if (!Current()) return;
                _status.Text = $"Bench files prepared at {result.Directory}. Copy the complete folder into Programs, then reload. Required packages must already be installed.";
                AutomationProperties.SetName(_receipt, "Prepared bench program receipt");
                _receipt.Text = BenchProgramHandoff.Describe(result.Receipt);
            }
            _receipt.IsVisible = true;
        }
        catch (OperationCanceledException) { if (Current()) _status.Text = "Bench file preparation cancelled."; }
        catch (Exception error) { if (Current()) ShowPreparationError(error.Message); }
        finally
        {
            _busy = false;
            _preparation = null;
            if (Volatile.Read(ref _closed) == 0)
            {
                SetPreparationBusy(false);
                if (!Current())
                {
                    _receipt.IsVisible = false;
                    _status.Text = publishedDirectory is null
                        ? "The selected revision changed. Close this dialog and reopen Prepare bench files for the intended program."
                        : $"Files were prepared for the previous revision at {publishedDirectory}. Close this dialog and reopen Prepare bench files for the intended program.";
                }
            }
            if (Volatile.Read(ref _closed) != 0) _cancellation.Dispose();
        }
    }
}
