using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace HardwareTest.Authoring;

internal static class AuthoringFormLayout
{
    internal static TextBlock Heading(string title, int level = 3)
    {
        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        heading.Classes.Add(level == 2 ? "routeTitle" : "sectionTitle");
        AutomationProperties.SetHeadingLevel(heading, level);
        return heading;
    }

    internal static Border Section(string title, params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Heading(title));
        foreach (var control in controls) panel.Children.Add(control);
        var border = new Border { Child = panel };
        border.Classes.Add("surface"); border.Classes.Add("taskSection");
        return border;
    }

    internal static Grid Frame(string title, string purpose, Control body, Control footer, TextBlock? progress = null)
    {
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(Heading(title, 2));
        var description = new TextBlock { Text = purpose, TextWrapping = TextWrapping.Wrap };
        description.Classes.Add("purpose"); header.Children.Add(description);
        if (progress is not null) header.Children.Add(progress);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(16), RowSpacing = 12 };
        layout.Children.Add(header);
        var viewport = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(viewport, "Form details viewport");
        Grid.SetRow(viewport, 1); layout.Children.Add(viewport);
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        return layout;
    }
}
