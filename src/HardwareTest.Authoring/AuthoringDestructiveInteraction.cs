using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HardwareTest.Authoring;

/// Focused, safe-default review dialogs. Dismissal never authorizes a destructive operation.
public sealed class AuthoringDestructiveInteraction(Window owner)
{
    public Task<bool> ConfirmCatalogDeletionAsync(CatalogDeletionImpact impact)
    {
        var details = new List<string>
        {
            impact.Scope,
            "This removes the entry from the workspace catalog and updates only the affected programs listed below.",
            "Files stay unchanged until Save All.",
            "",
            $"Affected programs: {impact.AffectedPrograms.Count}",
        };
        details.AddRange(impact.AffectedPrograms.Select(p => $"{p.DisplayName} ({p.PlanId})\n  {string.Join(", ", p.Nodes)}"));
        if (impact.AffectedPrograms.Count == 0) details.Add("No program sidecars use this entry; only the workspace catalog changes.");
        var (dialog, _, cancel, confirm) = Build(impact.OperationName, string.Join(Environment.NewLine, details), "Remove from workspace");
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(false); e.Handled = true; } };
        return dialog.ShowDialog<bool>(owner);
    }

    public Task<string?> ChooseInstrumentReplacementAsync(InstrumentRemovalImpact impact)
    {
        var details = $"{impact.Scope}\nChoose the compatible slot that will replace every known reference below. The workspace slot catalog stays unchanged.\nSave this program to persist the edit.\n\nKnown affected references:\n{string.Join(Environment.NewLine, impact.AffectedNodes)}";
        var (dialog, body, cancel, confirm) = Build(impact.OperationName, details, "Remove and replace slot");
        var chooser = new ComboBox { ItemsSource = impact.CompatibleReplacementSlots, SelectedIndex = -1, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(chooser, "Compatible replacement instrument slot");
        var label = new TextBlock { Text = "Choose replacement slot (required)", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLabeledBy(chooser, label);
        body.Children.Add(label);
        body.Children.Add(chooser);
        confirm.IsEnabled = false;
        chooser.SelectionChanged += (_, _) => confirm.IsEnabled = chooser.SelectedItem is string;
        cancel.Click += (_, _) => dialog.Close((string?)null);
        confirm.Click += (_, _) => { if (chooser.SelectedItem is string replacement) dialog.Close(replacement); };
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close((string?)null); e.Handled = true; } };
        return dialog.ShowDialog<string?>(owner);
    }

    public Task<bool> ConfirmProgramRemovalAsync((string PlanId, string TapPlanPath, string SidecarPath) target)
    {
        var (dialog, _, cancel, confirm) = Build($"Remove program '{target.PlanId}'?",
            $"Program '{target.PlanId}' only\nThese files will be deleted if they exist:\n{target.TapPlanPath}\n{target.SidecarPath}\nOther programs stay unchanged.", "Remove program");
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { dialog.Close(false); e.Handled = true; } };
        return dialog.ShowDialog<bool>(owner);
    }

    private (Window Dialog, StackPanel Body, Button Cancel, Button Confirm) Build(string operation, string details, string affirmative)
    {
        var dialog = new Window
        {
            Title = operation,
            Width = Math.Min(560, Math.Max(360, owner.ClientSize.Width - 80)),
            Height = Math.Min(480, Math.Max(240, owner.ClientSize.Height - 80)),
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var cancel = new Button { Content = "Cancel", IsDefault = true, IsCancel = true };
        var confirm = new Button { Content = affirmative };
        AutomationProperties.SetName(cancel, "Cancel destructive operation");
        AutomationProperties.SetName(confirm, affirmative);
        var body = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = operation + Environment.NewLine + details, TextWrapping = TextWrapping.Wrap } } };
        var scroll = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 12),
        };
        AutomationProperties.SetName(scroll, "Destructive operation scope and impact");
        var heading = new TextBlock { Text = operation, TextWrapping = TextWrapping.Wrap, MaxHeight = 64, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeight.SemiBold };
        AutomationProperties.SetHeadingLevel(heading, 2);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, confirm } };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20) };
        Grid.SetRow(scroll, 1); Grid.SetRow(buttons, 2);
        layout.Children.Add(heading); layout.Children.Add(scroll); layout.Children.Add(buttons);
        dialog.Content = layout;
        dialog.Opened += (_, _) => cancel.Focus();
        return (dialog, body, cancel, confirm);
    }
}
