using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class ChannelAverageRuntimeTests
{
    [Fact]
    public void Evaluator_uses_inclusive_threshold_and_handles_large_finite_values()
    {
        Assert.Equal(new ChannelAverageResult(2, true), ChannelAverageEvaluator.Evaluate([1, 2, 3], 2));
        Assert.False(ChannelAverageEvaluator.Evaluate([1, 2, 3], 2.1).Passed);
        Assert.Equal(double.MaxValue, ChannelAverageEvaluator.Evaluate(Enumerable.Repeat(double.MaxValue, 9).ToArray(), double.MaxValue).Average);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Evaluator_rejects_invalid_sample_or_threshold(double invalid)
    {
        Assert.Throws<InvalidOperationException>(() => ChannelAverageEvaluator.Evaluate([1, invalid], 1));
        Assert.Throws<InvalidOperationException>(() => ChannelAverageEvaluator.Evaluate([1], invalid));
    }

    [Fact]
    public void Evaluator_rejects_empty_samples()
        => Assert.Throws<InvalidOperationException>(() => ChannelAverageEvaluator.Evaluate([], 1));

    [Fact]
    public void Execution_uses_only_intended_producer_and_publishes_shared_result()
    {
        var producer = new SampleProducer { Values = [1, 2, 3] };
        var unrelated = new SampleProducer { Values = [100] };
        var average = AverageFor(producer);
        average.Unit = "V";
        var results = new ScalarListener();
        var run = PlanWith(producer, unrelated, average).Execute([results], []);
        Assert.Equal(Verdict.Pass, run.Verdict);
        Assert.Equal("V", Assert.Single(results.Units));
        Assert.Equal(ChannelAverageEvaluator.Evaluate(producer.Values, average.Threshold).Average, Assert.Single(results.Values));
    }

    [Fact]
    public void Successive_plan_runs_do_not_use_disabled_producer_previous_rows()
    {
        var producer = new SampleProducer { Values = [2] };
        var average = AverageFor(producer);
        var plan = PlanWith(producer, average);
        Assert.Equal(Verdict.Pass, plan.Execute().Verdict);
        producer.Enabled = false;
        Assert.Equal(Verdict.Fail, plan.Execute().Verdict);
        Assert.Equal(Verdict.Fail, average.Verdict);
    }

    [Fact]
    public void Successive_iterations_use_each_new_series()
    {
        var producer = new SampleProducer { Values = [2], LaterValues = [4] };
        var average = AverageFor(producer);
        var loop = new RepeatLoopStep { Count = 2 };
        loop.ChildTestSteps.Add(producer);
        loop.ChildTestSteps.Add(average);
        var results = new ScalarListener();
        Assert.Equal(Verdict.Pass, PlanWith(loop).Execute([results], []).Verdict);
        Assert.Equal([2d, 4d], results.Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_producer_in_later_iteration_fails_without_prior_rows(bool externalLoop)
    {
        var producer = new SampleProducer { Values = [2], DisableAfterRun = true };
        var average = AverageFor(producer);
        TestStep loop = externalLoop ? new ExternalRepeat() : new RepeatLoopStep { Count = 2 };
        loop.ChildTestSteps.Add(producer);
        loop.ChildTestSteps.Add(average);
        var results = new ScalarListener();
        Assert.Equal(Verdict.Fail, PlanWith(loop).Execute([results], []).Verdict);
        Assert.Equal(Verdict.Fail, average.Verdict);
        if (externalLoop) Assert.Empty(results.Values);
        else Assert.Equal(2, Assert.Single(results.Values));
    }

    [Fact]
    public void Unknown_loop_skipping_consumer_then_producer_cannot_reuse_old_samples()
    {
        var producer = new SampleProducer { Values = [2] };
        var average = AverageFor(producer);
        var loop = new AlternatingRepeat();
        loop.ChildTestSteps.Add(producer);
        loop.ChildTestSteps.Add(average);
        var results = new ScalarListener();
        Assert.Equal(Verdict.Fail, PlanWith(loop).Execute([results], []).Verdict);
        Assert.Equal(Verdict.Fail, average.Verdict);
        Assert.Empty(results.Values);
    }

    [Fact]
    public void Known_group_inside_basic_repeat_keeps_current_iteration_samples()
    {
        var producer = new SampleProducer { Values = [2], LaterValues = [4] };
        var average = AverageFor(producer);
        var group = new TestGroupStep();
        group.ChildTestSteps.Add(producer);
        group.ChildTestSteps.Add(average);
        var loop = new RepeatLoopStep { Count = 2 };
        loop.ChildTestSteps.Add(group);
        var results = new ScalarListener();
        Assert.Equal(Verdict.Pass, PlanWith(loop).Execute([results], []).Verdict);
        Assert.Equal([2d, 4d], results.Values);
    }

    [Fact]
    public void Transfer_function_does_not_reuse_prior_iteration_rows()
    {
        var producer = new SampleProducer { Values = [1, 2, 3], DisableAfterRun = true };
        var filter = new ApplyTransferFunctionStep { InputChannel = "VDC", Channel = "filtered", TsSeconds = 0.005 };
        var loop = new RepeatLoopStep { Count = 2 };
        loop.ChildTestSteps.Add(producer);
        loop.ChildTestSteps.Add(filter);
        Assert.Equal(Verdict.Fail, PlanWith(loop).Execute().Verdict);
        Assert.Equal(Verdict.Fail, filter.Verdict);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Runtime_invalid_samples_fail(double invalid)
    {
        var producer = new SampleProducer { Values = [1, invalid] };
        var average = AverageFor(producer);
        Assert.Equal(Verdict.Fail, PlanWith(producer, average).Execute().Verdict);
    }

    [Fact]
    public void Runtime_missing_or_nonfinite_threshold_fails()
    {
        var producer = new SampleProducer { Values = [2] };
        var average = AverageFor(producer);
        average.Threshold = double.NaN;
        Assert.Equal(Verdict.Fail, PlanWith(producer, average).Execute().Verdict);
    }

    [Fact]
    public void Legacy_mean_gte_keeps_fresh_instrument_reads()
    {
        var instrument = new CountingDmm();
        var acquire = new AcquireVoltageStep { Instrument = instrument, SampleCount = 3, IntervalMs = 0 };
        var legacy = new MeanGteStep { Instrument = instrument, SampleCount = 3, Threshold = 5 };
        var results = new ScalarListener();
        Assert.Equal(Verdict.Pass, PlanWith(acquire, legacy).Execute([results], []).Verdict);
        Assert.Equal(6, instrument.ReadCount);
        Assert.Equal(5, Assert.Single(results.Values));
    }

    private static ChannelAverageStep AverageFor(ITestStep producer)
        => new() { InputChannel = "VDC", ProducerStepId = producer.Id, Channel = "average", Threshold = 2 };

    private static TestPlan PlanWith(params ITestStep[] steps)
    {
        var plan = new TestPlan();
        foreach (var step in steps)
        {
            plan.ChildTestSteps.Add(step);
        }

        return plan;
    }

    private sealed class SampleProducer : TestStep
    {
        public double[] Values { get; set; } = [];
        public double[]? LaterValues { get; set; }
        public bool DisableAfterRun { get; set; }
        private int _runs;

        public override void Run()
        {
            var values = _runs++ > 0 ? LaterValues ?? Values : Values;
            for (var i = 0; i < values.Length; i++)
            {
                Results.Publish("Sample", new List<string> { "Channel", "Value", "ElapsedMs" }, "VDC", values[i], i * 5d);
            }

            if (DisableAfterRun)
            {
                Enabled = false;
            }

            UpgradeVerdict(Verdict.Pass);
        }
    }

    [AllowAnyChild]
    private sealed class ExternalRepeat : TestStep
    {
        public override void Run()
        {
            RunChildSteps();
            RunChildSteps();
        }
    }

    [AllowAnyChild]
    private sealed class AlternatingRepeat : TestStep
    {
        public override void Run()
        {
            ChildTestSteps[1].Enabled = false;
            RunChildSteps();
            ChildTestSteps[0].Enabled = false;
            ChildTestSteps[1].Enabled = true;
            RunChildSteps();
        }
    }

    private sealed class CountingDmm : HardwareDmm
    {
        public int ReadCount { get; private set; }
        public override string QueryIdn() => "counting";
        public override void ConfigureDcVolts() { }
        public override double ReadVoltage() => ++ReadCount;
        public override void OutputOff() { }
        public override void Reset() { }
    }

    private sealed class ScalarListener : ResultListener
    {
        public List<double> Values { get; } = [];
        public List<string> Units { get; } = [];
        public override void OnResultPublished(Guid stepRunId, ResultTable result)
        {
            if (result.Name != "Scalar")
            {
                return;
            }

            var column = result.Columns.Single(c => c.Name == "Value");
            var units = result.Columns.Single(c => c.Name == "Unit");
            foreach (var unit in units.Data)
            {
                Units.Add(Convert.ToString(unit) ?? string.Empty);
            }
            foreach (var value in column.Data)
            {
                Values.Add(Convert.ToDouble(value));
            }
        }
    }
}
