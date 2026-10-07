using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HardwareTest.Widgets.MeasurementPlot;
using HardwareTest.Widgets.Presentation;

namespace HardwareTest.Authoring;

/// Hosts the operator MetricGaugeView / plot / timing strip for the authoring preview column.
public sealed class OperatorPreviewPane : UserControl
{
    private readonly StackPanel _board = new() { Spacing = 12 };
    private AuthoringWorkspaceViewModel? _session;

    public OperatorPreviewPane()
    {
        Content = new ScrollViewer
        {
            Content = _board,
            ClipToBounds = true,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        AutomationProperties.SetName(this, "Operator preview chrome");
        DataContextChanged += (_, _) => HookSession();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HookSession();
        Apply();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        UnhookSession();
        base.OnDetachedFromVisualTree(e);
    }

    private void HookSession()
    {
        UnhookSession();
        if (DataContext is AuthoringWorkspaceViewModel session)
        {
            _session = session;
            session.PropertyChanged += OnSessionChanged;
        }

        Apply();
    }

    private void UnhookSession()
    {
        if (_session is null)
        {
            return;
        }

        _session.PropertyChanged -= OnSessionChanged;
        _session = null;
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AuthoringWorkspaceViewModel.BoardTiles))
        {
            Apply();
        }
    }

    private void Apply()
    {
        _board.Children.Clear();
        var tiles = _session?.BoardTiles ?? [];
        if (tiles.Count == 0)
        {
            _board.Children.Add(new TextBlock { Text = "Add acquisitions and calculations to see the board preview.", TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var tile in tiles) _board.Children.Add(new BoardPreviewTileView(tile));
    }
}
