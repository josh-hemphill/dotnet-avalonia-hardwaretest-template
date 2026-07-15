using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using HardwareTest.Features.Home;
using HardwareTest.Features.Instruments;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;

namespace HardwareTest;

/// AoT-safe ViewLocator using an explicit factory map (no Activator / reflection).
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Factories = new()
    {
        [typeof(HomeViewModel)] = static () => new HomeView(),
        [typeof(RunTestViewModel)] = static () => new RunTestView(),
        [typeof(ResultsViewModel)] = static () => new ResultsView(),
        [typeof(ReportPreviewViewModel)] = static () => new ReportPreviewView(),
        [typeof(InstrumentsViewModel)] = static () => new InstrumentsView(),
        [typeof(SettingsViewModel)] = static () => new SettingsView(),
    };

    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        return Factories.TryGetValue(data.GetType(), out var factory)
            ? factory()
            : new TextBlock { Text = $"Not Found: {data.GetType()}" };
    }

    public bool Match(object? data) => data is not null && Factories.ContainsKey(data.GetType());
}
