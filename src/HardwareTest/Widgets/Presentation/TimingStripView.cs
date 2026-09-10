using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Widgets.Presentation;

/// 1-D elapsed axis with event ticks and optional out-of-band window bars. No Y scale.
public sealed class TimingStripView : UserControl
{
    public static readonly StyledProperty<IEnumerable<MeasurementEventMark>?> EventsProperty =
        AvaloniaProperty.Register<TimingStripView, IEnumerable<MeasurementEventMark>?>(nameof(Events));

    public static readonly StyledProperty<IEnumerable<(double T0, double T1)>?> SpansProperty =
        AvaloniaProperty.Register<TimingStripView, IEnumerable<(double T0, double T1)>?>(nameof(Spans));

    public static readonly StyledProperty<double> DurationSecProperty =
        AvaloniaProperty.Register<TimingStripView, double>(nameof(DurationSec));

    static TimingStripView()
    {
        AffectsRender<TimingStripView>(EventsProperty, SpansProperty, DurationSecProperty);
    }

    public TimingStripView()
    {
        MinHeight = 36;
        Height = 36;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        HookEvents(Events, null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EventsProperty)
        {
            HookEvents(
                change.OldValue as IEnumerable<MeasurementEventMark>,
                change.NewValue as IEnumerable<MeasurementEventMark>);
        }
    }

    public IEnumerable<MeasurementEventMark>? Events
    {
        get => GetValue(EventsProperty);
        set => SetValue(EventsProperty, value);
    }

    public IEnumerable<(double T0, double T1)>? Spans
    {
        get => GetValue(SpansProperty);
        set => SetValue(SpansProperty, value);
    }

    public double DurationSec
    {
        get => GetValue(DurationSecProperty);
        set => SetValue(DurationSecProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (bounds.Width < 8 || bounds.Height < 8)
        {
            return;
        }

        var axisY = bounds.Height * 0.62;
        var left = 8.0;
        var right = bounds.Width - 8.0;
        var width = Math.Max(1, right - left);
        var duration = Math.Max(DurationSec, MaxElapsed());
        if (duration <= 0)
        {
            duration = 1;
        }

        var axis = new Pen(new SolidColorBrush(Color.FromArgb(160, 128, 128, 128)), 1.25);
        context.DrawLine(axis, new Point(left, axisY), new Point(right, axisY));

        var oobBrush = new SolidColorBrush(Color.FromArgb(50, 198, 40, 40));
        if (Spans is not null)
        {
            foreach (var (t0, t1) in Spans)
            {
                var x0 = left + (Math.Min(t0, t1) / duration) * width;
                var x1 = left + (Math.Max(t0, t1) / duration) * width;
                if (x1 <= x0)
                {
                    x1 = x0 + 2;
                }

                context.FillRectangle(oobBrush, new Rect(x0, 6, x1 - x0, bounds.Height - 12));
            }
        }

        var tick = new Pen(new SolidColorBrush(Color.FromArgb(220, 123, 31, 162)), 1.5);
        if (Events is null)
        {
            return;
        }

        foreach (var mark in Events)
        {
            var x = left + (mark.ElapsedMs / 1000.0 / duration) * width;
            context.DrawLine(tick, new Point(x, 8), new Point(x, bounds.Height - 6));
        }
    }

    private void HookEvents(IEnumerable<MeasurementEventMark>? previous, IEnumerable<MeasurementEventMark>? next)
    {
        if (previous is INotifyCollectionChanged oldNotify)
        {
            oldNotify.CollectionChanged -= OnEventsChanged;
        }

        if (next is INotifyCollectionChanged notify)
        {
            notify.CollectionChanged += OnEventsChanged;
        }

        InvalidateVisual();
    }

    private void OnEventsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => InvalidateVisual();

    private double MaxElapsed()
    {
        var max = 0.0;
        if (Events is not null)
        {
            foreach (var mark in Events)
            {
                max = Math.Max(max, mark.ElapsedMs / 1000.0);
            }
        }

        if (Spans is not null)
        {
            foreach (var (t0, t1) in Spans)
            {
                max = Math.Max(max, Math.Max(t0, t1));
            }
        }

        return max;
    }
}
