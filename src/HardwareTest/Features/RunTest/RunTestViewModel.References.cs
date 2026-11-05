using System.Globalization;
using HardwareTest.Core.BenchReferences;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

public partial class RunTestViewModel
{
    internal Func<string> ExecutionEngineIdentityProvider { get; set; } = () => OpenTapBuildInfo.ExecutionEngineIdentity;
    private IOpenTapStationSession _referenceStation = null!;
    private (string Plan, string Sidecar)? _loadedReferenceArtifacts;
    public BenchReferencePanelViewModel References { get; private set; } = null!;
    public bool HasBenchReferences => References.HasRules;
    public string ReferenceReadinessSummary => References.IsReady && !References.IsAcquisition
        ? References.SelectionSummary : References.ReadinessText;
    public string ReferenceRunLabel => References.IsAcquisition ? "Acquire reference" : "Run";
    private string _referenceOutcomeSummary = string.Empty;
    private string _referenceOutcomeDetails = string.Empty;
    public string ReferenceOutcomeSummary => _referenceOutcomeSummary;
    public string ReferenceOutcomeDetails => _referenceOutcomeDetails;
    public bool HasReferenceOutcome => !string.IsNullOrWhiteSpace(_referenceOutcomeSummary);
    internal void SetReferenceOutcome(BenchReferenceRunSnapshot? snapshot, IReadOnlyList<BenchReferenceEvaluation> evaluations, HardwareTest.Core.Runs.RunResult backend)
    {
        _referenceOutcomeDetails = snapshot is null || (!snapshot.IsAcquisition && snapshot.Rules.Count == 0) ? string.Empty : snapshot.IsAcquisition
            ? $"Reference acquisition: {backend}; reference acceptance unevaluated — not production acceptance."
            : evaluations.Count == 0 ? "Reference acceptance was not completed."
            : $"Reference acceptance: {evaluations.Count(evaluation => evaluation.Passed)}/{evaluations.Count} passed. "
                + (evaluations.FirstOrDefault(evaluation => !evaluation.Passed)?.Message ?? evaluations[0].Message);
        _referenceOutcomeSummary = string.IsNullOrWhiteSpace(_referenceOutcomeDetails) ? string.Empty
            : snapshot!.IsAcquisition ? "Last run: acquisition only — not a production pass."
            : evaluations.Count == 0 ? "Last run: reference acceptance was not completed."
            : $"Last run: reference acceptance {(evaluations.All(evaluation => evaluation.Passed) ? "Passed" : "Failed")} ({evaluations.Count(evaluation => evaluation.Passed)}/{evaluations.Count} passed).";
        this.RaisePropertyChanged(nameof(ReferenceOutcomeSummary));
        this.RaisePropertyChanged(nameof(ReferenceOutcomeDetails));
        this.RaisePropertyChanged(nameof(HasReferenceOutcome));
    }

    private void InitializeBenchReferences(IBenchReferenceStore? store, IOpenTapStationSession station, ISettingsStore? settingsStore)
    {
        _referenceStation = station;
        References = new BenchReferencePanelViewModel(store, _settings, settingsStore, _clock,
            () => IsRunning || _runControl.IsRunning, () => IsEngineerDebugMode, RunOnUiAsync,
            () => SessionPanel.RequireContextConfirmation());
        References.StateChanged += (_, _) =>
        {
            RaiseStartGates();
            this.RaisePropertyChanged(nameof(HasBenchReferences));
            this.RaisePropertyChanged(nameof(ReferenceReadinessSummary));
            this.RaisePropertyChanged(nameof(ReferenceRunLabel));
        };
    }

    private static (string Plan, string Sidecar)? ReadReferenceArtifactHashes(ProgramItemViewModel program)
    {
        if (program.LoadKind != ProgramLoadKind.TapPlanFile) return null;
        BenchProgramHandoff.ValidateProgram(program.Path);
        var sidecar = System.IO.Path.ChangeExtension(program.Path, ".program.json");
        var hashes = (Plan: BenchReferenceIdentity.Sha256(System.IO.File.ReadAllBytes(program.Path)),
            Sidecar: System.IO.File.Exists(sidecar) ? BenchReferenceIdentity.Sha256(System.IO.File.ReadAllBytes(sidecar)) : string.Empty);
        if (program.PlanSha256 is not null && (hashes.Plan != program.PlanSha256 || hashes.Sidecar != (program.SidecarSha256 ?? string.Empty)))
            throw new InvalidOperationException("The plan or sidecar changed since program discovery. Reload programs before Run so the correct requirements and reference declarations are loaded.");
        return hashes;
    }

    private BenchReferenceContext CreateReferenceContext(ProgramItemViewModel program)
    {
        var artifacts = ReadReferenceArtifactHashes(program);
        if (artifacts is null || _loadedReferenceArtifacts is null || artifacts != _loadedReferenceArtifacts)
            throw new InvalidOperationException("The compiled program changed or its reference identity is unavailable. Reload the complete plan and sidecar before Run.");
        var mode = (_visaModeController?.EffectiveUseMockVisa ?? _settings.UseMockVisa)
            ? BenchReferenceVisaModes.Simulated : BenchReferenceVisaModes.Physical;
        var values = new List<KeyValuePair<string, string>>
        {
            new("visa-mode", mode),
            new("engine-build", ExecutionEngineIdentityProvider()),
        };
        var station = StationOverrides.BuildStationProfile();
        foreach (var group in _referenceStation.InstrumentSlots.GroupBy(slot => slot.Name.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
                throw new ReferenceConfigurationException("An instrument slot has no stable name. Correct the authored hardware definition, compile and reload the complete program.");
            var effective = group.Select(slot =>
            {
                var resource = station.SlotToResource.TryGetValue(slot.Name, out var replacement) ? replacement : slot.ResourceName;
                return (Type: slot.TypeName?.Trim() ?? string.Empty, Resource: resource?.Trim() ?? string.Empty);
            }).Distinct().ToArray();
            if (effective.Length != 1)
                throw new ReferenceConfigurationException($"Instrument slot '{group.Key}' has conflicting types or effective resources across the program. Correct the authored slot or apply one consistent bench binding, then reload the program.");
            // OpenTAP serializes the same logical resource beside identity,
            // acquisition and cleanup steps. Hash its effective binding once.
            var key = group.Key.ToLowerInvariant();
            values.Add(new($"slot:{key}:type", effective[0].Type));
            values.Add(new($"slot:{key}:resource", effective[0].Resource));
        }
        foreach (var parameter in _settings.PlanParameterOverrides.Where(item => item.PlanId.Equals(program.Id, StringComparison.OrdinalIgnoreCase)))
            values.Add(new($"parameter:{parameter.MemberKey}", parameter.Value ?? string.Empty));
        // New reference programs explicitly refuse temporary overlays. Persisted
        // station parameters above still apply and require requalification.
        values.Add(new("temporary-overlay", "not-applied-to-reference-program"));
        return new BenchReferenceContext
        {
            PlanId = program.Id,
            PlanSha256 = artifacts.Value.Plan,
            SidecarSha256 = artifacts.Value.Sidecar,
            ConfigurationSha256 = BenchReferenceIdentity.ConfigurationHash(values),
            VisaMode = mode,
        };
    }

    private sealed class ReferenceConfigurationException(string message) : InvalidOperationException(message);

    public async Task RefreshBenchReferencesAsync()
    {
        var program = ProgramSelection.SelectedProgram;
        if (program is null || program.ReferenceRules.Count == 0)
        {
            await RunOnUiAsync(() => References.Configure(program, null)).ConfigureAwait(false);
            return;
        }
        var context = CreateReferenceContext(program);
        await RunOnUiAsync(() => References.Configure(program, context)).ConfigureAwait(false);
        await References.RefreshAsync().ConfigureAwait(false);
    }

    internal IReadOnlyList<BenchReferenceRule> SelectedReferenceRules()
    {
        if (ProgramSelection.SelectedProgram is not { } program || StepTree.SelectedStep is not { } selected) return [];
        var ids = new HashSet<Guid>();
        void Add(HierarchyStepViewModel node)
        {
            if (Guid.TryParse(node.Id, out var id)) ids.Add(id);
            foreach (var child in node.Children) Add(child);
        }
        Add(selected);
        return program.ReferenceRules.Where(rule => ids.Contains(rule.ProducerStepId)).ToArray();
    }

    internal async Task<BenchReferenceRunSnapshot?> FreezeBenchReferencesAsync(ProgramItemViewModel program, bool selectionOnly)
    {
        if (program.ReferenceRules.Count == 0 && string.IsNullOrWhiteSpace(program.ReferenceDeclarationError)) return null;
        return await References.FreezeAsync(CreateReferenceContext(program), selectionOnly ? SelectedReferenceRules() : null).ConfigureAwait(false);
    }
}
