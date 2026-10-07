using System.ComponentModel;
using System.Xml.Serialization;
using OpenTap;

namespace HardwareTest.OpenTap.Plugins.Basic;

[Display("Channel Average", Groups: ["HardwareTest", "Analyze"],
    Description: "Pass if the average of this execution's intended producer samples meets the inclusive threshold.")]
public sealed class ChannelAverageStep : RuntimeAwareTestStep
{
    private readonly SampleTableCapture _capture = new();

    [Display("Input channel", Order: 1)]
    public string InputChannel { get; set; } = string.Empty;

    [Display("Producer step ID", Order: 2)]
    public Guid ProducerStepId { get; set; }

    [Display("Channel", Order: 3)]
    public string Channel { get; set; } = string.Empty;

    [Display("Unit", Order: 3.5)]
    public string Unit { get; set; } = string.Empty;

    [Display("Threshold", Order: 4)]
    public double Threshold { get; set; } = double.NaN;

    [Browsable(false)]
    [XmlIgnore]
    [AnnotationIgnore]
    public IResultSink SampleCapture => _capture;

    public override void Run()
    {
        WaitIfPaused();
        PlanRun?.WaitForResults();
        try
        {
            if (ProducerStepId == Guid.Empty || string.IsNullOrWhiteSpace(InputChannel))
            {
                throw new InvalidOperationException("CHANNEL_AVERAGE: requires an intended producer and input channel.");
            }

            var samples = _capture.RowsFor(InputChannel, StepRun, this, ProducerStepId).Select(row => row.Value).ToArray();
            var result = ChannelAverageEvaluator.Evaluate(samples, Threshold);
            Results.Publish("Analyze", new List<string> { "Mean", "Threshold" }, result.Average, Threshold);
            Results.Publish("Scalar", new List<string> { "Name", "Value", "Unit", "LimitLow" },
                string.IsNullOrWhiteSpace(Channel) ? Name : Channel, result.Average, Unit, Threshold);
            UpgradeVerdict(result.Passed ? Verdict.Pass : Verdict.Fail);
        }
        catch (InvalidOperationException ex)
        {
            Log.Error("{0}", ex.Message);
            UpgradeVerdict(Verdict.Fail);
        }
    }
}
