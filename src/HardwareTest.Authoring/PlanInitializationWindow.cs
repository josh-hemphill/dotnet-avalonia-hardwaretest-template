using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HardwareTest.Authoring;

/// A local six-stage form. Only the final Create action publishes source bytes.
public sealed class PlanInitializationWindow : Window
{
    private readonly AuthoringWorkspaceViewModel _vm;
    private readonly AuthoringWorkspace _workspace;
    private readonly TextBox _name = Input("Plan display name");
    private readonly TextBox _id = Input("Stable plan ID");
    private readonly TextBox _family = Input("Device family", "generic");
    private readonly TextBox _destination = Input("Draft destination");
    private readonly ComboBox _starting = Choice("Starting point", ["Empty plan", "Voltage task", "Demo voltage task — Mock DMM"]);
    private readonly ComboBox _hardware = Choice("Hardware choice", ["No hardware yet", "Create VISA DMM", "Create Mock DMM — demo"]);
    private readonly TextBox _slot = Input("Instrument slot", "DMM");
    private readonly TextBox _address = Input("Instrument address");
    private readonly TextBlock _readiness = Text("Hardware readiness");
    private readonly CheckBox _serial = Toggle("Require DUT serial", true);
    private readonly CheckBox _identity = Toggle("Check instrument identity", false);
    private readonly TextBox _fields = Input("Required operator fields");
    private readonly TextBox _confirmation = Input("Fixture confirmation");
    private readonly TextBox _fixture = Input("Fixture input field");
    private readonly CheckBox _shutdown = Toggle("Safe shutdown selected resources", true);
    private readonly TextBlock _coverage = Text("Shutdown coverage");
    private readonly CheckBox _measurement = Toggle("Include first measurement", false);
    private readonly TextBox _channel = Input("Output channel", "VDC");
    private readonly TextBox _unit = Input("Measurement unit", "V");
    private readonly TextBox _samples = Input("Sample count", "32");
    private readonly TextBox _interval = Input("Sampling interval ms", "5");
    private readonly CheckBox _criterion = Toggle("Mean greater than or equal criterion", false);
    private readonly TextBox _threshold = Input("Pass threshold", "1.2");
    private readonly TextBlock _review = Text("Initialization review");
    private readonly TextBlock _error = Text("Initialization error");
    private readonly TextBlock _heading = Text("Initialization stage");
    private readonly Button _back = Action("Back");
    private readonly Button _next = Action("Next");
    private readonly Button _create = Action("Create test plan");
    private readonly StackPanel[] _stages;
    private readonly List<InstrumentRef> _reusable;
    private int _stage;
    private bool _idEdited;

    public PlanInitializationWindow(AuthoringWorkspaceViewModel vm)
    {
        _vm = vm;
        _workspace = vm.Workspace ?? throw new AuthoringWorkspaceException("Open a workspace first.");
        Title = "New test plan"; Width = 650; Height = 680; MinWidth = 480; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _reusable = vm.HardwareDefinitions.Select(definition => new InstrumentRef(definition.Name, definition.TypeId, definition.Address)
        { Settings = definition.Settings }).Concat(vm.Programs.SelectMany(program => program.Instruments))
            .Where(resource => AuthoringInstrumentCatalog.TryGet(resource.TypeId, out _))
            .DistinctBy(resource => (resource.SlotName, resource.TypeId, resource.VisaAddress)).ToList();
        _hardware.ItemsSource = new[] { "No hardware yet", "Create VISA DMM", "Create Mock DMM — demo" }
            .Concat(_reusable.Select(resource => $"Reuse {resource.SlotName} — {AuthoringInstrumentCatalog.All.Single(adapter => adapter.TypeId == resource.TypeId).DisplayName} ({resource.VisaAddress})")).ToArray();
        _destination.IsReadOnly = false;
        _name.Text = vm.SuggestedPlanId; _id.Text = vm.SuggestedPlanId;
        _id.TextChanged += (_, _) => { _idEdited = true; ShowDestination(); };
        _name.TextChanged += (_, _) =>
        {
            if (_idEdited) return;
            var token = string.Concat((_name.Text ?? "").Trim().Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-'));
            _id.Text = token; _idEdited = false;
        };
        _starting.SelectionChanged += (_, _) =>
        {
            _measurement.IsChecked = _starting.SelectedIndex != 0;
            if (_starting.SelectedIndex == 2) { _hardware.SelectedIndex = 2; _address.Text = "MOCK::INSTR0"; }
        };
        _hardware.SelectionChanged += (_, _) => ShowHardware();
        _slot.TextChanged += (_, _) => ShowHardware();
        _identity.IsCheckedChanged += (_, _) => ShowHardware();
        _shutdown.IsCheckedChanged += (_, _) => ShowHardware();
        _stages =
        [
            Stage(Label("Display name", _name), Label("Editable stable plan ID", _id), Label("Device family", _family),
                new TextBlock { Text = "Workspace: " + _workspace.Root, TextWrapping = TextWrapping.Wrap }, Label("Draft filename", _destination)),
            Stage(_starting, new TextBlock { Text = "Empty plans contain no instruments. Voltage tasks can remain incomplete. Demo tasks explicitly select a Mock DMM.", TextWrapping = TextWrapping.Wrap }),
            Stage(_hardware, Label("Logical slot", _slot), Label("Address", _address), _readiness),
            Stage(_serial, _identity, Label("Required operator fields (comma separated)", _fields), Label("Fixture confirmation (optional)", _confirmation),
                Label("Fixture input field (optional)", _fixture), _shutdown, _coverage),
            Stage(_measurement, Label("Output channel", _channel), Label("Unit", _unit), Label("Samples", _samples), Label("Interval (ms)", _interval),
                _criterion, Label("Threshold", _threshold)),
            Stage(_review)
        ];
        _back.Click += (_, _) => { _stage--; ShowStage(); };
        _next.Click += (_, _) =>
        {
            try { Review(); _stage++; ShowStage(); }
            catch (Exception error) { _error.Text = AuthoringWorkspaceViewModel.PersistenceError(error); }
        };
        _create.Click += (_, _) =>
        {
            try
            {
                EnsureSession();
                vm.InitializePlan(Request());
                Close(true);
            }
            catch (Exception error) { _error.Text = AuthoringWorkspaceViewModel.PersistenceError(error); }
        };
        var cancel = Action("Cancel"); cancel.IsCancel = true; cancel.Click += (_, _) => Close(false);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(false); e.Handled = true; } };
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { cancel, _back, _next, _create }) { button.Margin = new Thickness(4); buttons.Children.Add(button); }
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(_heading, Dock.Top); root.Children.Add(_heading);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        DockPanel.SetDock(_error, Dock.Bottom); root.Children.Add(_error);
        _error.Foreground = Brushes.DarkRed; AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Assertive);
        var content = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12) };
        foreach (var stage in _stages) content.Children.Add(stage);
        root.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = root;
        ShowDestination(); ShowHardware(); ShowStage();
    }

    private void EnsureSession()
    {
        if (!ReferenceEquals(_workspace, _vm.Workspace)) throw new AuthoringWorkspaceException("The workspace changed; reopen New test plan.");
        if (!_vm.CanInitializePlan) throw new AuthoringWorkspaceException("Open a writable workspace and wait for the active operation.");
    }

    private PlanInitializationRequest Request()
    {
        var resources = SelectedResources();
        return new PlanInitializationRequest(_id.Text ?? "")
        {
            DisplayName = _name.Text,
            DeviceFamily = _family.Text ?? "",
            WorkspaceRoot = _workspace.Root,
            DestinationPath = _destination.Text,
            StartingPoint = (PlanStartingPoint)_starting.SelectedIndex,
            IncludeTemplateMeasurement = false,
            UseTemplateHardware = false,
            Instruments = resources,
            RequireSerial = _serial.IsChecked == true,
            RequiredOperatorFields = (_fields.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            IdentityInstrumentSlot = _identity.IsChecked == true ? _slot.Text : null,
            FixtureConfirmation = _confirmation.Text,
            FixtureInputField = _fixture.Text,
            IncludeSafeShutdown = _shutdown.IsChecked == true,
            Measurement = _measurement.IsChecked == true ? new(_criterion.IsChecked == true ? AuthoringRecipeIds.MeanGte : AuthoringRecipeIds.Acquire, resources.FirstOrDefault()?.SlotName ?? "")
            {
                ChannelKey = _channel.Text ?? "",
                Unit = _unit.Text ?? "",
                SampleCount = _samples.Text ?? "",
                IntervalMs = _interval.Text ?? "",
                ThresholdText = _criterion.IsChecked == true ? _threshold.Text ?? "" : null
            } : null
        };
    }

    private IReadOnlyList<InstrumentRef> SelectedResources()
    {
        if (_hardware.SelectedIndex >= 3)
        {
            var reused = _reusable[_hardware.SelectedIndex - 3];
            return [reused with { SlotName = _slot.Text ?? "" }];
        }
        if (_hardware.SelectedIndex == 0) return [];
        var type = AuthoringInstrumentCatalog.All.Single(adapter => adapter.DisplayName == (_hardware.SelectedIndex == 1 ? "VISA DMM" : "Mock DMM"));
        return [new InstrumentRef(_slot.Text ?? "", type.TypeId, _address.Text ?? "")];
    }

    private void ShowHardware()
    {
        var resources = SelectedResources();
        if (_hardware.SelectedIndex >= 3) { _address.Text = resources[0].VisaAddress; _address.IsReadOnly = true; }
        else _address.IsReadOnly = false;
        try
        {
            var review = _vm.ReviewPlanInitialization(Request());
            _readiness.Text = resources.Count == 0 ? "Choose hardware later; missing bindings remain draft issues."
                : string.Join("\n", resources.Select(resource =>
                {
                    var adapter = AuthoringInstrumentCatalog.All.Single(candidate => candidate.TypeId == resource.TypeId);
                    var declared = _workspace.Manifest.Dependencies.Any(dependency => string.Equals(dependency.Package, adapter.RequiredPackage, StringComparison.OrdinalIgnoreCase));
                    return $"{adapter.DisplayName} · {adapter.RequiredPackage} · {(declared ? "dependency declared" : "dependency missing — preserve as draft")}\nCompatible functions: {string.Join(", ", adapter.CompatibleFunctions)}";
                })) + "\n" + string.Join("\n", review.Issues.Where(issue => issue.Code.StartsWith("INSTRUMENT_", StringComparison.Ordinal)).Select(issue => issue.Message));
            _coverage.Text = "Shutdown coverage: " + (_shutdown.IsChecked == true ? string.Join(", ", review.Draft.Cleanup.InstrumentSlots) : "disabled");
        }
        catch (Exception error) { _readiness.Text = error.Message; }
    }

    private void ShowDestination()
    {
        try { _destination.Text = new AuthoringDocumentStore(_workspace.Root).GetDocumentPath(_id.Text ?? ""); }
        catch (ArgumentException) { _destination.Text = "Enter a safe stable plan ID."; }
    }

    private void Review()
    {
        EnsureSession();
        var result = _vm.ReviewPlanInitialization(Request());
        _review.Text = result.Review + "\n" + string.Join("\n", result.Issues.Select(issue => issue.Message)) + "\n" + result.NextAction;
        _error.Text = "";
    }

    private void ShowStage()
    {
        string[] titles = ["Name and destination", "Starting point", "Hardware", "Setup and cleanup", "First measurement and criterion", "Review and create"];
        _heading.Text = $"{_stage + 1} of 6 · {titles[_stage]}";
        for (var index = 0; index < _stages.Length; index++) _stages[index].IsVisible = index == _stage;
        _back.IsVisible = _stage > 0; _next.IsVisible = _stage < 5; _create.IsVisible = _stage == 5;
    }

    private static TextBox Input(string name, string value = "") => Named(new TextBox { Text = value }, name);
    private static ComboBox Choice(string name, string[] items) => Named(new ComboBox { ItemsSource = items, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch }, name);
    private static TextBlock Text(string name) => Named(new TextBlock { TextWrapping = TextWrapping.Wrap }, name);
    private static CheckBox Toggle(string name, bool value) => Named(new CheckBox { Content = name, IsChecked = value }, name);
    private static Button Action(string name) => Named(new Button { Content = name }, name);
    private static T Named<T>(T control, string name) where T : Control { AutomationProperties.SetName(control, name); return control; }
    private static StackPanel Stage(params Control[] controls) { var panel = new StackPanel { Spacing = 10 }; foreach (var control in controls) panel.Children.Add(control); return panel; }
    private static StackPanel Label(string text, Control input) => Stage(new TextBlock { Text = text }, input);
}
