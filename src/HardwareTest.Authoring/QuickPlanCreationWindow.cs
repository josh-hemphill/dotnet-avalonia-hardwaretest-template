using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

internal sealed record QuickPlanForm(string Name, string Id, string Family, bool IdEdited);

/// Names a normal empty authoring draft without visiting instrument-only wizard stages.
public sealed class QuickPlanCreationWindow : Window
{
    private readonly AuthoringWorkspaceViewModel _vm;
    private readonly Func<bool> _ownerIsCurrent;
    private readonly AuthoringWorkspace _workspace;
    private readonly Guid _session;
    private readonly TextBox _name = Input("Plan display name");
    private readonly TextBox _id = Input("Stable plan ID");
    private readonly TextBox _family = Input("Device family");
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _destination = new() { TextWrapping = TextWrapping.Wrap };
    private bool _idEdited;
    private string _automaticId;
    private TextBox? _errorField;
    private string? _previousFieldHelp;
    private string? _fieldError;

    public bool AdvancedRequested { get; private set; }

    internal QuickPlanCreationWindow(AuthoringWorkspaceViewModel vm, Func<bool> ownerIsCurrent, QuickPlanForm? retained = null)
    {
        _vm = vm;
        _ownerIsCurrent = ownerIsCurrent;
        _workspace = vm.Workspace ?? throw new AuthoringWorkspaceException("Open a workspace first.");
        _session = vm.WorkspaceSessionId;
        _automaticId = retained?.Id ?? vm.SuggestedPlanId;
        if (retained?.IdEdited != true) _automaticId = AuthoringPlanFormIds.Automatic(_automaticId);
        _name.Text = retained?.Name ?? "";
        _id.Text = _automaticId;
        _family.Text = retained?.Family ?? "";
        _idEdited = retained?.IdEdited ?? false;
        Title = "New test plan";
        Width = 600; Height = 560; MinWidth = 440; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("authoringPalette");

        var cancel = Action("Cancel");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close(false);
        var create = Action("Create test plan");
        create.IsDefault = true;
        create.Classes.Add("primaryAction");
        create.Click += (_, _) => Create();
        var advanced = Action("Start from hardware or template");
        advanced.Click += (_, _) =>
        {
            try { EnsureCurrent(); AdvancedRequested = true; Close(false); }
            catch (Exception error) { ShowError(error.Message, _name); }
        };
        AutomationProperties.SetName(_error, "Initialization error");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Assertive);
        _error.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("AuthoringError"));
        AutomationProperties.SetName(_destination, "Draft destination preview");
        var details = new Expander
        {
            Header = "Plan ID and destination",
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { Label("Stable plan ID", _id), _destination }
            }
        };
        AutomationProperties.SetName(details, "Plan ID and destination");
        var body = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Label("Plan title", _name),
                Label("Device family (optional)", _family),
                new TextBlock { Text = "Starts with an empty test-steps section. Add operator actions or measurements in Sequence. Device family defaults to generic when left blank.", TextWrapping = TextWrapping.Wrap, Classes = { "purpose" } },
                details,
                advanced
            }
        };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var action in new[] { cancel, create }) { action.Margin = new Thickness(4, 0); actions.Children.Add(action); }
        var footer = new StackPanel { Spacing = 10, Children = { _error, actions } };
        Content = AuthoringFormLayout.Frame("Create an empty test plan", "Name the test now. Configure instruments and steps when you need them.", body, footer);
        _name.TextChanged += (_, _) =>
        {
            ClearFieldError(_name);
            if (!_idEdited)
            {
                var token = Slug(_name.Text ?? "");
                _automaticId = AuthoringPlanFormIds.Automatic(token.Length == 0 ? vm.SuggestedPlanId : token);
                _id.Text = _automaticId;
            }
            ShowDestination();
        };
        _id.TextChanged += (_, _) => { ClearFieldError(_id); _idEdited = _id.Text != _automaticId; ShowDestination(); };
        _family.TextChanged += (_, _) => ClearFieldError(_family);
        Opened += (_, _) => { _name.Focus(); _name.SelectAll(); };
        ShowDestination();
    }

    internal QuickPlanForm CaptureForm() => new(_name.Text ?? "", _id.Text ?? "", _family.Text ?? "", _idEdited);

    private void EnsureCurrent()
    {
        if (!_ownerIsCurrent() || _session != _vm.WorkspaceSessionId || !ReferenceEquals(_workspace, _vm.Workspace))
            throw new AuthoringWorkspaceException("The workspace changed. Close this form and create the plan in the current workspace.");
        if (!_vm.CanInitializePlan) throw new AuthoringWorkspaceException("Open a writable workspace and finish the active operation first.");
    }

    private void Create()
    {
        try
        {
            ClearFieldError();
            _error.Text = "";
            EnsureCurrent();
            if (string.IsNullOrWhiteSpace(_name.Text)) { ShowError("Enter a title for this test plan.", _name); return; }
            if (_vm.Programs.Any(program => string.Equals(program.PlanId, _id.Text?.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                ShowError("A plan already uses this ID. Choose a different plan ID, or change the title to generate another one.", _id);
                return;
            }
            AuthoringPlanFormIds.RequireCustom(_id.Text ?? "");
            var request = new PlanInitializationRequest(_id.Text ?? "")
            {
                WorkspaceRoot = _workspace.Root,
                DisplayName = _name.Text,
                DeviceFamily = string.IsNullOrWhiteSpace(_family.Text) ? "generic" : _family.Text,
                StartingPoint = PlanStartingPoint.Empty,
                IncludeTemplateMeasurement = false,
                UseTemplateHardware = false,
                Instruments = [],
                IncludeSafeShutdown = false
            };
            _vm.ReviewPlanInitialization(request);
            _vm.InitializePlan(request);
            Close(true);
        }
        catch (Exception error)
        {
            ShowError(AuthoringWorkspaceViewModel.PersistenceError(error), error.Message.Contains("family", StringComparison.OrdinalIgnoreCase) ? _family
                : error.Message.Contains("display name", StringComparison.OrdinalIgnoreCase) ? _name : _id);
        }
    }

    private void ShowError(string message, TextBox field)
    {
        ClearFieldError();
        _error.Text = message;
        _errorField = field;
        _previousFieldHelp = AutomationProperties.GetHelpText(field);
        _fieldError = message;
        field.SetCurrentValue(AutomationProperties.HelpTextProperty, message);
        // Expander content is not visible until explicitly opened.
        if (ReferenceEquals(field, _id) && Content is Control content)
        {
            var expander = content.GetVisualDescendants().OfType<Expander>().Single();
            expander.IsExpanded = true;
        }
        Dispatcher.UIThread.Post(() => { if (IsVisible) { field.BringIntoView(); field.Focus(); field.SelectAll(); } }, DispatcherPriority.Loaded);
    }

    private void ClearFieldError(TextBox? editedField = null)
    {
        if (_errorField is not { } field || editedField is not null && !ReferenceEquals(editedField, field)) return;
        // Restore only this validation message; keep any underlying help binding intact.
        if (AutomationProperties.GetHelpText(field) == _fieldError)
            field.SetCurrentValue(AutomationProperties.HelpTextProperty, _previousFieldHelp);
        _errorField = null;
        _previousFieldHelp = null;
        _fieldError = null;
        _error.Text = "";
    }

    private void ShowDestination()
    {
        try { _destination.Text = new AuthoringDocumentStore(_workspace.Root).GetDocumentPath(_id.Text ?? ""); }
        catch (ArgumentException) { _destination.Text = "Use letters, numbers, hyphens, underscores or periods in the plan ID."; }
    }

    private static string Slug(string value)
        => string.Join('-', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => new string(part.Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-').ToArray())));
    private static TextBox Input(string name) { var box = new TextBox(); AutomationProperties.SetName(box, name); return box; }
    private static Button Action(string name) { var button = new Button { Content = name, ContentTemplate = AuthoringActionLabels.WrappedText, Classes = { "authoringAction" } }; AutomationProperties.SetName(button, name); return button; }
    private static StackPanel Label(string label, Control field)
    {
        var text = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLabeledBy(field, text);
        return new StackPanel { Spacing = 6, Children = { text, field } };
    }
}
