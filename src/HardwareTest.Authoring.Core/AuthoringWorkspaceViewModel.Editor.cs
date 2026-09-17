using System.Globalization;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public int SelectedMeasureIndex
    {
        get => _selectedMeasureIndex;
        set => SelectMeasure(value);
    }

    public string? SelectedRecipeId
    {
        get => _selectedRecipeId;
        set => SetField(ref _selectedRecipeId, value);
    }

    public MeasureNode? SelectedMeasure
        => SelectedProgram is null
           || _selectedMeasureIndex < 0
           || _selectedMeasureIndex >= SelectedProgram.Measure.Count
            ? null
            : SelectedProgram.Measure[_selectedMeasureIndex];

    public MetricDraft? SelectedMetric => SelectedMeasure switch
    {
        MetricNode metric => metric.Metric,
        RepeatNode repeat => repeat.Children.OfType<MetricNode>().FirstOrDefault()?.Metric,
        _ => null,
    };

    public IReadOnlyList<string> MeasureItems { get; private set; } = [];

    public MetricPreview Preview
    {
        get
        {
            var siblings = SelectedProgram is null
                ? []
                : AuthoringRecipeCatalog.EnumerateMetrics(SelectedProgram.Measure).ToArray();
            return MetricPreviewBuilder.From(SelectedMetric, siblings);
        }
    }

    public string PreviewKind => Preview.TileKind?.ToString() ?? "Text";

    public string ChannelKey
    {
        get => SelectedMetric?.ChannelKey ?? string.Empty;
        set => UpdateSelectedMetric(metric => metric with { ChannelKey = value });
    }

    public string DisplayRole
    {
        get => SelectedMetric?.DisplayRole ?? string.Empty;
        set => UpdateSelectedMetric(metric => metric with { DisplayRole = value });
    }

    public string YUnit
    {
        get => SelectedMetric?.YUnit ?? string.Empty;
        set => UpdateSelectedMetric(metric => metric with { YUnit = value });
    }

    public string LimitLow
    {
        get => FormatLimit(SelectedMetric?.Limits?.Low);
        set => UpdateLimits(ParseLimit(value), SelectedMetric?.Limits?.High, SelectedMetric?.Limits?.Threshold);
    }

    public string LimitHigh
    {
        get => FormatLimit(SelectedMetric?.Limits?.High);
        set => UpdateLimits(SelectedMetric?.Limits?.Low, ParseLimit(value), SelectedMetric?.Limits?.Threshold);
    }

    public string Threshold
    {
        get => FormatLimit(SelectedMetric?.Limits?.Threshold);
        set => UpdateLimits(SelectedMetric?.Limits?.Low, SelectedMetric?.Limits?.High, ParseLimit(value));
    }

    public string FormulaSource
    {
        get => SelectedMetric?.Source is ExpressionAlgorithm expr ? expr.Source : string.Empty;
        set
        {
            UpdateSelectedMetric(metric =>
            {
                var keys = metric.Source is ExpressionAlgorithm existing
                    ? existing.InputChannelKeys
                    : AuthoringRecipeCatalog.EnumerateMetrics(SelectedProgram?.Measure ?? [])
                        .Select(m => m.ChannelKey)
                        .Where(key => !string.Equals(key, metric.ChannelKey, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                return metric with { Source = new ExpressionAlgorithm(keys, value) };
            });
        }
    }

    public string FormulaError
    {
        get
        {
            if (SelectedMetric?.Source is not ExpressionAlgorithm expr)
            {
                return string.Empty;
            }

            try
            {
                FormulaParser.Parse(expr.Source);
                return string.Empty;
            }
            catch (AuthoringWorkspaceException ex)
            {
                return ex.Message;
            }
        }
    }

    public string VisaAddress
    {
        get => SelectedProgram?.Instruments.FirstOrDefault()?.VisaAddress ?? string.Empty;
        set
        {
            if (SelectedProgram is null || SelectedProgram.Instruments.Count == 0)
            {
                return;
            }

            var first = SelectedProgram.Instruments[0] with { VisaAddress = value };
            var rest = SelectedProgram.Instruments.Skip(1);
            ReplaceSelected(SelectedProgram with { Instruments = [first, .. rest] });
        }
    }

    public string RawTypeName => SelectedMeasure is RawStepNode raw ? raw.TypeName : string.Empty;

    public string RawXml => SelectedMeasure is RawStepNode raw ? raw.XmlFragment : string.Empty;

    public void SelectMeasure(int index)
    {
        var count = SelectedProgram?.Measure.Count ?? 0;
        var clamped = count == 0 ? -1 : Math.Clamp(index, 0, count - 1);
        if (!SetField(ref _selectedMeasureIndex, clamped, nameof(SelectedMeasureIndex)))
        {
            RaiseEditorProperties();
            return;
        }

        RaiseEditorProperties();
    }

    public void ApplySelectedRecipe()
    {
        if (string.IsNullOrWhiteSpace(SelectedRecipeId))
        {
            throw new AuthoringWorkspaceException("Select a recipe before adding it.");
        }

        ApplyRecipe(SelectedRecipeId);
    }

    private void RefreshMeasurePresentation()
    {
        MeasureTree = SelectedProgram is null ? [] : DescribeMeasure(SelectedProgram.Measure);
        MeasureItems = SelectedProgram is null
            ? []
            : SelectedProgram.Measure.Select(DescribeTop).ToArray();
        OnPropertyChanged(nameof(MeasureItems));
        var count = SelectedProgram?.Measure.Count ?? 0;
        _selectedMeasureIndex = count == 0 ? -1 : Math.Clamp(_selectedMeasureIndex < 0 ? 0 : _selectedMeasureIndex, 0, count - 1);
        OnPropertyChanged(nameof(SelectedMeasureIndex));
        OnPropertyChanged(nameof(MeasureHint));
        RaiseEditorProperties();
    }

    private static string DescribeTop(MeasureNode node)
        => node switch
        {
            MetricNode metric => $"{metric.Metric.Name} [{metric.Metric.DisplayRole}] {metric.Metric.ChannelKey}",
            RepeatNode repeat => $"Repeat x{repeat.Count}",
            RawStepNode raw => raw.TypeName,
            _ => node.GetType().Name,
        };

    private void UpdateSelectedMetric(Func<MetricDraft, MetricDraft> mutate)
    {
        if (SelectedProgram is null || _selectedMeasureIndex < 0
            || _selectedMeasureIndex >= SelectedProgram.Measure.Count)
        {
            return;
        }

        var measure = SelectedProgram.Measure.ToArray();
        measure[_selectedMeasureIndex] = measure[_selectedMeasureIndex] switch
        {
            MetricNode metric => new MetricNode(mutate(metric.Metric)),
            RepeatNode repeat => repeat with { Children = MutateFirstMetric(repeat.Children, mutate) },
            var other => other,
        };
        ReplaceSelected(SelectedProgram with { Measure = measure });
    }

    private static IReadOnlyList<MeasureNode> MutateFirstMetric(
        IReadOnlyList<MeasureNode> children,
        Func<MetricDraft, MetricDraft> mutate)
    {
        var copy = children.ToArray();
        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i] is MetricNode metric)
            {
                copy[i] = new MetricNode(mutate(metric.Metric));
                break;
            }
        }

        return copy;
    }

    private void UpdateLimits(double? low, double? high, double? threshold)
    {
        UpdateSelectedMetric(metric =>
        {
            LimitSpec? limits = low is null && high is null && threshold is null
                ? null
                : new LimitSpec(low, high, threshold);
            return metric with { Limits = limits };
        });
    }

    private void RaiseEditorProperties()
    {
        OnPropertyChanged(nameof(SelectedMeasure));
        OnPropertyChanged(nameof(SelectedMetric));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(PreviewKind));
        OnPropertyChanged(nameof(ChannelKey));
        OnPropertyChanged(nameof(DisplayRole));
        OnPropertyChanged(nameof(YUnit));
        OnPropertyChanged(nameof(LimitLow));
        OnPropertyChanged(nameof(LimitHigh));
        OnPropertyChanged(nameof(Threshold));
        OnPropertyChanged(nameof(FormulaSource));
        OnPropertyChanged(nameof(FormulaError));
        OnPropertyChanged(nameof(VisaAddress));
        OnPropertyChanged(nameof(RawTypeName));
        OnPropertyChanged(nameof(RawXml));
        OnPropertyChanged(nameof(MeasureHint));
    }

    private static string FormatLimit(double? value)
        => value is { } number ? number.ToString(CultureInfo.InvariantCulture) : string.Empty;

    private static double? ParseLimit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return double.Parse(value, CultureInfo.InvariantCulture);
    }
}
