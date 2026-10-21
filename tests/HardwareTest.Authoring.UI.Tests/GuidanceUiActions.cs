using Avalonia.Controls;

namespace HardwareTest.Authoring.UI.Tests;

internal static class GuidanceUiActions
{
    internal static Button Action(AuthoringUiFixture fixture, string name)
    {
        var group = name is "Leave saved guidance" or "Skip saved guidance" ? "Guidance preferences"
            : name is "Fix guidance blockers" or "Validate saved plans" or "Save and check draft" ? "Draft checks" : "Preview and deployment";
        fixture.Control<Expander>(group).IsExpanded = true;
        AuthoringUiFixture.Drain();
        var button = fixture.Control<Button>(name);
        button.BringIntoView();
        AuthoringUiFixture.Drain();
        return button;
    }
}
