using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace HardwareTest;

/// Resolves page views from <see cref="ViewRegistry"/> (explicit factories, no Activator).
public sealed class ViewLocator(ViewRegistry registry) : IDataTemplate
{
    private readonly ViewRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public ViewLocator()
        : this(ViewRegistry.Shared)
    {
    }

    public Control? Build(object? data) => _registry.Build(data);

    public bool Match(object? data) => _registry.Match(data);
}
