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
    public RecordingPickerWindow(IReadOnlyList<string> labels, int initialIndex)
    {
        var labelSnapshot = Array.AsReadOnly(labels.ToArray());
        Title = "Choose a recording";
        Width = 580;
        Height = 420;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("authoringPalette");
        var recordings = new ListBox
        {
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
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancel, use }
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
        recordings.SelectionChanged += (_, _) =>
            use.IsEnabled = recordings.SelectedIndex >= 0 && recordings.SelectedIndex < labelSnapshot.Count;
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
