using System.ComponentModel;
using System.Xml.Serialization;
using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

/// Native IIR analyze step. Reads sibling Sample rows; never drops or coerces elapsed to 0.
[Display(
    "Apply Transfer Function",
    Groups: ["HardwareTest", "Analyze"],
    Description: "Apply a discrete SISO LTI filter (Direct Form II transposed) on a sibling Sample channel.")]
public sealed class ApplyTransferFunctionStep : RuntimeAwareTestStep
{
    private readonly SampleTableCapture _capture = new();

    public ApplyTransferFunctionStep()
    {
        SampleCapture = _capture;
    }

    [Display("Input channel", Order: 1)]
    public string InputChannel { get; set; } = string.Empty;

    [Display("Channel", Order: 1.5, Description: "Output ChannelKey for Presentation.")]
    public string Channel { get; set; } = string.Empty;

    [Display("Numerator", Order: 2)]
    public double[] Numerator { get; set; } = [1];

    [Display("Denominator", Order: 3)]
    public double[] Denominator { get; set; } = [1];

    [Display("Ts seconds", Order: 4)]
    public double TsSeconds { get; set; }

    [Display("Method", Order: 5, Description: "filter or filtfilt (lowercase).")]
    public string Method { get; set; } = "filter";

    /// OpenTAP auto-wires public IResultSink members via ResultSinkListener before listeners are sealed.
    [Browsable(false)]
    [XmlIgnore]
    [AnnotationIgnore]
    public IResultSink SampleCapture { get; }

    public override void Run()
    {
        WaitIfPaused();
        PlanRun?.WaitForResults();
        try
        {
            var rows = _capture.RowsFor(InputChannel);
            var values = rows.Select(r => r.Value).ToArray();
            var elapsed = rows.Select(r => r.ElapsedMs).ToArray();
            var filtered = ApplyToSeries(values, elapsed);
            for (var i = 0; i < filtered.Length; i++)
            {
                Results.Publish(
                    "Sample",
                    new List<string> { "Channel", "Index", "Value", "LimitLow", "LimitHigh", "ElapsedMs" },
                    string.IsNullOrWhiteSpace(Channel) ? Name : Channel,
                    i,
                    filtered[i].Value,
                    double.NaN,
                    double.NaN,
                    filtered[i].ElapsedMs);
            }

            UpgradeVerdict(Verdict.Pass);
        }
        catch (InvalidOperationException ex)
        {
            Log.Error("{0}", ex.Message);
            UpgradeVerdict(Verdict.Fail);
        }
    }

    /// Applies the filter after RequireUniform. Missing elapsed is NaN at that index (same length).
    public (double Value, double ElapsedMs)[] ApplyToSeries(
        IReadOnlyList<double> values,
        IReadOnlyList<double> elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(elapsedMs);
        if (values.Count != elapsedMs.Count)
        {
            throw new InvalidOperationException("TF_GRID: value and elapsedMs length mismatch.");
        }

        TransferFunctionGrid.RequireUniform(elapsedMs, TsSeconds);
        var y = Dispatch(Method, Numerator, Denominator, values);
        var result = new (double Value, double ElapsedMs)[y.Length];
        for (var i = 0; i < y.Length; i++)
        {
            result[i] = (y[i], elapsedMs[i]);
        }

        return result;
    }

    private static double[] Dispatch(
        string method,
        IReadOnlyList<double> numerator,
        IReadOnlyList<double> denominator,
        IReadOnlyList<double> values)
    {
        if (string.Equals(method, "filtfilt", StringComparison.Ordinal))
        {
            return TransferFunctionFilter.FiltFilt(numerator, denominator, values);
        }

        if (string.Equals(method, "filter", StringComparison.Ordinal))
        {
            return TransferFunctionFilter.Filter(numerator, denominator, values);
        }

        throw new InvalidOperationException(
            "TF_METHOD: method must be lowercase filter or filtfilt.");
    }
}
