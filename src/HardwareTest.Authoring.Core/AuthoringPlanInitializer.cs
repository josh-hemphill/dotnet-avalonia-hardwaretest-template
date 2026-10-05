using System.Globalization;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

/// Constructs one normal source draft; durable creation publishes only authoring JSON.
public sealed class AuthoringPlanInitializer
{
    private readonly AuthoringAtomicWriter? _writer;
    public AuthoringPlanInitializer(AuthoringAtomicWriter? writer = null) => _writer = writer;

    public PlanInitializationResult Construct(PlanInitializationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = request.PlanId.Trim();
        AuthoringDocumentStore.ValidateId(id);
        if (request.ExistingPlanIds.Any(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)))
            throw new AuthoringWorkspaceException($"Program '{id}' already exists; choose a different ID.");
        var name = request.DisplayName?.Trim() ?? id;
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl))
            throw new ArgumentException("Enter a display name without control characters.");
        if (string.IsNullOrWhiteSpace(request.DeviceFamily) || request.DeviceFamily.Any(char.IsControl))
            throw new ArgumentException("Enter a device family without control characters.");
        var destination = ValidateDestination(request, id);
        if (!Enum.IsDefined(request.StartingPoint)) throw new ArgumentException("Choose a supported starting point.");
        var chosenInstruments = request.UseTemplateHardware && request.StartingPoint == PlanStartingPoint.DemoVoltageTask && request.Instruments.Count == 0
            ? new[] { new InstrumentRef("DMM", AuthoringInstrumentCatalog.All.Single(adapter => adapter.DisplayName == "Mock DMM").TypeId, "MOCK::INSTR0") }
            : request.Instruments;
        var instruments = chosenInstruments.Select(instrument => instrument with
        {
            SlotName = instrument.SlotName.Trim(),
            Settings = new Dictionary<string, string>(instrument.Settings, StringComparer.Ordinal)
        }).ToArray();
        if (instruments.Any(instrument => string.IsNullOrWhiteSpace(instrument.SlotName))
            || instruments.Select(instrument => instrument.SlotName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != instruments.Length)
            throw new ArgumentException("Instrument slots must be nonempty and unique.");
        foreach (var instrument in instruments)
        {
            if (!AuthoringInstrumentCatalog.TryGet(instrument.TypeId, out var adapter))
                throw new ArgumentException($"Choose an explicitly supported instrument type for '{instrument.SlotName}'.");
            if (instrument.Settings.Keys.Any(key => !adapter.ConfigurationFields.Contains(key, StringComparer.Ordinal)))
                throw new ArgumentException($"Unsupported settings for '{instrument.SlotName}'.");
        }
        var setup = new List<SetupAction>();
        if (!string.IsNullOrWhiteSpace(request.IdentityInstrumentSlot)) setup.Add(new IdentitySetup(request.IdentityInstrumentSlot.Trim()));
        if (!string.IsNullOrWhiteSpace(request.FixtureConfirmation)) setup.Add(new OperatorPromptSetup("Fixture confirmation", request.FixtureConfirmation.Trim()));
        if (!string.IsNullOrWhiteSpace(request.FixtureInputField)) setup.Add(new OperatorInputSetup("Fixture input", "Fixture input", "Enter the fixture identifier.", request.FixtureInputField.Trim(), null));
        var sidecar = new ProgramSidecar
        {
            DisplayName = name,
            DutFamily = request.DeviceFamily.Trim(),
            RequireSerial = request.RequireSerial,
            ReportKinds = ["status"],
            DefaultReportKind = "status",
            SelectionIncludesCleanup = true
        };
        RequiredFieldIds.Apply(sidecar, request.RequiredOperatorFields.Concat(request.RequireSerial ? [RequiredFieldIds.Serial] : []).ToArray());
        var draft = new ProgramDraft(id, sidecar, instruments, setup, [],
            new CleanupPolicy(request.IncludeSafeShutdown && instruments.Length > 0,
                instruments.Where(instrument => AuthoringInstrumentCatalog.TryGet(instrument.TypeId, out var adapter) && adapter.SupportsShutdown).Select(instrument => instrument.SlotName).ToArray()));
        var measurement = request.Measurement;
        if (measurement is null && request.IncludeTemplateMeasurement && request.StartingPoint is PlanStartingPoint.VoltageTask or PlanStartingPoint.DemoVoltageTask)
            measurement = new(AuthoringRecipeIds.Acquire, instruments.FirstOrDefault()?.SlotName ?? string.Empty);
        if (measurement is not null)
        {
            if (measurement.RecipeId is not (AuthoringRecipeIds.Acquire or AuthoringRecipeIds.MeanGte))
                throw new ArgumentException("Choose the supported voltage acquisition or mean criterion task.");
            draft = AuthoringRecipeCatalog.Apply(draft, measurement.RecipeId, measurement.InstrumentSlot);
            var node = (MetricNode)draft.Measure.Single();
            var metric = node.Metric with { ChannelKey = measurement.ChannelKey.Trim(), YUnit = measurement.Unit.Trim(), Limits = measurement.Criterion ?? node.Metric.Limits };
            if (measurement.ThresholdText is { } thresholdText)
            {
                var valid = double.TryParse(thresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold) && double.IsFinite(threshold);
                metric = metric with { Limits = new LimitSpec(null, null, valid ? threshold : null) };
                if (!valid) draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(node.NodeId, "Threshold")] = thresholdText;
            }
            if (metric.Source is MeasureSource or AlgorithmSource)
            {
                var settings = new Dictionary<string, string>(metric.Source is MeasureSource source ? source.Settings : ((AlgorithmSource)metric.Source).Settings);
                if (metric.Source is MeasureSource) settings["Channel"] = metric.ChannelKey;
                SetNumeric("SampleCount", measurement.SampleCount, true);
                if (metric.Source is MeasureSource) SetNumeric("IntervalMs", measurement.IntervalMs, false);
                metric = metric with
                {
                    Source = metric.Source is MeasureSource acquisition
                    ? acquisition with { Settings = settings } : ((AlgorithmSource)metric.Source) with { Settings = settings }
                };
                void SetNumeric(string field, string text, bool positive)
                {
                    if (int.TryParse(text, out var value) && (positive ? value > 0 : value >= 0)) settings[field] = text;
                    else draft.AuthoringState.IncompleteNumericText[AuthoringDocumentState.FieldKey(node.NodeId, "MetricSetting:" + field)] = text;
                }
            }
            draft = draft with { Measure = [node with { Metric = metric }] };
        }
        // Snapshot isolates caller-owned lists/settings and uses the existing DTO identity validator.
        draft = AuthoringDocumentDto.FromDraft(draft).ToDraft();
        AuthoringCleanup.SyncSidecar(draft.Sidecar, draft.Cleanup);
        var issues = AuthoringIssueService.GetIssues(draft, request.Home);
        var shutdown = draft.Cleanup.IncludeSafeShutdown
            ? $"enabled; coverage: {string.Join(", ", AuthoringCleanup.ResolveSlots(draft))}"
            : "disabled; no shutdown steps";
        var review = $"{name} ({id}) · {draft.Sidecar.DutFamily}\n{draft.Setup.Count} setup actions, {draft.Measure.Count} measurements; shutdown: {shutdown}\n"
            + $"Starting point: {request.StartingPoint}\nResources: {string.Join(", ", instruments.Select(instrument => instrument.SlotName + " — " + AuthoringInstrumentCatalog.All.Single(adapter => adapter.TypeId == instrument.TypeId).DisplayName))}\n"
            + $"Measurements: {string.Join("; ", AuthoringRecipeCatalog.EnumerateMetrics(draft.Measure).Select(metric => metric.Name + " → " + metric.ChannelKey + " [" + metric.YUnit + "]; threshold " + (metric.Limits?.Threshold?.ToString(CultureInfo.InvariantCulture) ?? "none")))}\n"
            + $"Requirements: serial {sidecar.RequireSerial}; fields {string.Join(", ", sidecar.RequiredFields!)}\nDestination: {destination ?? "Unsaved draft"}\n{issues.Count} outstanding issues. Compiled TapPlan and sidecar are generated only by explicit compile/check.";
        return new(draft, destination, issues, review, draft.Measure.Count == 0 ? "Add a measurement and choose its hardware before compiling." : "Review issues, then Save draft / compile and Validate.");
    }

    public PlanInitializationResult Create(PlanInitializationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.WorkspaceRoot)) throw new ArgumentException("Choose an open workspace before creating a durable plan.");
        var result = Construct(request);
        var document = AuthoringDocumentDto.FromDraft(result.Draft);
        document.RequiresCompilation = true;
        new AuthoringDocumentStore(request.WorkspaceRoot, _writer).CreateNew(document, cancellationToken, () => ValidateWorkspace(request.WorkspaceRoot, result.Draft.PlanId));
        return result;
    }

    private static void ValidateWorkspace(string root, string id)
    {
        if (!Directory.Exists(root)) throw new AuthoringWorkspaceException("Open an existing workspace before creating a test plan.");
        if (!File.Exists(Path.Combine(root, AuthoringWorkspaceLoader.ManifestFileName))) return;
        var workspace = AuthoringWorkspaceLoader.Load(root);
        if (workspace.IsReadOnly || new AuthoringDocumentStore(root).LoadWorkspace().IsReadOnly)
            throw new AuthoringWorkspaceException("Workspace is read-only; cannot create a test plan.");
        if (workspace.TapPlanPaths.Any(path => string.Equals(Path.GetFileNameWithoutExtension(path), id, StringComparison.OrdinalIgnoreCase)))
            throw new AuthoringWorkspaceException($"Program '{id}' already exists in this workspace; choose a different ID.");
    }

    private string? ValidateDestination(PlanInitializationRequest request, string id)
    {
        if (request.WorkspaceRoot is null)
        {
            if (request.DestinationPath is not null) throw new ArgumentException("A destination requires a workspace.");
            return null;
        }
        var store = new AuthoringDocumentStore(request.WorkspaceRoot, _writer);
        ValidateWorkspace(request.WorkspaceRoot, id);
        var path = store.ValidateNewDestination(id);
        if (request.DestinationPath is not null && store.ValidatePath(request.DestinationPath) != path)
            throw new ArgumentException("Drafts must use the displayed workspace authoring-drafts filename matching the plan ID.");
        return path;
    }
}
