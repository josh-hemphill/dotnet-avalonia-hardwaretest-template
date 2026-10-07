using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HardwareTest.Authoring;

/// A local six-stage form. Only the final Create action publishes source bytes.
public sealed partial class PlanInitializationWindow : Window
{
    private readonly AuthoringWorkspaceViewModel _vm;
    private readonly Func<bool>? _ownerIsCurrent;
    private readonly Guid _workspaceSession;
    private readonly AuthoringWorkspace _workspace;
    private readonly TextBox _name = Input("Plan display name");
    private readonly TextBox _id = Input("Stable plan ID");
    private readonly TextBox _family = Input("Device family", "generic");
    private readonly TextBox _destination = Input("Draft destination");
    private readonly ComboBox _starting = Choice("Starting point", ["Empty plan", "Voltage task", "Demo voltage task — Mock DMM"]);
    private readonly ComboBox _hardware = Choice("Hardware choice", []);
    private readonly TextBox _timeout = Input("Instrument I/O timeout ms", "5000");
    private readonly Button _environment = Action("Open Environment — retain inputs");
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
    private readonly List<string> _resourceOrigins = [];
    private int _stage;
    private bool _idEdited;
    private bool _automaticDemoAddress;

    public bool SkipGuidanceRequested { get; private set; }
    public bool EnvironmentRequested { get; private set; }

    public PlanInitializationWindow(AuthoringWorkspaceViewModel vm, bool guided = false, GuidedFormState? retained = null, Func<bool>? ownerIsCurrent = null)
    {
        _guided = guided;
        _vm = vm;
        _ownerIsCurrent = ownerIsCurrent;
        _workspaceSession = vm.WorkspaceSessionId;
        _workspace = vm.Workspace ?? throw new AuthoringWorkspaceException("Open a workspace first.");
        Title = "New test plan"; Width = 650; Height = 680; MinWidth = 480; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _ = vm.InstrumentTypeChoices; // Discover only validated devices in this selected home.
        var candidates = vm.HardwareDefinitions.Select(definition => (Resource: new InstrumentRef(definition.Name, definition.TypeId, definition.Address)
        { Settings = new Dictionary<string, string>(definition.Settings) }, Origin: "Workspace definition " + definition.Name))
            .Concat(vm.Programs.SelectMany(program => program.Instruments.Select(resource => (Resource: CopyResource(resource), Origin: "Program " + program.PlanId))))
            .Where(candidate => AuthoringInstrumentCatalog.TryGet(candidate.Resource.TypeId, out _)).ToList();
        _reusable = candidates.Select(candidate => candidate.Resource).ToList();
        _resourceOrigins.AddRange(candidates.Select(candidate => candidate.Origin));
        ShowHardwareChoices();
        _destination.IsReadOnly = false;
        _name.Text = vm.SuggestedPlanId; _id.Text = vm.SuggestedPlanId;
        _id.TextChanged += (_, _) => { _idEdited = true; ShowDestination(); };
        _name.TextChanged += (_, _) =>
        {
            if (_idEdited) return;
            var token = string.Concat((_name.Text ?? "").Trim().Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-'));
            _id.Text = token; _idEdited = false;
        };
        _address.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) _automaticDemoAddress = false; };
        _starting.SelectionChanged += (_, _) =>
        {
            _measurement.IsChecked = _starting.SelectedIndex != 0;
            if (_starting.SelectedIndex == 2) { SelectHardwareType(AuthoringInstrumentCatalog.All.Single(a => a.DisplayName == "Mock DMM").TypeId); _address.Text = "MOCK::INSTR0"; _automaticDemoAddress = true; }
        };
        _hardware.SelectionChanged += (_, _) => ShowHardware();
        _environment.Click += (_, _) => { try { EnsureSession(); EnvironmentRequested = true; Close(false); } catch (Exception error) { _error.Text = error.Message; } };
        _slot.TextChanged += (_, _) => ShowHardware();
        _identity.IsCheckedChanged += (_, _) => ShowHardware();
        _shutdown.IsCheckedChanged += (_, _) => ShowHardware();
        _stages =
        [
            Stage(Label("Display name", _name), Label("Editable stable plan ID", _id), Label("Device family", _family),
                new TextBlock { Text = "Workspace: " + _workspace.Root, TextWrapping = TextWrapping.Wrap }, Label("Draft filename", _destination)),
            Stage(_starting, new TextBlock { Text = "Empty plans contain no instruments. Voltage tasks can remain incomplete. Demo tasks explicitly select a Mock DMM.", TextWrapping = TextWrapping.Wrap }),
            Stage(_hardware, Label("Logical slot", _slot), Label("Address", _address), Label("I/O timeout (ms)", _timeout), _readiness, _environment),
            Stage(new TextBlock { Text = "Before measurements · DUT/operator requirements and instrument checks", TextWrapping = TextWrapping.Wrap }, _serial, _identity, Label("Required operator fields (comma separated)", _fields), Label("Fixture confirmation (optional)", _confirmation),
                Label("Fixture input field (optional)", _fixture), new TextBlock { Text = "After measurements · output off, then reset each selected instrument", TextWrapping = TextWrapping.Wrap }, _shutdown, _coverage),
            Stage(_measurement, Label("Output channel", _channel), Label("Unit", _unit), Label("Samples", _samples), Label("Interval (ms)", _interval),
                _criterion, Label("Threshold", _threshold)),
            Stage(_review)
        ];
        if (guided) ConfigureGuidedStages();
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
        if (guided)
        {
            var leave = Action("Leave guidance");
            leave.Click += (_, _) => Close(false);
            var skip = Action("Skip optional guidance");
            skip.IsEnabled = vm.PreferencesEditable;
            skip.Click += (_, _) =>
            {
                try { EnsureSession(); SkipGuidanceRequested = true; vm.SkipGuidance = true; Close(false); }
                catch (Exception error) { _error.Text = AuthoringWorkspaceViewModel.PersistenceError(error); }
            };
            buttons.Children.Insert(0, leave); buttons.Children.Insert(1, skip);
        }
        root.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = root;
        ShowDestination();
        if (retained is not null) RestoreGuidedForm(retained);
        ShowHardware(); ShowStage();
        if (guided && retained is not null && _stage >= 4)
        {
            try { Review(); }
            catch (Exception error) { _error.Text = AuthoringWorkspaceViewModel.PersistenceError(error); }
        }
    }

    private void EnsureSession()
    {
        if (_ownerIsCurrent?.Invoke() == false || _workspaceSession != _vm.WorkspaceSessionId || !ReferenceEquals(_workspace, _vm.Workspace)) throw new AuthoringWorkspaceException("The workspace changed; reopen New test plan.");
        if (!_vm.CanInitializePlan) throw new AuthoringWorkspaceException("Open a writable workspace and wait for the active operation.");
    }

    private PlanInitializationRequest Request()
    {
        EnsureGuidedIntervalApplicable();
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

    private void ShowDestination()
    {
        try { _destination.Text = new AuthoringDocumentStore(_workspace.Root).GetDocumentPath(_id.Text ?? ""); }
        catch (ArgumentException) { _destination.Text = "Enter a safe stable plan ID."; }
    }

    private void Review()
    {
        EnsureSession();
        var result = _vm.ReviewPlanInitialization(Request());
        _review.Text = (_guided ? "Preview the generated instrument identity and safe shutdown below. Save creates an editable draft; use the editor preview and Save/check guidance to finish.\n" : "") + result.Review + "\n" + string.Join("\n", result.Issues.Select(issue => issue.Message)) + "\n" + result.NextAction;
        if (_guided) _review.Text += "\nInstrument identity: " + string.Join(", ", result.Draft.Setup.OfType<IdentitySetup>().Select(check => check.InstrumentSlot))
            + "\nSafe shutdown: " + (result.Draft.Cleanup.IncludeSafeShutdown ? string.Join(", ", result.Draft.Cleanup.InstrumentSlots) : "disabled");
        _error.Text = "";
    }

    private void ShowStage()
    {
        string[] titles = _guided ? ["Name and device", "Instrument", "Measurement", "Pass criterion", "Preview", "Save and check"]
            : ["Name and destination", "Starting point", "Hardware", "Setup and cleanup", "First measurement and criterion", "Review and create"];
        _heading.Text = $"{_stage + 1} of 6 · {titles[_stage]}";
        for (var index = 0; index < _stages.Length; index++) _stages[index].IsVisible = index == _stage;
        _back.IsVisible = _stage > 0; _next.IsVisible = _stage < 5; _create.IsVisible = _stage == 5;
    }

    private static TextBox Input(string name, string value = "") => Named(new TextBox { Text = value }, name);
    private static ComboBox Choice(string name, string[] items) => Named(new ComboBox { ItemsSource = items, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch }, name);
    private static TextBlock Text(string name) => Named(new TextBlock { TextWrapping = TextWrapping.Wrap }, name);
    private static CheckBox Toggle(string name, bool value) => Named(new CheckBox { Content = name, IsChecked = value }, name);
    private static Button Action(string name) { var button = Named(new Button { Content = name }, name); button.Classes.Add("authoringAction"); return button; }
    private static T Named<T>(T control, string name) where T : Control { AutomationProperties.SetName(control, name); return control; }
    private static StackPanel Stage(params Control[] controls) { var panel = new StackPanel { Spacing = 10 }; foreach (var control in controls) panel.Children.Add(control); return panel; }
    private static StackPanel Label(string text, Control input) => Stage(new TextBlock { Text = text }, input);
}
