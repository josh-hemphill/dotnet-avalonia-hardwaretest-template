using Avalonia.Controls;
using Avalonia.Threading;

namespace HardwareTest.Authoring.UI.Tests;

internal static class GuidanceUiActions
{
    internal static TextBlock Feedback(AuthoringUiFixture fixture)
    {
        WaitForAssessment(fixture);
        return fixture.Control<TextBlock>("Guidance feedback");
    }

    internal static void WaitForAssessment(AuthoringUiFixture fixture)
    {
        AuthoringUiFixture.Drain();
        var primary = fixture.Control<Button>("Guidance next action");
        if (!primary.IsEffectivelyVisible || primary.IsEnabled) return;
        var frame = new DispatcherFrame();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task WaitAsync()
        {
            try { while (primary.IsEffectivelyVisible && !primary.IsEnabled) await Task.Delay(1, timeout.Token); }
            finally { frame.Continue = false; }
        }
        var pending = WaitAsync();
        if (!pending.IsCompleted) Dispatcher.UIThread.PushFrame(frame);
        pending.GetAwaiter().GetResult();
    }

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
