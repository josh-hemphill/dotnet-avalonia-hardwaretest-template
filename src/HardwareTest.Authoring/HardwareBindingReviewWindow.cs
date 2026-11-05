using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HardwareTest.Authoring;

/// Presents the immutable reviewed replacement; application remains guarded by its receipt.
public sealed class HardwareBindingReviewWindow : Window
{
    internal HardwareBindingReviewWindow(Window owner, AuthoringHardwareEditImpact impact)
    {
        Title = "Review hardware binding change";
        Width = Math.Min(760, Math.Max(440, owner.ClientSize.Width - 48));
        Height = Math.Min(600, Math.Max(360, owner.ClientSize.Height - 48));
        MinWidth = 440; MinHeight = 360;
        FontSize = owner.FontSize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("authoringPalette");

        var program = (owner.DataContext as AuthoringWorkspaceViewModel)?.SelectedProgram;
        var planName = program?.PlanId == impact.PlanId ? program.Sidecar.DisplayName : impact.PlanId;
        if (string.IsNullOrWhiteSpace(planName)) planName = impact.PlanId;
        var current = program?.PlanId == impact.PlanId
            ? program.Instruments.FirstOrDefault(binding => binding.SlotName == impact.SlotName) : null;
        var replacement = impact.Replacement;
        var adapterName = AuthoringInstrumentCatalog.TryGet(replacement.TypeId, out var adapter)
            ? adapter.DisplayName : "Imported instrument adapter";

        var proposed = new StackPanel { Spacing = 8 };
        proposed.Children.Add(Value("Test plan", planName, "Reviewed binding plan"));
        proposed.Children.Add(Value("Instrument slot", impact.SlotName, "Reviewed binding slot"));
        proposed.Children.Add(Value("Adapter", adapterName, "Reviewed binding adapter"));
        if (current is not null && current.TypeId != replacement.TypeId)
            proposed.Children.Add(Note("Previous adapter: " + (AuthoringInstrumentCatalog.TryGet(current.TypeId, out var previous)
                ? previous.DisplayName : "Imported instrument adapter")));
        proposed.Children.Add(Value("Address", replacement.VisaAddress, "Reviewed binding address"));
        if (current is not null && current.VisaAddress != replacement.VisaAddress)
            proposed.Children.Add(Note($"Previously: {current.VisaAddress}"));
        if (replacement.Settings.TryGetValue("IoTimeoutMilliseconds", out var timeout))
            proposed.Children.Add(Value("I/O timeout", timeout + " ms", "Reviewed binding timeout"));

        var stepReferences = impact.AffectedNodes.Where(IsStep).ToArray();
        var affected = new StackPanel { Spacing = 6 };
        if (stepReferences.Length == 0)
            affected.Children.Add(Note("No test steps currently use this slot."));
        foreach (var reference in stepReferences)
        {
            var label = reference.StartsWith("Setup[", StringComparison.Ordinal)
                ? "Device identification" : reference[(reference.IndexOf(' ') + 1)..];
            affected.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        }
        if (impact.AffectedNodes.Any(reference => reference.StartsWith("Cleanup", StringComparison.Ordinal)
            || reference.StartsWith("Sidecar", StringComparison.Ordinal)))
            affected.Children.Add(Note("Safe shutdown continues to use this instrument slot."));

        var detailsText = new TextBlock
        {
            Text = $"Plan ID: {impact.PlanId}\nAdapter type: {replacement.TypeId}\nConfiguration: "
                + (replacement.Settings.Count == 0 ? "None" : string.Join(", ", replacement.Settings.Select(setting => $"{setting.Key}={setting.Value}")))
                + $"\nAffected references:\n{string.Join(Environment.NewLine, impact.AffectedNodes)}",
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(detailsText, "Reviewed binding technical details");
        var details = new Expander
        {
            Header = "Adapter and reference details",
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = detailsText,
        };
        details.Classes.Add("inspectorSection");
        AutomationProperties.SetName(details, "Reviewed binding details");
        var body = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                proposed,
                new StackPanel { Spacing = 8, Children = { AuthoringFormLayout.Heading($"Affected steps ({stepReferences.Length})"), affected } },
                details,
            },
        };

        var cancel = Action("Cancel", "Cancel destructive operation");
        cancel.IsDefault = true; cancel.IsCancel = true;
        cancel.Click += (_, _) => Close(false);
        var apply = Action("Apply reviewed binding", "Apply reviewed binding");
        apply.Classes.Add("primaryAction");
        apply.Click += (_, _) => Close(true);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(cancel); actions.Children.Add(apply);
        Content = AuthoringFormLayout.Frame("Review instrument binding",
            "Apply to this test plan. Save all when you are ready to persist the change.", body, actions);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(false); e.Handled = true; } };
        Opened += (_, _) => cancel.Focus();
    }

    private static bool IsStep(string reference)
        => reference.StartsWith("Measure[", StringComparison.Ordinal) || reference.StartsWith("Setup[", StringComparison.Ordinal);

    private static Control Value(string label, string value, string name)
    {
        var content = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
        AutomationProperties.SetName(content, name);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*"), ColumnSpacing = 12 };
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Classes = { "purpose" } });
        Grid.SetColumn(content, 1); row.Children.Add(content);
        return row;
    }

    private static TextBlock Note(string text)
        => new() { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { "purpose" } };

    private static Button Action(string text, string name)
    {
        var button = new Button { Content = text, ContentTemplate = AuthoringActionLabels.WrappedText, Margin = new Thickness(4, 0) };
        button.Classes.Add("authoringAction");
        AutomationProperties.SetName(button, name);
        return button;
    }
}
