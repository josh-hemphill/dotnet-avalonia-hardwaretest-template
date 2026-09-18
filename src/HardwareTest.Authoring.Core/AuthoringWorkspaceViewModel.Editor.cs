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
        set
        {
            if (SetField(ref _selectedRecipeId, value))
            {
                OnPropertyChanged(nameof(SelectedRecipe));
            }
        }
    }

    public AuthoringRecipe? SelectedRecipe
    {
        get => Recipes.FirstOrDefault(recipe =>
            string.Equals(recipe.Id, _selectedRecipeId, StringComparison.OrdinalIgnoreCase));
        set => SelectedRecipeId = value?.Id;
    }

    public MeasureNode? SelectedMeasure
    {
        get
        {
            if (SelectedProgram is null)
            {
                return null;
            }

            if (SelectedSequence is { Section: SequenceSection.Measure } row)
            {
                return AuthoringSequence.ResolveMeasure(SelectedProgram, row.IndexPath);
            }

            return _selectedMeasureIndex < 0 || _selectedMeasureIndex >= SelectedProgram.Measure.Count
                ? null
                : SelectedProgram.Measure[_selectedMeasureIndex];
        }
    }

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
            var recorded = SelectedDataset is { } dataset
                ? RunDatasetBinder.SeriesByMetric(dataset.Run)
                : null;
            return MetricPreviewBuilder.From(SelectedMetric, siblings, recorded);
        }
    }

    public string PreviewKind => Preview.TileKind?.ToString() ?? "Text";

    public string PreviewNote
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Preview.Note))
            {
                return Preview.Note;
            }

            return SelectedDataset is null
                ? "Canned samples (not Execute)."
                : "Recording samples (not Execute).";
        }
    }

    public string ChannelKey
    {
        get => SelectedMetric?.ChannelKey ?? string.Empty;
        set
        {
            if (string.Equals(ChannelKey, value, StringComparison.Ordinal))
            {
                return;
            }

            UpdateSelectedMetric(metric => metric with { ChannelKey = value });
        }
    }

    public string DisplayRole
    {
        get => SelectedMetric?.DisplayRole ?? string.Empty;
        set
        {
            if (string.Equals(DisplayRole, value, StringComparison.Ordinal))
            {
                return;
            }

            UpdateSelectedMetric(metric => metric with { DisplayRole = value });
        }
    }

    public string YUnit
    {
        get => SelectedMetric?.YUnit ?? string.Empty;
        set
        {
            if (string.Equals(YUnit, value, StringComparison.Ordinal))
            {
                return;
            }

            UpdateSelectedMetric(metric => metric with { YUnit = value });
        }
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
        get => HasFormula && SelectedMetric?.Source is ExpressionAlgorithm expr ? expr.Source : string.Empty;
        set
        {
            if (!HasFormula || string.Equals(FormulaSource, value, StringComparison.Ordinal))
            {
                return;
            }

            UpdateSelectedMetric(metric =>
            {
                if (metric.Source is not ExpressionAlgorithm existing)
                {
                    return metric;
                }

                var keys = existing.InputChannelKeys;
                try
                {
                    var ast = FormulaParser.Parse(value);
                    keys = FormulaExprWalk.Identifiers(ast.Root)
                        .Where(name => !string.Equals(name, metric.ChannelKey, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                }
                catch (AuthoringWorkspaceException)
                {
                    // Keep previous keys while the formula is still incomplete.
                }

                return metric with { Source = existing with { InputChannelKeys = keys, Source = value } };
            });
        }
    }

    public string FormulaError
    {
        get
        {
            if (!HasFormula || SelectedMetric?.Source is not ExpressionAlgorithm expr)
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

    public string TfNumerator
    {
        get => FormatVector(SelectedTf?.Numerator);
        set => UpdateSelectedTf(tf => tf with { Numerator = ParseVector(value, tf.Numerator) });
    }

    public string TfDenominator
    {
        get => FormatVector(SelectedTf?.Denominator);
        set => UpdateSelectedTf(tf => tf with { Denominator = ParseVector(value, tf.Denominator) });
    }

    public string TfTsSeconds
    {
        get => SelectedTf is { } tf ? tf.TsSeconds.ToString(CultureInfo.InvariantCulture) : string.Empty;
        set
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ts) || ts <= 0)
            {
                return;
            }

            UpdateSelectedTf(tf => tf with { TsSeconds = ts });
        }
    }

    public string TfMethod
    {
        get => SelectedTf?.Method ?? string.Empty;
        set
        {
            var method = value.Trim().ToLowerInvariant();
            if (method is not ("filter" or "filtfilt"))
            {
                return;
            }

            UpdateSelectedTf(tf => tf with { Method = method });
        }
    }

    public string TfInputChannel
    {
        get => SelectedTf?.InputChannelKey ?? string.Empty;
        set => UpdateSelectedTf(tf => tf with { InputChannelKey = value.Trim() });
    }

    public void ImportTransferFunction(string path)
    {
        if (SelectedProgram is null)
        {
            throw new AuthoringWorkspaceException("Open a program before importing a transfer function.");
        }

        var imported = TfModelImporter.Load(path);
        if (HasTransferFunction)
        {
            UpdateSelectedMetric(metric => metric with
            {
                ChannelKey = imported.OutputChannelKey,
                DisplayRole = PresentationRoles.Timeseries,
                Source = imported.Algorithm,
            });
            return;
        }

        var created = new MetricDraft(
            "Transfer Function",
            imported.OutputChannelKey,
            PresentationRoles.Timeseries,
            "V",
            null,
            null,
            imported.Algorithm);
        ReplaceSelected(SelectedProgram with { Measure = [.. SelectedProgram.Measure, new MetricNode(created)] });
        SelectedMeasureIndex = SelectedProgram.Measure.Count - 1;
    }

    private TransferFunctionAlgorithm? SelectedTf
        => HasTransferFunction ? SelectedMetric?.Source as TransferFunctionAlgorithm : null;

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

    public string RawTypeName => HasRawStep && SelectedMeasure is RawStepNode raw ? raw.TypeName : string.Empty;

    public string RawXml => HasRawStep && SelectedMeasure is RawStepNode raw ? raw.XmlFragment : string.Empty;

    public void SelectMeasure(int index)
    {
        var count = SelectedProgram?.Measure.Count ?? 0;
        var clamped = count == 0 ? -1 : Math.Clamp(index, 0, count - 1);
        _selectedMeasureIndex = clamped;
        OnPropertyChanged(nameof(SelectedMeasureIndex));
        var sequenceIndex = AuthoringSequence.IndexOfTopLevelMeasure(_sequenceItems, clamped);
        if (sequenceIndex >= 0)
        {
            SelectSequence(sequenceIndex);
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
        RefreshSequencePresentation();
        OnPropertyChanged(nameof(MeasureHint));
        RefreshDatasets();
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
        if (SelectedProgram is null)
        {
            return;
        }

        var path = MeasureMutationPath();
        if (path.Count == 0 || SelectedMetric is null)
        {
            return;
        }

        var next = mutate(SelectedMetric);
        if (Equals(next, SelectedMetric))
        {
            return;
        }

        var measure = AuthoringSequence.MutateMeasure(
            SelectedProgram.Measure,
            path,
            node => node switch
            {
                MetricNode metric => new MetricNode(mutate(metric.Metric)),
                RepeatNode repeat => repeat with { Children = MutateFirstMetric(repeat.Children, mutate) },
                var other => other,
            });
        ReplaceSelected(SelectedProgram with { Measure = measure });
    }

    private IReadOnlyList<int> MeasureMutationPath()
    {
        if (SelectedSequence is { Section: SequenceSection.Measure, IndexPath.Count: > 0 } row)
        {
            return row.IndexPath;
        }

        return _selectedMeasureIndex < 0 || SelectedProgram is null
            || _selectedMeasureIndex >= SelectedProgram.Measure.Count
            ? []
            : [_selectedMeasureIndex];
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
        OnPropertyChanged(nameof(PreviewNote));
        OnPropertyChanged(nameof(ChannelKey));
        OnPropertyChanged(nameof(DisplayRole));
        OnPropertyChanged(nameof(YUnit));
        OnPropertyChanged(nameof(LimitLow));
        OnPropertyChanged(nameof(LimitHigh));
        OnPropertyChanged(nameof(Threshold));
        OnPropertyChanged(nameof(FormulaSource));
        OnPropertyChanged(nameof(FormulaError));
        OnPropertyChanged(nameof(TfNumerator));
        OnPropertyChanged(nameof(TfDenominator));
        OnPropertyChanged(nameof(TfTsSeconds));
        OnPropertyChanged(nameof(TfMethod));
        OnPropertyChanged(nameof(TfInputChannel));
        OnPropertyChanged(nameof(VisaAddress));
        OnPropertyChanged(nameof(RawTypeName));
        OnPropertyChanged(nameof(RawXml));
        OnPropertyChanged(nameof(MeasureHint));
        OnPropertyChanged(nameof(SelectedSequence));
        OnPropertyChanged(nameof(SelectedRepeat));
        OnPropertyChanged(nameof(SelectedSetup));
        OnPropertyChanged(nameof(RepeatCount));
        OnPropertyChanged(nameof(InspectorBreadcrumb));
        OnPropertyChanged(nameof(HasFormula));
        OnPropertyChanged(nameof(HasTransferFunction));
        OnPropertyChanged(nameof(HasRawStep));
        OnPropertyChanged(nameof(HasRawStepEditor));
        OnPropertyChanged(nameof(FormulaPrefixCompletions));
        OnPropertyChanged(nameof(HasFormulaPrefixCompletions));
        OnPropertyChanged(nameof(HasMetricPresentation));
        OnPropertyChanged(nameof(HasRepeatEditor));
        OnPropertyChanged(nameof(HasSetupEditor));
        OnPropertyChanged(nameof(HasPromptSetup));
        OnPropertyChanged(nameof(HasInputSetup));
        OnPropertyChanged(nameof(HasIdentitySetup));
        OnPropertyChanged(nameof(HasCleanupEditor));
        OnPropertyChanged(nameof(ShowThreshold));
        OnPropertyChanged(nameof(ShowBandLimits));
        OnPropertyChanged(nameof(FormulaSaveNote));
        OnPropertyChanged(nameof(ChannelKeys));
        OnPropertyChanged(nameof(FormulaCompletions));
        OnPropertyChanged(nameof(InstrumentSlots));
        OnPropertyChanged(nameof(Instruments));
        OnPropertyChanged(nameof(PromptMessage));
        OnPropertyChanged(nameof(InputTitle));
        OnPropertyChanged(nameof(InputMessage));
        OnPropertyChanged(nameof(InputStringFieldId));
        OnPropertyChanged(nameof(InputNumberFieldId));
        OnPropertyChanged(nameof(SetupInstrumentSlot));
        OnPropertyChanged(nameof(CleanupInstrumentSlot));
        OnPropertyChanged(nameof(IncludeSafeShutdown));
        OnPropertyChanged(nameof(MetricInstrumentSlot));
        OnPropertyChanged(nameof(SelectedRecipe));
        OnPropertyChanged(nameof(SelectedInstrumentVisa));
        OnPropertyChanged(nameof(SelectedInstrument));
        OnPropertyChanged(nameof(CanOfferLastWorkspace));
        OnPropertyChanged(nameof(LastWorkspacePath));
    }

    private void UpdateSelectedTf(Func<TransferFunctionAlgorithm, TransferFunctionAlgorithm> mutate)
    {
        if (!HasTransferFunction)
        {
            return;
        }

        UpdateSelectedMetric(metric =>
        {
            if (metric.Source is not TransferFunctionAlgorithm tf)
            {
                return metric;
            }

            return metric with { Source = mutate(tf) };
        });
    }

    private static string FormatVector(IReadOnlyList<double>? values)
        => values is null || values.Count == 0
            ? string.Empty
            : string.Join(" ", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));

    private static IReadOnlyList<double> ParseVector(string? value, IReadOnlyList<double> fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var parts = value.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return fallback;
        }

        return parts.Select(part => double.Parse(part, CultureInfo.InvariantCulture)).ToArray();
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
