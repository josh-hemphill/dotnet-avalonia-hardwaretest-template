using System.Collections.Frozen;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private AuthoringInstrumentAdapter? _hardwareEditType = AuthoringInstrumentCatalog.All[0];
    private string _hardwareEditAddress = string.Empty;
    private string _hardwareEditTimeout = string.Empty;
    private AuthoringHardwareDefinition? _selectedHardwareDefinition;

    public IReadOnlyList<AuthoringHardwareDefinition> HardwareDefinitions => Workspace?.Manifest.Catalogs?.Hardware ?? [];
    public AuthoringHardwareDefinition? SelectedHardwareDefinition
    {
        get => _selectedHardwareDefinition is { } selected ? HardwareDefinitions.FirstOrDefault(d => d.Id == selected.Id) : null;
        set => SetField(ref _selectedHardwareDefinition, value);
    }

    public IReadOnlyList<AuthoringHardwareRow> HardwareRows => Instruments.Select(instrument =>
    {
        var registered = AuthoringInstrumentCatalog.TryGet(instrument.TypeId, out var adapter);
        string package;
        try
        {
            var inspection = HardwareInspection;
            package = registered ? $"{adapter.RequiredPackage}: {inspection.Error ?? adapter.Availability(inspection.Home).Reason ?? "available"}"
                : "Unregistered adapter; preserve imported binding";
        }
        catch (ArgumentException) { package = "Invalid OpenTAP home path"; }
        var usage = SelectedProgram is null ? [] : AuthoringInstrumentUsage.DescribeSlotUsage(SelectedProgram, instrument.SlotName)
            .Where(node => node.StartsWith("Setup", StringComparison.Ordinal) || node.StartsWith("Measure", StringComparison.Ordinal)).ToArray();
        var opaque = AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(SelectedProgram);
        var cleanup = SelectedProgram is { } program && program.Cleanup.IncludeSafeShutdown
            && AuthoringCleanup.ResolveSlots(program).Contains(instrument.SlotName, StringComparer.OrdinalIgnoreCase)
            ? "Safe shutdown covers this slot" : "No safe shutdown coverage";
        return new AuthoringHardwareRow(instrument.SlotName, instrument.TypeId, instrument.VisaAddress, package,
            string.Join("; ", usage) + (opaque ? "; unresolved imported usage" : usage.Length == 0 ? "Unused by steps" : ""), cleanup);
    }).ToArray();

    private (OpenTapHome? Home, string? Error) HardwareInspection
    {
        get
        {
            if (Workspace is not { } workspace) return (null, null);
            return WorkspacePackPlan.TryResolveHomePath(workspace, OpenTapHomeOverride, out var path, out var error)
                ? (new OpenTapHome(path!), null) : (null, error);
        }
    }

    public AuthoringInstrumentAdapter? HardwareEditType { get => _hardwareEditType; set => SetField(ref _hardwareEditType, value); }
    public string HardwareEditAddress { get => _hardwareEditAddress; set => SetField(ref _hardwareEditAddress, value ?? string.Empty); }
    public string HardwareEditTimeout { get => _hardwareEditTimeout; set => SetField(ref _hardwareEditTimeout, value ?? string.Empty); }

    public void LoadHardwareEditor()
    {
        var instrument = SelectedInstrument ?? throw new AuthoringWorkspaceException("Select an instrument before editing.");
        HardwareEditType = AuthoringInstrumentCatalog.TryGet(instrument.TypeId, out var adapter) ? adapter : null;
        HardwareEditAddress = instrument.VisaAddress;
        HardwareEditTimeout = instrument.Settings.GetValueOrDefault("IoTimeoutMilliseconds") ?? string.Empty;
    }

    private InstrumentRef HardwareEditorBinding(string name)
    {
        var adapter = HardwareEditType ?? throw new AuthoringWorkspaceException("Choose a registered adapter.");
        if (string.IsNullOrWhiteSpace(HardwareEditAddress)) throw new AuthoringWorkspaceException("Enter the instrument address.");
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(HardwareEditTimeout))
        {
            if (!adapter.ConfigurationFields.Contains("IoTimeoutMilliseconds"))
                throw new AuthoringWorkspaceException("This adapter does not support I/O timeout configuration.");
            if (!int.TryParse(HardwareEditTimeout, out var timeout) || timeout <= 0)
                throw new AuthoringWorkspaceException("I/O timeout must be a positive integer in milliseconds.");
            settings["IoTimeoutMilliseconds"] = timeout.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var binding = new InstrumentRef(name, adapter.TypeId, HardwareEditAddress.Trim()) { Settings = settings.ToFrozenDictionary(StringComparer.Ordinal) };
        AuthoringInstrumentCatalog.Create(binding, InstrumentCreationHomeFor(binding.TypeId));
        return binding;
    }

    public AuthoringHardwareEditImpact PrepareHardwareEdit()
    {
        EnsureWritableWorkspace("review a hardware binding edit");
        var program = SelectedProgram ?? throw new AuthoringWorkspaceException("Select a program.");
        var current = SelectedInstrument ?? throw new AuthoringWorkspaceException("Select an instrument.");
        if (current.OpaqueResourceXml is not null || AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(program))
            throw new AuthoringWorkspaceException("Imported or unresolved hardware usage must be preserved until bindings are represented explicitly.");
        var replacement = HardwareEditorBinding(current.SlotName);
        if (!AuthoringInstrumentCatalog.CanReplace(program, current.SlotName, replacement))
            throw new AuthoringWorkspaceException("Selected adapter cannot satisfy the affected step and cleanup capabilities.");
        return new(_workspaceSession, Workspace!.Root, program.PlanId, current.SlotName, ContentFingerprint(), replacement,
            AuthoringInstrumentUsage.DescribeSlotUsage(program, current.SlotName));
    }

    public void ApplyHardwareEdit(AuthoringHardwareEditImpact impact)
    {
        EnsureWritableWorkspace("edit hardware binding");
        RequireCurrentImpact(impact.Session, impact.WorkspaceRoot, impact.ContentFingerprint);
        if (SelectedProgram?.PlanId != impact.PlanId || !Same(SelectedInstrumentSlot, impact.SlotName))
            throw new AuthoringWorkspaceException("Selection changed; review the hardware edit again.");
        if (!Same(impact.Replacement.SlotName, impact.SlotName))
            throw new AuthoringWorkspaceException("A binding edit must preserve the logical slot; use explicit removal and replacement to retarget.");
        // Revalidate the reviewed value, never use editor text changed after review.
        AuthoringInstrumentCatalog.Create(impact.Replacement, InstrumentCreationHomeFor(impact.Replacement.TypeId));
        if (!AuthoringInstrumentCatalog.CanReplace(SelectedProgram, impact.SlotName, impact.Replacement))
            throw new AuthoringWorkspaceException("The replacement does not support the affected steps.");
        ReplaceSelected(SelectedProgram with { Instruments = SelectedProgram.Instruments.Select(i => Same(i.SlotName, impact.SlotName) ? impact.Replacement : i).ToArray() });
        Status = $"Updated {impact.SlotName} binding in {impact.PlanId}; save the program";
    }

    public void AddHardwareDefinition() => RunCatalogEdit("Add workspace hardware definition", () =>
    {
        var name = AuthoringWorkspaceCatalog.Normalize(NewInstrumentSlot) ?? throw new AuthoringWorkspaceException("Enter a definition name.");
        if (HardwareDefinitions.Any(d => Same(d.Name, name))) throw new AuthoringWorkspaceException("A hardware definition with that name already exists.");
        var binding = HardwareEditorBinding(name);
        var definition = new AuthoringHardwareDefinition(Guid.NewGuid(), name, binding.TypeId, binding.VisaAddress, binding.Settings);
        RememberWorkspaceCatalog(c => c.Hardware.Add(definition));
        SelectedHardwareDefinition = definition;
        NewInstrumentSlot = string.Empty;
    });

    public void LoadHardwareDefinitionEditor()
    {
        var definition = SelectedHardwareDefinition ?? throw new AuthoringWorkspaceException("Select a workspace hardware definition.");
        HardwareEditType = AuthoringInstrumentCatalog.TryGet(definition.TypeId, out var adapter) ? adapter : null;
        HardwareEditAddress = definition.Address;
        HardwareEditTimeout = definition.Settings.GetValueOrDefault("IoTimeoutMilliseconds") ?? string.Empty;
    }

    public void UpdateHardwareDefinition() => RunCatalogEdit("Edit workspace hardware definition", () =>
    {
        var definition = SelectedHardwareDefinition ?? throw new AuthoringWorkspaceException("Select a workspace hardware definition.");
        var binding = HardwareEditorBinding(definition.Name);
        if (definition.TypeId == binding.TypeId && definition.Address == binding.VisaAddress
            && definition.Settings.Count == binding.Settings.Count
            && definition.Settings.All(p => binding.Settings.TryGetValue(p.Key, out var value) && p.Value == value)) return;
        var next = definition with { TypeId = binding.TypeId, Address = binding.VisaAddress, Settings = binding.Settings };
        RememberWorkspaceCatalog(c => c.Hardware = c.Hardware.Select(d => d.Id == definition.Id ? next : d).ToList());
        Status = $"Updated workspace definition {definition.Name}; existing program bindings remain unchanged. Include explicitly in another program.";
    });

    public void IncludeHardwareDefinition()
    {
        EnsureWritableWorkspace("include a hardware definition");
        var definition = SelectedHardwareDefinition ?? throw new AuthoringWorkspaceException("Select a workspace hardware definition.");
        var program = SelectedProgram ?? throw new AuthoringWorkspaceException("Select a program.");
        if (program.Instruments.Any(i => Same(i.SlotName, definition.Name)))
            throw new AuthoringWorkspaceException("This program already has that logical slot; review its binding separately.");
        var binding = new InstrumentRef(definition.Name, definition.TypeId, definition.Address) { Settings = new Dictionary<string, string>(definition.Settings) };
        AuthoringInstrumentCatalog.Create(binding, InstrumentCreationHomeFor(binding.TypeId));
        ReplaceSelected(program with { Instruments = [.. program.Instruments, binding] });
        SelectedInstrumentSlot = binding.SlotName;
    }

    public AuthoringHardwareDefinitionImpact PrepareHardwareDefinitionRemoval()
    {
        EnsureWritableWorkspace("review a hardware definition removal");
        var definition = SelectedHardwareDefinition ?? throw new AuthoringWorkspaceException("Select a workspace hardware definition.");
        return new(_workspaceSession, Workspace!.Root, ContentFingerprint(), definition.Id, definition.Name,
            Programs.Where(p => p.Instruments.Any(i => Same(i.SlotName, definition.Name)))
                .Select(p => $"{p.Sidecar.DisplayName ?? p.PlanId} ({p.PlanId}): existing '{definition.Name}' membership and binding remain; remove or replace explicitly in Hardware").ToArray());
    }

    public void ApplyHardwareDefinitionRemoval(AuthoringHardwareDefinitionImpact impact) => RunCatalogEdit("Remove workspace hardware definition", () =>
    {
        RequireCurrentImpact(impact.Session, impact.WorkspaceRoot, impact.ContentFingerprint);
        if (!HardwareDefinitions.Any(d => d.Id == impact.Id)) throw new AuthoringWorkspaceException("Definition no longer exists.");
        RememberWorkspaceCatalog(c => c.Hardware.RemoveAll(d => d.Id == impact.Id));
        SelectedHardwareDefinition = null;
    });

    public void AddWorkspaceRequiredField() => AddWorkspaceToken(CatalogDeletionKind.RequiredField, NewRequiredField);
    public void AddWorkspaceReportKind() => AddWorkspaceToken(CatalogDeletionKind.ReportKind, NewReportKind);
    public void AddWorkspaceProgramKind() => AddWorkspaceToken(CatalogDeletionKind.ProgramKind, NewProgramKind);

    private void AddWorkspaceToken(CatalogDeletionKind kind, string raw) => RunCatalogEdit("Add workspace definition", () =>
    {
        var token = AuthoringWorkspaceCatalog.Normalize(raw) ?? throw new AuthoringWorkspaceException("Enter a definition name.");
        RememberWorkspaceCatalog(c => AuthoringWorkspaceCatalog.Remember(kind switch
        {
            CatalogDeletionKind.RequiredField => c.RequiredFields,
            CatalogDeletionKind.ReportKind => c.ReportKinds,
            _ => c.ProgramKinds,
        }, token));
        NewRequiredField = NewReportKind = NewProgramKind = string.Empty;
    });

    private void RaiseHardwareProperties()
    {
        var selected = _selectedHardwareDefinition;
        OnPropertyChanged(nameof(HardwareRows));
        OnPropertyChanged(nameof(HardwareDefinitions));
        _selectedHardwareDefinition = selected;
        OnPropertyChanged(nameof(SelectedHardwareDefinition));
    }
}
