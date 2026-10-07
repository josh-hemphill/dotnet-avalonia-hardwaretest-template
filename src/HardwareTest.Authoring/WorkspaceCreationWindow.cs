using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace HardwareTest.Authoring;

/// Local form with an explicit file/package review before any workspace is written.
public sealed class WorkspaceCreationWindow : Window
{
    private readonly TextBox _destination = Input("Workspace destination");
    private readonly TextBox _name = Input("Workspace display name");
    private readonly TextBox _package = Input("Package name");
    private readonly TextBox _version = Input("Package version", "0.1.0");
    private readonly TextBox _platforms = Input("Package platforms", "Windows,Linux,MacOS");
    private readonly TextBox _plan = Input("Initial plan ID", "voltage");
    private readonly TextBox _family = Input("Workspace device family", "generic");
    private readonly CheckBox _serial = Named(new CheckBox { Content = "Require device serial", IsChecked = true }, "Require device serial");
    private readonly CheckBox _tui = Named(new CheckBox { Content = "Include terminal app" }, "Include terminal app");
    private readonly CheckBox _visa = Named(new CheckBox { Content = "Include Instrument Components package for physical hardware" }, "Include Instrument Components package");
    private readonly CheckBox _continue = Named(new CheckBox { Content = "Continue to New test plan after creation" }, "Continue to New test plan");
    private readonly ComboBox _template = Named(new ComboBox { ItemsSource = AuthoringWorkspaceTemplates.All.Select(item => item.Name).ToArray(), SelectedIndex = 0 }, "Workspace template");
    private readonly TextBlock _review = Named(new TextBlock { TextWrapping = TextWrapping.Wrap }, "Workspace creation review");
    private readonly TextBlock _error = Named(new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Red }, "Workspace creation error");
    private readonly Button _create = Named(new Button { Content = "Create workspace", IsEnabled = false }, "Create workspace");
    private WorkspaceCreationRequest? _reviewed;
    private readonly CancellationTokenSource _lifetime = new();

    public Guid? CreatedWorkspaceSession { get; private set; }
    public bool ContinueToPlan => _continue.IsChecked == true;

    public WorkspaceCreationWindow(MainWindow owner)
    {
        Title = "Create workspace from template"; Width = 680; Height = 680; MinWidth = 400; MinHeight = 460;
        FontSize = owner.FontSize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Closing += (_, _) => _lifetime.Cancel();
        Closed += (_, _) => _lifetime.Dispose();
        _template.SelectionChanged += (_, _) => _visa.IsVisible = _template.SelectedIndex != (int)WorkspaceTemplateKind.ProductVoltage;
        var browse = Named(new Button { Content = "Choose destination folder…" }, "Choose workspace destination");
        browse.Click += async (_, _) =>
        {
            var vm = owner.DataContext as AuthoringWorkspaceViewModel;
            var session = vm?.WorkspaceSessionId;
            var lifetime = _lifetime.Token;
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Workspace destination", AllowMultiple = false });
            if (lifetime.IsCancellationRequested || !IsVisible || !owner.IsVisible || !ReferenceEquals(owner.DataContext, vm) || session != vm?.WorkspaceSessionId) return;
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _destination.Text = path;
        };
        var review = Named(new Button { Content = "Review files and packages" }, "Review workspace creation");
        review.Click += (_, _) =>
        {
            try
            {
                _reviewed = null; _create.IsEnabled = false;
                var request = Request();
                var preview = new AuthoringWorkspaceInitializer().Preview(request);
                _review.Text = $"{preview.Template.Name} · Classification: {preview.Template.Classification}\nTask: {preview.Template.SupportedTask}\nDestination: {preview.Destination}\n"
                    + $"Package: {preview.Manifest.Package.Name} {preview.Manifest.Package.Version} ({preview.Manifest.Package.Os})\n"
                    + "Required packages:\n" + string.Join("\n", preview.Manifest.Dependencies.Select(package => package.Package + " " + package.Version))
                    + "\nFiles (created exclusively):\n" + string.Join("\n", preview.Files) + "\nDirectories: plans/, authoring-drafts/\n"
                    + (preview.Plan?.Review ?? "No initial program. Use New test plan after creation.");
                _reviewed = request; _create.IsEnabled = true; _error.Text = "";
            }
            catch (Exception error) { _error.Text = error.Message; }
        };
        _create.Click += async (_, _) =>
        {
            if (!IsVisible || !owner.IsVisible || _lifetime.IsCancellationRequested) return;
            if (_reviewed is null || _reviewed != Request()) { _error.Text = "Review the updated files and settings before creating."; _create.IsEnabled = false; return; }
            var cancellationToken = _lifetime.Token;
            var vm = owner.DataContext as AuthoringWorkspaceViewModel;
            var session = vm?.WorkspaceSessionId;
            _create.IsEnabled = false;
            var created = await owner.CreateWorkspaceAsync(_reviewed, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !IsVisible || !owner.IsVisible || !ReferenceEquals(owner.DataContext, vm)
                || (!created && session != vm?.WorkspaceSessionId)) return;
            if (created)
            {
                CreatedWorkspaceSession = (owner.DataContext as AuthoringWorkspaceViewModel)?.WorkspaceSessionId;
                Close(true);
            }
            else { _error.Text = owner.WorkspaceCreationError; _create.IsEnabled = true; }
        };
        var cancel = Named(new Button { Content = "Cancel", IsCancel = true }, "Cancel workspace creation");
        cancel.Click += (_, _) => Close(false);
        _error.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AuthoringError"));
        _create.Classes.Add("authoringAction"); _create.Classes.Add("primaryAction");
        review.Classes.Add("authoringAction"); browse.Classes.Add("authoringAction"); cancel.Classes.Add("authoringAction");
        _visa.Content = new TextBlock { Text = "Include Instrument Components package for physical hardware", TextWrapping = TextWrapping.Wrap };
        _continue.Content = new TextBlock { Text = "Continue to New test plan after creation", TextWrapping = TextWrapping.Wrap };
        var form = new StackPanel { Spacing = 16 };
        form.Children.Add(AuthoringFormLayout.Section("Workspace location and identity",
            Label("Destination (new folder, or a folder without generated paths)", _destination), browse, Label("Workspace display name", _name)));
        form.Children.Add(AuthoringFormLayout.Section("Deployment package",
            Label("Package name", _package), Label("Package version", _version), Label("Package platforms (comma separated)", _platforms)));
        form.Children.Add(AuthoringFormLayout.Section("Starting task and hardware",
            Label("Template", _template), Label("Initial plan ID (task templates)", _plan), Label("Device family (task templates)", _family), _serial, _visa));
        form.Children.Add(AuthoringFormLayout.Section("Optional outputs and next step", _tui, _continue));
        form.Children.Add(AuthoringFormLayout.Section("Review generated files and requirements", review, _review));
        var decisions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { cancel, _create }) { button.Margin = new Thickness(4); decisions.Children.Add(button); }
        var footer = new StackPanel { Spacing = 8, Children = { new ScrollViewer { Content = _error, MaxHeight = 80 }, decisions } };
        Content = AuthoringFormLayout.Frame("Create workspace", "Choose a destination and template, then review exactly what will be created.", form, footer);

    }

    private WorkspaceCreationRequest Request() => new(_destination.Text ?? "", _name.Text ?? "", _package.Text ?? "")
    {
        Template = (WorkspaceTemplateKind)_template.SelectedIndex,
        PackageVersion = _version.Text ?? "",
        PackageOs = _platforms.Text ?? "",
        PlanId = _plan.Text ?? "",
        DeviceFamily = _family.Text ?? "",
        RequireSerial = _serial.IsChecked == true,
        IncludeTui = _tui.IsChecked == true,
        IncludeLibraryPackage = _visa.IsChecked == true
    };
    private static TextBox Input(string name, string value = "") => Named(new TextBox { Text = value }, name);
    private static T Named<T>(T control, string name) where T : Control { AutomationProperties.SetName(control, name); return control; }
    private static StackPanel Label(string label, Control input) => new() { Spacing = 4, Children = { new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, input } };
}
