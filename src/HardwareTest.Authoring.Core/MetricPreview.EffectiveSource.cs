using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.Authoring;

public static partial class MetricPreviewBuilder
{
    // Concrete factories apply the compiler's defaults, case handling and enumeration precedence.
    private static MetricDraft EffectiveMetric(MetricDraft metric)
    {
        PlanCompiler.RequirePreviewEnabled(metric);
        var id = metric.Source switch { MeasureSource m => m.FunctionId, AlgorithmSource a => a.AlgorithmId, _ => string.Empty };
        if (id is not (AuthoringFunctionIds.BasicChannelAverage or AuthoringFunctionIds.BasicApplyTransferFunction)) return metric;
        var step = PlanCompiler.PreviewStep(metric);
        return step switch
        {
            ChannelAverageStep average => metric with { Source = new AlgorithmSource(AuthoringFunctionIds.BasicChannelAverage, [average.InputChannel], new Dictionary<string, string>()) },
            ApplyTransferFunctionStep filter => metric with { Source = new TransferFunctionAlgorithm(filter.InputChannel, filter.Numerator, filter.Denominator, filter.TsSeconds, filter.Method) },
            _ => metric,
        };
    }
}
