using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private readonly TextBlock _guidancePlan = GuidanceText("Guidance plan name");
    private readonly TextBlock _guidanceHardware = GuidanceText("Guidance hardware mode");
    private readonly TextBlock _guidanceIdentity = GuidanceText("Guidance identity checks");
    private readonly TextBlock _guidanceShutdown = GuidanceText("Guidance safe shutdown");
    private readonly TextBlock _guidanceNext = GuidanceText("Guidance next step");
    private readonly TextBlock _guidanceWarning = GuidanceText("Guidance save warning");
    private readonly StackPanel _guidanceBlockers = new() { Spacing = 8 };
    private readonly Expander _guidanceDetails = new() { Header = "Details to resolve", HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Button _guidancePrimary = new() { ContentTemplate = AuthoringActionLabels.WrappedText, HorizontalAlignment = HorizontalAlignment.Stretch };
    private System.Action? _guidancePrimaryAction;

    private static TextBlock GuidanceText(string name)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(text, name);
        return text;
    }

    private StackPanel GuidanceContent(StackPanel checks, StackPanel deployment, StackPanel preferences)
    {
        _guidancePlan.FontWeight = FontWeight.SemiBold;
        _guidanceFeedback.FontWeight = FontWeight.SemiBold;
        _guidancePrimary.Classes.Add("authoringAction");
        _guidancePrimary.Classes.Add("primaryAction");
        AutomationProperties.SetName(_guidancePrimary, "Guidance next action");
        _guidancePrimary.Click += (_, _) =>
        {
            if (GuidanceTargetCurrent()) _guidancePrimaryAction?.Invoke();
            RefreshGuidance();
        };
        AutomationProperties.SetName(_guidanceBlockers, "Guidance blocker items");
        AutomationProperties.SetName(_guidanceDetails, "Guidance recovery details");
        _guidanceDetails.Content = _guidanceBlockers;
        Expander Group(string title, Control content)
        {
            var expander = new Expander { Header = title, Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(expander, title);
            return expander;
        }
        return new StackPanel
        {
            Spacing = 14,
            Children =
        {
            AuthoringFormLayout.Heading("First test plan", 3),
            new StackPanel { Spacing = 5, Children = { _guidancePlan, _guidanceHardware, _guidanceIdentity, _guidanceShutdown } },
            AuthoringFormLayout.Heading("Status", 3), _guidanceFeedback,
            AuthoringFormLayout.Heading("Next action", 3), _guidanceNext, _guidancePrimary,
            _guidanceWarning, _guidanceDetails,
            Group("Draft checks", checks), Group("Preview and deployment", deployment), Group("Guidance preferences", preferences)
        }
        };
    }

    private void PresentGuidance(string status, string next, string actionLabel, System.Action action, IEnumerable<string> details)
    {
        _guidanceFeedback.Text = status;
        _guidanceNext.Text = next;
        _guidancePrimary.Content = actionLabel;
        _guidancePrimaryAction = action;
        var messages = details.Where(message => !string.IsNullOrWhiteSpace(message)).Distinct().ToArray();
        // Preserve the user's expansion choice unless a new blocker appears.
        var previous = _guidanceBlockers.Children.OfType<TextBlock>().Select(text => text.Text).ToArray();
        if (!previous.SequenceEqual(messages))
        {
            _guidanceBlockers.Children.Clear();
            for (var index = 0; index < messages.Length; index++)
            {
                var item = GuidanceText($"Guidance blocker {index + 1}");
                item.Text = messages[index];
                _guidanceBlockers.Children.Add(item);
            }
            _guidanceDetails.IsExpanded = messages.Length > 0;
        }
        _guidanceDetails.IsVisible = messages.Length > 0;
        _guidanceWarning.Text = _viewModel.SavePreviewWarning;
        _guidanceWarning.IsVisible = !string.IsNullOrWhiteSpace(_guidanceWarning.Text);
    }

    private void GuidanceSave() { CommitFocusedEditor(); TryRun(() => _viewModel.Apply()); }
    private void GuidanceIssues() => WorkspaceTabs.SelectedIndex = 2;
}
