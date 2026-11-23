using System.Diagnostics;
using System.Globalization;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Examples.AdvancedProcedure;

[Display("Advanced Fixture Procedure", Groups: ["HardwareTest Examples"])]
[AllowChildrenOfType(typeof(FixtureSectionStep))]
public sealed partial class FixtureProcedureStep : RuntimeAwareTestStep, IProcedureSourceIdentity
{
    public SerialDutPort Serial { get; set; } = null!;
    public string TableDirectory { get; set; } = "tables/advanced-procedure";

    private readonly Dictionary<Guid, Verdict> _sectionVerdicts = [];

    internal ProcedureTables Tables { get; private set; } = null!;
    private string? _sourceHash;
    void IProcedureSourceIdentity.BindPlanSha256(string sha256) => _sourceHash = sha256;

    internal string? ActiveConfiguration { get; set; }
    internal bool SupplyEnabled { get; private set; }
    internal Route[] ActiveRoutes { get; private set; } = [];

    public override void Run()
    {
        var started = Stopwatch.StartNew();
        try
        {
            // Run Selected masks prerequisite siblings. Reject before any I/O or prompt can execute.
            if (Descendants(this).Any(step => !step.Enabled))
            {
                const string message = "Run the full procedure. Setup and DUT checks are required.";
                RecordEvent(Results, "OperatorGuidance", started, message);
                throw new InvalidOperationException(message);
            }
            try
            {
                ValidateSerial();
                Tables = ValidateConfiguration();
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or IOException)
            {
                RecordEvent(Results, "OperatorGuidance", started, error.Message);
                throw;
            }
            if (_sourceHash is { } hash) RecordEvent(Results, "procedure.source", started, $"Plan SHA-256 {hash}");
            RecordEvent(Results, "tables", started, $"{Tables.DescribeProfile()} / {Tables.FileHashes()}");

            _sectionVerdicts.Clear();
            foreach (FixtureSectionStep section in ChildTestSteps.Cast<FixtureSectionStep>())
            {
                TapThread.ThrowIfAborted();
                if (RecordBlockedPrerequisites(section.Name, section.PrerequisiteIds, "section", Results, started))
                {
                    _sectionVerdicts.Add(section.Id, Verdict.NotSet);
                    continue;
                }
                var run = RunChildStep(section, throwOnBreak: false);
                _sectionVerdicts.Add(section.Id, run.Verdict);
                if (run.Verdict is Verdict.Aborted or Verdict.Error) return;
                if (section.GatesRemainingSections && run.Verdict != Verdict.Pass)
                {
                    foreach (var withheld in ChildTestSteps.SkipWhile(step => step.Id != section.Id).Skip(1))
                        RecordEvent(Results, "fixture.section.blocked", started, $"{withheld.Name} requires {section.Name}");
                    return;
                }
            }
        }
        finally
        {
            ResetFixture();
            RecordEvent(Results, "fixture.reset", started, FixtureState());
        }
    }

    private void ValidateSerial()
    {
        if (Serial is null || Serial.EnablePhysicalPort || !string.Equals(Serial.Address, SerialDutPort.MockAddress, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(Serial.Name) || Serial.BaudRate <= 0 || Serial.TimeoutMs is < 1 or > 60000)
        {
            throw new InvalidOperationException("Use the simulation-only serial resource with a positive baud rate and reply timeout.");
        }
    }

    public ProcedureTables ValidateConfiguration()
    {
        if (Parent is not TestPlan plan || string.IsNullOrEmpty(plan.Directory))
            throw new InvalidOperationException("Load the saved fixture TapPlan and its tables first.");
        if (ChildTestSteps.Count < 3 || ChildTestSteps.Any(step => step is not FixtureSectionStep)
            || ChildTestSteps.Take(2).Any(step => step is FixtureSectionStep { GatesRemainingSections: false }))
        {
            throw new InvalidOperationException("Start with the station and DUT prerequisite sections followed by the remaining fixture sections.");
        }

        var stationChecks = Descendants(ChildTestSteps[0]).ToArray();
        var dutChecks = Descendants(ChildTestSteps[1]).ToArray();
        ProcedureTables.Require(stationChecks.Any(step => step is RailBringUpStep) && stationChecks.Any(step => step is HarnessResistanceStep)
            && !stationChecks.Any(step => step is DutCommunicationsStep or DutSignalsStep)
            && dutChecks.Any(step => step is DutCommunicationsStep) && dutChecks.Any(step => step is DutSignalsStep),
            "Run station rail and harness self-test before DUT connection and basic checks.");
        ValidateHandoffs(stationChecks, before: step => step is HarnessResistanceStep, enableSupply: false);
        ValidateHandoffs(dutChecks, before: step => step is DutCommunicationsStep or DutSignalsStep or IdentityCheckStep, enableSupply: true);
        var previous = new HashSet<Guid>();
        foreach (FixtureSectionStep section in ChildTestSteps.Cast<FixtureSectionStep>())
        {
            ValidatePrerequisites(section.Name, section.PrerequisiteIds, previous);
            foreach (var suite in Descendants(section).OfType<FixtureSuiteStep>())
                ValidatePrerequisites(suite.Name, suite.PrerequisiteIds, previous);
            _ = previous.Add(section.Id);
        }

        var tables = ProcedureTables.Load(Path.Combine(plan.Directory, TableDirectory));
        var measurements = Descendants(this).OfType<FixtureMeasurementStep>().ToArray();
        ProcedureTables.Require(measurements.Select(step => step.CaseId).Distinct().Count() == measurements.Length,
            "Each measurement step needs a unique CaseId to identify its reports.");
        foreach (var measurement in measurements)
            measurement.Validate(tables);

        foreach (var configurations in Descendants(this).OfType<FixtureConfigurationsStep>())
        {
            ProcedureTables.Require(configurations.ChildTestSteps.Count > 0
                && configurations.ChildTestSteps.All(step => !string.IsNullOrWhiteSpace(step.Name) && step.Name == step.Name.Trim())
                && configurations.ChildTestSteps.Select(step => step.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                == configurations.ChildTestSteps.Count, "Fixture configuration names must be unique.");
        }

        return tables;
    }

    private static void ValidateHandoffs(IEnumerable<ITestStep> steps, Func<ITestStep, bool> before, bool enableSupply)
    {
        var confirmed = false;
        foreach (var step in steps)
        {
            if (step is FixtureChangeStep handoff)
            {
                ProcedureTables.Require(!string.IsNullOrWhiteSpace(handoff.Message), $"'{handoff.Name}' needs operator instructions.");
                confirmed = handoff.EnableSupplyAfterConfirmation == enableSupply;
            }
            else if (step is RailBringUpStep)
            {
                confirmed = false;
            }

            if (before(step))
                ProcedureTables.Require(confirmed, $"'{step.Name}' needs the {(enableSupply ? "DUT connection" : "supplies-off harness self-test")} handoff before measurement.");
        }
    }

    private static void ValidatePrerequisites(string name, string requirement, IReadOnlySet<Guid> previous)
    {
        var prerequisites = RequiredSections(name, requirement);
        var unavailable = prerequisites.Where(id => !previous.Contains(id)).ToArray();
        ProcedureTables.Require(unavailable.Length == 0 && prerequisites.Distinct().Count() == prerequisites.Length,
            $"'{name}' must reference unique preceding prerequisites. Check missing, later, or cyclic IDs [{string.Join(", ", unavailable)}].");
    }

    private static Guid[] RequiredSections(string name, string requirements)
        => [.. requirements.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(value => Guid.TryParse(value, out var id) ? id : throw new InvalidDataException($"'{name}' has invalid prerequisite ID '{value}'."))];

    internal bool RecordBlockedPrerequisites(string name, string requirement, string target, ResultSource results, Stopwatch started)
    {
        var blocked = RequiredSections(name, requirement).Where(id => _sectionVerdicts.GetValueOrDefault(id) != Verdict.Pass).ToHashSet();
        if (blocked.Count == 0) return false;
        var names = ChildTestSteps.Where(step => blocked.Contains(step.Id)).Select(step => step.Name);
        RecordEvent(results, $"fixture.{target}.blocked", started, $"{name} requires {string.Join(", ", names)}");
        return true;
    }

    internal void EnableSupply() => SupplyEnabled = true;

    internal void SelectRoutes(CaseTable row) => ActiveRoutes = Tables.Routes(row);

    internal void ResetFixture()
    {
        SupplyEnabled = false;
        ActiveConfiguration = null;
        ActiveRoutes = [];
    }

    internal string FixtureState() => $"supplies {(SupplyEnabled ? "on" : "off")} / {ActiveRoutes.Length} active routes";

    internal static IEnumerable<ITestStep> Descendants(ITestStepParent parent)
        => parent.ChildTestSteps.SelectMany(step => Descendants(step).Prepend(step));

    internal static FixtureProcedureStep FindRoot(ITestStep step)
    {
        for (var parent = step.Parent; parent is ITestStep ancestor; parent = ancestor.Parent)
        {
            if (ancestor is FixtureProcedureStep procedure)
                return procedure;
        }

        throw new InvalidOperationException("Place this step within an Advanced Fixture Procedure.");
    }

    internal static void RecordEvent(ResultSource results, string name, Stopwatch started, string label)
        => results.Publish("Event", ["Name", "ElapsedMs", "Label", "TimestampUtc"],
            name, started.Elapsed.TotalMilliseconds, label, Timestamp(DateTimeOffset.UtcNow));

    internal static string Timestamp(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

[Display("Fixture Section", Groups: ["HardwareTest Examples"])]
[AllowAnyChild]
public sealed class FixtureSectionStep : RuntimeAwareTestStep
{
    [Display("Prerequisite", Description: "Stop the procedure if any suite in this section fails to pass.")]
    public bool GatesRemainingSections { get; set; }
    public string PrerequisiteIds { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;

    public override void Run()
    {
        WaitIfPaused();
        foreach (var suite in ChildTestSteps)
        {
            TapThread.ThrowIfAborted();
            var run = RunChildStep(suite, throwOnBreak: false);
            if (run.Verdict is Verdict.Aborted or Verdict.Error ||
                (GatesRemainingSections && run.Verdict != Verdict.Pass))
            {
                return;
            }
        }

        UpgradeVerdict(Verdict.Pass);
    }
}

[Display("Fixture Suite", Groups: ["HardwareTest Examples"])]
[AllowAnyChild]
public sealed class FixtureSuiteStep : RuntimeAwareTestStep
{
    public string Purpose { get; set; } = string.Empty;
    public string SetupNotes { get; set; } = string.Empty;
    public string PrerequisiteIds { get; set; } = string.Empty;

    public override void Run()
    {
        if (PrerequisiteIds.Length > 0 && FixtureProcedureStep.FindRoot(this)
            .RecordBlockedPrerequisites(Name, PrerequisiteIds, "suite", Results, Stopwatch.StartNew()))
        {
            UpgradeVerdict(Verdict.Inconclusive);
            return;
        }
        foreach (var step in ChildTestSteps)
        {
            WaitIfPaused();
            TapThread.ThrowIfAborted();
            var run = RunChildStep(step, throwOnBreak: false);
            if (run.Verdict != Verdict.Pass)
                return;
        }

        UpgradeVerdict(Verdict.Pass);
    }
}

[Display("Fixture Change", Groups: ["HardwareTest Examples"])]
public sealed class FixtureChangeStep : RuntimeAwareTestStep
{
    public string Message { get; set; } = string.Empty;
    public bool EnableSupplyAfterConfirmation { get; set; }
    internal OperatorInteractionRequest Interaction() => OperatorInteractionRequest.ConfirmOnly(Message, Name);

    public override void Run()
    {
        var root = FixtureProcedureStep.FindRoot(this);
        root.ResetFixture();
        var started = Stopwatch.StartNew();
        FixtureProcedureStep.RecordEvent(Results, "fixture.handoff", started, $"{Name} / {root.FixtureState()}");
        var response = RequestInteraction(Interaction());
        TapThread.ThrowIfAborted();
        if (response.Cancelled)
        {
            UpgradeVerdict(Verdict.Aborted);
            return;
        }

        if (EnableSupplyAfterConfirmation)
            root.EnableSupply();
        FixtureProcedureStep.RecordEvent(Results, "fixture.confirmed", started, Name);
        UpgradeVerdict(Verdict.Pass);
    }
}

[Display("Choose Fixture Configurations", Groups: ["HardwareTest Examples"])]
[AllowAnyChild]
public sealed class FixtureConfigurationsStep : RuntimeAwareTestStep
{
    public string Instructions { get; set; } = string.Empty;
    public bool OfferRetryOnFailure { get; set; }

    public static string DefaultInstructions => "Simulated supplies are off. Set the fixture to an available configuration, then select its name and continue.";

    internal OperatorInteractionRequest? Interaction(IReadOnlySet<string> complete, string error, IReadOnlySet<string>? blocked = null)
    {
        var eligible = ChildTestSteps.Select(step => step.Name).Where(name => blocked?.Contains(name) != true).ToArray();
        var choices = eligible.Where(name => !complete.Contains(name)).ToArray();
        if (choices.Length == 0) return null;
        var availability = blocked is { Count: > 0 } ? $"Blocked by prerequisites: {string.Join(", ", blocked)}. " : string.Empty;
        return new OperatorInteractionRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = $"Confirm fixture configuration · {eligible.Length - choices.Length} of {eligible.Length} completed",
            Message = error + availability + (string.IsNullOrWhiteSpace(Instructions) ? DefaultInstructions : Instructions),
            Fields = [new() { Id = "configuration", Label = "Fixture configuration", Required = true, Choices = choices }],
        };
    }

    public override void Run()
    {
        var root = FixtureProcedureStep.FindRoot(this);
        var remaining = ChildTestSteps.ToDictionary(step => step.Name, StringComparer.OrdinalIgnoreCase);
        var error = string.Empty;
        var started = Stopwatch.StartNew();
        var blocked = ChildTestSteps.OfType<FixtureSuiteStep>()
            .Where(suite => root.RecordBlockedPrerequisites(suite.Name, suite.PrerequisiteIds, "configuration", Results, started))
            .Select(suite => suite.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in blocked) _ = remaining.Remove(name);
        if (blocked.Count > 0) UpgradeVerdict(Verdict.Inconclusive);
        while (remaining.Count > 0)
        {
            WaitIfPaused();
            TapThread.ThrowIfAborted();
            root.ResetFixture();
            var complete = ChildTestSteps.Select(step => step.Name).Where(name => !remaining.ContainsKey(name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var request = Interaction(complete, error, blocked)!;
            var response = RequestInteraction(request);
            TapThread.ThrowIfAborted();
            if (response.Cancelled)
            {
                UpgradeVerdict(Verdict.Aborted);
                return;
            }

            if (!response.Values.TryGetValue("configuration", out var selected) || !remaining.Remove(selected.Trim(), out var suite))
            {
                error = "That configuration is unknown or already tested. ";
                continue;
            }

            error = string.Empty;
            FixtureProcedureStep.RecordEvent(Results, "fixture.configuration.started", started, suite.Name);
            root.EnableSupply();
            root.ActiveConfiguration = suite.Name;
            Verdict verdict;
            try
            {
                var run = RunChildStep(suite, throwOnBreak: false);
                verdict = run.Verdict;
                TapThread.ThrowIfAborted();
                if (run.Verdict is Verdict.Aborted or Verdict.Error)
                    return;
            }
            finally
            {
                root.ResetFixture();
            }
            if (verdict == Verdict.Fail && OfferRetryOnFailure)
            {
                var retry = RequestInteraction(new OperatorInteractionRequest
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = $"{suite.Name} failed",
                    Message = "Supplies are off. Inspect the physical configuration, then choose how to continue.",
                    Fields = [new() { Id = "retry", Label = "Next action", Required = true, Choices = ["Retry configuration", "Record failure and continue"] }],
                });
                TapThread.ThrowIfAborted();
                if (retry.Cancelled) { UpgradeVerdict(Verdict.Aborted); return; }
                if (retry.Values.GetValueOrDefault("retry") == "Retry configuration")
                {
                    remaining.Add(suite.Name, suite);
                    FixtureProcedureStep.RecordEvent(Results, "fixture.configuration.retry", started, suite.Name);
                    continue;
                }
            }
            FixtureProcedureStep.RecordEvent(Results, "fixture.configuration", started, suite.Name);
        }

        UpgradeVerdict(Verdict.Pass);
    }
}

[Display("Fixture Safe Shutdown", Groups: ["HardwareTest Examples"])]
public sealed class FixtureShutdownStep : RuntimeAwareTestStep, ISafeShutdownStep
{
    public override void Run()
    {
        FixtureProcedureStep.FindRoot(this).ResetFixture();
        UpgradeVerdict(Verdict.Pass);
    }
}
