using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HardwareTest.Authoring;

public sealed class RecordingPickerWindow : Window
{
    public RecordingPickerWindow(IReadOnlyList<string> labels, int initialIndex, IReadOnlyList<string>? descriptions = null)
    {
        var labelSnapshot = Array.AsReadOnly(labels.ToArray());
        var descriptionSnapshot = Array.AsReadOnly((descriptions ?? labels).ToArray());
        if (descriptionSnapshot.Count != labelSnapshot.Count) throw new ArgumentException("Each recording needs its matching details.", nameof(descriptions));
        Title = "Choose a recording";
        Width = 580;
        Height = 500;
        MinWidth = 420;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("authoringPalette");
        var recordings = new ListBox
        {
            Focusable = true,
            ItemsSource = labelSnapshot,
            SelectedIndex = initialIndex >= 0 && initialIndex < labelSnapshot.Count ? initialIndex : -1,
            Classes = { "authoringList" },
            ItemTemplate = new FuncDataTemplate<string>((label, _) =>
            {
                var text = new TextBlock
                {
                    Text = label,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                ToolTip.SetTip(text, label);
                AutomationProperties.SetName(text, label ?? string.Empty);
                return text;
            })
        };
        AutomationProperties.SetName(recordings, "Recordings");
        KeyboardNavigation.SetTabNavigation(recordings, KeyboardNavigationMode.Once);
        ScrollViewer.SetHorizontalScrollBarVisibility(recordings, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(recordings, ScrollBarVisibility.Auto);
        var cancel = new Button
        {
            Content = "Cancel",
            ContentTemplate = AuthoringActionLabels.WrappedText,
            IsCancel = true,
            Classes = { "authoringAction" }
        };
        AutomationProperties.SetName(cancel, "Cancel recording selection");
        var use = new Button
        {
            Content = "Use recording",
            ContentTemplate = AuthoringActionLabels.WrappedText,
            IsDefault = true,
            IsEnabled = recordings.SelectedIndex >= 0,
            Classes = { "authoringAction", "primaryAction" }
        };
        AutomationProperties.SetName(use, "Use recording");
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancel, use }
        };
        var selectedDetails = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 124
        };
        AutomationProperties.SetName(selectedDetails, "Selected recording details");
        ScrollViewer.SetHorizontalScrollBarVisibility(selectedDetails, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(selectedDetails, ScrollBarVisibility.Auto);
        void ShowSelection()
        {
            var index = recordings.SelectedIndex;
            use.IsEnabled = index >= 0 && index < labelSnapshot.Count;
            selectedDetails.Text = use.IsEnabled ? descriptionSnapshot[index] : "Choose a recording above to inspect its identity.";
        }
        ShowSelection();
        var footer = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                new TextBlock { Text = "Selected recording", FontWeight = FontWeight.SemiBold },
                selectedDetails,
                actions
            }
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        var heading = new TextBlock
        {
            Text = "Choose a recording",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AutomationProperties.SetHeadingLevel(heading, 1);
        DockPanel.SetDock(heading, Dock.Top);
        var panel = new DockPanel { Margin = new Thickness(20) };
        panel.Children.Add(footer);
        panel.Children.Add(heading);
        panel.Children.Add(recordings);
        Content = panel;
        recordings.SelectionChanged += (_, _) => ShowSelection();
        cancel.Click += (_, _) => Close((int?)null);
        use.Click += (_, _) =>
        {
            if (recordings.SelectedIndex >= 0 && recordings.SelectedIndex < labelSnapshot.Count)
                Close((int?)recordings.SelectedIndex);
        };
        Opened += (_, _) =>
        {
            recordings.Focus();
            if (recordings.SelectedItem is { } selected) recordings.ScrollIntoView(selected);
        };
    }
}
