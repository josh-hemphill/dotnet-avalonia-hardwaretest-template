using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HardwareTest.Widgets.MeasurementPlot;
using HardwareTest.Widgets.Presentation;

namespace HardwareTest.Authoring;

/// Hosts the operator MetricGaugeView / plot / timing strip for the authoring preview column.
public sealed class OperatorPreviewPane : UserControl
{
    private readonly StackPanel _board = new() { Spacing = 12 };
    private AuthoringWorkspaceViewModel? _session;
    private bool _attached;
    private bool _applyQueued;
    private bool _needsApply = true;
    private IReadOnlyList<BoardPreviewTile>? _renderedTiles;
    private string? _emptyMessage;

    public OperatorPreviewPane()
    {
        var viewport = new ScrollViewer
        {
            Content = _board,
            Focusable = true,
            ClipToBounds = true,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        AutomationProperties.SetName(viewport, "Operator board viewport");
        Content = viewport;
        AutomationProperties.SetName(this, "Operator preview chrome");
        DataContextChanged += (_, _) => HookSession();
        SizeChanged += (_, _) => ConstrainCharts();
        LayoutUpdated += (_, _) =>
        {
            // A tab can remain bound while its content is hidden. Delay widget
            // construction until it is actually shown, and consume only the latest result.
            if (_needsApply) QueueApply();
            if (IsEffectivelyVisible) ConstrainCharts();
        };
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        HookSession();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        UnhookSession();
        _needsApply = true;
        base.OnDetachedFromVisualTree(e);
    }

    private void HookSession()
    {
        UnhookSession();
        if (_attached && DataContext is AuthoringWorkspaceViewModel session)
        {
            _session = session;
            session.PropertyChanged += OnSessionChanged;
        }

        _needsApply = true;
        QueueApply();
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
        if (e.PropertyName is nameof(AuthoringWorkspaceViewModel.BoardTiles) or nameof(AuthoringWorkspaceViewModel.PreviewBusy))
        {
            _needsApply = true;
            QueueApply();
        }
    }

    private void QueueApply()
    {
        if (!_attached || !IsEffectivelyVisible || _applyQueued) return;
        _applyQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _applyQueued = false;
            if (!_attached || !IsEffectivelyVisible) return;
            _needsApply = false;
            Apply();
        }, DispatcherPriority.Background);
    }

    private void Apply()
    {
        var tiles = _session?.BoardTiles ?? [];
        var message = tiles.Count != 0 ? null : _session?.PreviewBusy == true ? "Updating preview…"
            : _session?.Preview.Note ?? "Add acquisitions and calculations to see the board preview.";
        if (ReferenceEquals(tiles, _renderedTiles) && message == _emptyMessage) return;
        _renderedTiles = tiles;
        _emptyMessage = message;
        _board.Children.Clear();
        if (tiles.Count == 0)
        {
            _board.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var tile in tiles) _board.Children.Add(new BoardPreviewTileView(tile));
        ConstrainCharts();
    }

    private void ConstrainCharts()
    {
        foreach (var tile in _board.Children.OfType<BoardPreviewTileView>())
            tile.SetChartViewportHeight(Bounds.Height);
    }
}
