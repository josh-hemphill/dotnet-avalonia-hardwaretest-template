using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Tests.Fixtures;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Xunit;

namespace HardwareTest.Tests.Reporting;

public sealed class BenchReferenceReportTests
{
    [Theory]
    [InlineData(ReportKinds.Status)]
    [InlineData(ReportKinds.Certification)]
    public async Task Actual_pdf_preserves_frozen_reference_identity_numbers_source_and_acceptance(string kind)
    {
        using var temp = new TempDataDirectory();
        var (source, profile) = Reference();
        var use = BenchReferenceEvaluator.Freeze(source.ReferenceRules.Single(), profile, source.ReferenceContext!, DateTimeOffset.UtcNow);
        var record = new TestRunRecord
        {
            RunId = Guid.NewGuid().ToString("N"),
            PlanId = source.PlanId,
            PlanName = "Reference-dependent acceptance",
            DutSerial = "PRODUCTION-UNIT",
            OperatorName = "Bench operator",
            ExecutionScope = RunExecutionScopes.FullSuite,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow.AddSeconds(1),
            ReferenceBackendResult = RunResult.Passed,
            Result = RunResult.Failed,
            ReferenceContext = source.ReferenceContext,
            ReferenceRules = [use.Rule],
            ReferenceUses = [use],
            Samples = [new StoredSample { MetricKey = "voltage", Channel = "voltage", ProducerStepId = use.Rule.ProducerStepId, Unit = "V", Value = 5 }],
        };
        record.ReferenceEvaluations = BenchReferenceEvaluator.Evaluate(record.Samples, record.ReferenceUses).ToList();
        Assert.False(Assert.Single(record.ReferenceEvaluations).Passed);
        // Later catalog/object edits must not change the run-owned reference.
        profile.Values[0] = profile.Values[0] with { Value = 100 };
        var store = new FileRunStore(temp.RunsDirectory);
        await store.SaveAsync(record);
        using var reports = new TypstReportService(store, new AppSettings { EmbedPlotsInReport = false });
        var bytes = await reports.CompileReportAsync(record, kind);
        var text = Text(bytes);
        Assert.Contains("Reference acceptance: Failed", text, StringComparison.Ordinal);
        Assert.Contains(use.ReferenceId, text, StringComparison.Ordinal);
        Assert.Contains(use.ReferenceSha256, text, StringComparison.Ordinal);
        Assert.Contains("GOLDEN-DUT-42", text, StringComparison.Ordinal);
        Assert.Contains(source.RunId, text, StringComparison.Ordinal);
        Assert.Contains("Value used: 2 V", text, StringComparison.Ordinal);
        Assert.Contains("arithmetic-mean", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CERTIFIED PASS", text, StringComparison.Ordinal);
        var saved = await store.LoadAsync(record.RunId);
        Assert.Equal(2, Assert.Single(saved!.ReferenceUses).ReferenceValue.Value);
        Assert.Equal(RunResult.Failed, saved.Result);
    }

    [Theory]
    [InlineData(ReportKinds.Status)]
    [InlineData(ReportKinds.Certification)]
    public async Task Acquisition_pdf_never_claims_production_or_certified_pass(string kind)
    {
        using var temp = new TempDataDirectory();
        var (source, _) = Reference();
        var store = new FileRunStore(temp.RunsDirectory);
        using var reports = new TypstReportService(store, new AppSettings { EmbedPlotsInReport = false });
        var text = Text(await reports.CompileReportAsync(source, kind));
        Assert.Contains("REFERENCE ACQUISITION", text, StringComparison.Ordinal);
        Assert.Contains("not production acceptance", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CERTIFIED PASS", text, StringComparison.Ordinal);
    }

    private static (TestRunRecord Source, BenchReferenceProfile Profile) Reference()
    {
        var now = DateTimeOffset.UtcNow;
        var rule = new BenchReferenceRule
        {
            RuleId = "voltage-reference",
            ReferenceKey = "golden.voltage",
            MetricKey = "voltage",
            ProducerStepId = Guid.NewGuid(),
            Unit = "V",
            Tolerance = 0.25,
        };
        var context = new BenchReferenceContext
        {
            PlanId = "reference-voltage",
            PlanSha256 = new string('a', 64),
            SidecarSha256 = new string('b', 64),
            ConfigurationSha256 = new string('c', 64),
            VisaMode = BenchReferenceVisaModes.Simulated,
        };
        var source = new TestRunRecord
        {
            RunId = Guid.NewGuid().ToString("N"),
            PlanId = context.PlanId,
            PlanName = "Golden acquisition",
            DutSerial = "GOLDEN-DUT-42",
            OperatorName = "Reference technician",
            ExecutionScope = RunExecutionScopes.ReferenceAcquisition,
            Result = RunResult.Passed,
            ReferenceBackendResult = RunResult.Passed,
            ReferenceContext = context,
            ReferenceRules = [rule],
            StartedAt = now.AddSeconds(-2),
            CompletedAt = now.AddSeconds(-1),
            Samples = [new StoredSample { MetricKey = rule.MetricKey, Channel = rule.MetricKey, ProducerStepId = rule.ProducerStepId, Unit = rule.Unit, Value = 2, Timestamp = now.AddSeconds(-1.5) }],
        };
        return (source, BenchReferenceEvaluator.Capture("Golden voltage reference", source, [rule], now));
    }

    private static string Text(byte[] bytes)
    {
        using var pdf = new PdfDocument(new PdfReader(new MemoryStream(bytes)));
        return string.Join("\n", Enumerable.Range(1, pdf.GetNumberOfPages()).Select(page => PdfTextExtractor.GetTextFromPage(pdf.GetPage(page))));
    }
}
