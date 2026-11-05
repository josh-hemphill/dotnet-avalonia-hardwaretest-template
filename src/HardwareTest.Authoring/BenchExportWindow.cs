using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// A local compiled-file handoff; it does not install packages or control a running bench.
public sealed class BenchExportWindow : Window
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
    private readonly CancellationTokenSource _cancellation = new();
    private bool _busy;
    private int _closed;

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
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24), RowSpacing = 16 };
        layout.Children.Add(new TextBlock { Text = "Prepare bench files", FontSize = 22, FontWeight = FontWeight.SemiBold });
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = $"Selected program: {_planId}", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock { Text = "Prepare the saved compiled plan and operator settings together. Choose the bench Programs folder or a local transfer folder.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "Destination folder" });
        _destination = new TextBox { MinHeight = 40 };
        AutomationProperties.SetName(_destination, "Bench files destination folder");
        body.Children.Add(_destination);
        _browse = new Button { Content = "Choose folder…", MinHeight = 40 };
        AutomationProperties.SetName(_browse, "Choose bench files destination");
        _browse.Click += async (_, _) => await BrowseAsync();
        body.Children.Add(_browse);
        body.Children.Add(new TextBlock { Text = "A prepared folder for this program ID will be replaced. Unrelated or manually changed files are preserved and require another destination.", TextWrapping = TextWrapping.Wrap });
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(_status, "Bench file preparation status"); body.Children.Add(_status);
        _receipt = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        ScrollViewer.SetVerticalScrollBarVisibility(_receipt, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_receipt, "Prepared bench program receipt"); body.Children.Add(_receipt);
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var footer = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = new Button { Content = "Close", MinHeight = 40, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(close, "Close bench file preparation"); close.Click += (_, _) => Close();
        _prepare = new Button { Content = "Prepare / replace bench copy", MinHeight = 40, IsDefault = true };
        AutomationProperties.SetName(_prepare, "Prepare bench copy"); _prepare.Classes.Add("accent");
        _prepare.Click += async (_, _) => await PrepareAsync(); footer.Children.Add(close); footer.Children.Add(_prepare);
        Grid.SetRow(footer, 2); layout.Children.Add(footer); Content = layout;
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
        catch (Exception error) { if (Current()) _status.Text = error.Message; }
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
        _busy = true; _prepare.IsEnabled = _browse.IsEnabled = _destination.IsEnabled = false;
        _status.Text = "Checking saved files and preparing the bench copy…"; _receipt.IsVisible = false;
        try
        {
            var result = await Task.Run(() => BenchProgramExporter.Export(_workspace, _planId, destination,
                ContextStillSelected, _cancellation.Token));
            if (!Current()) return;
            _status.Text = $"Bench files prepared at {result.Directory}. Copy this complete program folder into the idle bench's Programs folder, then reload programs. Required packages must already be installed on that bench.";
            _receipt.Text = BenchProgramHandoff.Describe(result.Receipt); _receipt.IsVisible = true;
        }
        catch (OperationCanceledException) { if (Current()) _status.Text = "Bench file preparation cancelled."; }
        catch (Exception error) { if (Current()) _status.Text = error.Message; }
        finally
        {
            _busy = false;
            if (Current()) _prepare.IsEnabled = _browse.IsEnabled = _destination.IsEnabled = true;
            if (Volatile.Read(ref _closed) != 0) _cancellation.Dispose();
        }
    }
}
