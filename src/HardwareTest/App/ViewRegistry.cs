using System;
using Avalonia.Controls;
using HardwareTest.Features.Home;
using HardwareTest.Features.Inspect;
using HardwareTest.Features.Instruments;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;

namespace HardwareTest;

/// Explicit ViewModel → view factory map. Call <see cref="Register"/> to add pages; no type scanning.
public interface IViewRegistrar
{
    void Register(Type viewModelType, Func<Control> factory);
    bool IsRegistered(Type viewModelType);
}

/// Process-wide registrar used by the XAML <see cref="ViewLocator"/> (created before DI).
public sealed class ViewRegistry : IViewRegistrar
{
    public static ViewRegistry Shared { get; } = new();

    private readonly Dictionary<Type, Func<Control>> _factories = [];
    private readonly object _sync = new();

    public ViewRegistry()
    {
        Register(typeof(HomeViewModel), static () => new HomeView());
        Register(typeof(RunTestViewModel), static () => new RunTestView());
        Register(typeof(InspectViewModel), static () => new InspectView());
        Register(typeof(ResultsViewModel), static () => new ResultsView());
        Register(typeof(ReportPreviewViewModel), static () => new ReportPreviewView());
        Register(typeof(InstrumentsViewModel), static () => new InstrumentsView());
        Register(typeof(SettingsViewModel), static () => new SettingsView());
    }

    public void Register(Type viewModelType, Func<Control> factory)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        ArgumentNullException.ThrowIfNull(factory);
        lock (_sync)
        {
            _factories[viewModelType] = factory;
        }
    }

    public bool IsRegistered(Type viewModelType)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        lock (_sync)
        {
            return _factories.ContainsKey(viewModelType);
        }
    }

    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        Func<Control>? factory;
        lock (_sync)
        {
            _factories.TryGetValue(data.GetType(), out factory);
        }

        return factory is null
            ? new TextBlock { Text = $"Not Found: {data.GetType()}" }
            : factory();
    }

    public bool Match(object? data)
        => data is not null && IsRegistered(data.GetType());
}
