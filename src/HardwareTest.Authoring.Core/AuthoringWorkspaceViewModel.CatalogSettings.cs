using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public void SetInstrumentVisa(string slotName, string visaAddress)
    {
        if (SelectedProgram is null || string.IsNullOrWhiteSpace(slotName))
        {
            return;
        }

        var current = Instruments.FirstOrDefault(instrument =>
            string.Equals(instrument.SlotName, slotName, StringComparison.OrdinalIgnoreCase));
        if (current is not null && string.Equals(current.VisaAddress, visaAddress, StringComparison.Ordinal))
        {
            return;
        }

        var updated = SelectedProgram.Instruments
            .Select(instrument =>
                string.Equals(instrument.SlotName, slotName, StringComparison.OrdinalIgnoreCase)
                    ? instrument with { VisaAddress = visaAddress }
                    : instrument)
            .ToArray();
        ReplaceSelected(SelectedProgram with { Instruments = updated });
    }

    public void InsertFormulaToken(string token)
        => ApplyFormulaCompletion(token, FormulaSource.Length);

    public IReadOnlyList<FormulaCatalog.Item> CompletionsAt(int caret)
        => HasFormula ? FormulaCatalog.CompletionsFor(FormulaCatalog.IdentAt(FormulaSource, caret).Text, ChannelKeys) : [];

    public int ApplyFormulaCompletion(string insertText, int caret)
    {
        if (!HasFormula || string.IsNullOrWhiteSpace(insertText))
        {
            return caret;
        }

        var source = FormulaSource;
        var span = FormulaCatalog.IdentAt(source, caret);
        FormulaSource = source.Remove(span.Start, span.Length).Insert(span.Start, insertText);
        return span.Start + insertText.Length;
    }

    public void SetReportKindIncluded(string kind, bool include)
    {
        EnsureWritableWorkspace("change report kind membership");
        SetReportKind(kind, include);
    }

    public void AddReportKind() => RunCatalogEdit("AddReportKind", AddReportKindCore);

    private void AddReportKindCore()
    {
        var kind = AuthoringWorkspaceCatalog.Normalize(NewReportKind);
        if (kind is null)
        {
            return;
        }

        EnsureWritableWorkspace("add a report kind");
        RememberWorkspaceCatalog(
            catalogs => RememberUnlessDefault(catalogs.ReportKinds, kind, AuthoringWorkspaceCatalog.DefaultReportKinds));
        SetReportKind(kind, include: true);
        NewReportKind = string.Empty;
        OnPropertyChanged(nameof(ReportKindOptions));
        OnPropertyChanged(nameof(ReportKindChoices));
        OnPropertyChanged(nameof(IncludedReportKinds));
        OnPropertyChanged(nameof(DefaultReportKind));
    }

    public void AddProgramKind() => RunCatalogEdit("AddProgramKind", AddProgramKindCore);

    private void AddProgramKindCore()
    {
        var kind = AuthoringWorkspaceCatalog.Normalize(NewProgramKind);
        if (kind is null)
        {
            return;
        }

        EnsureWritableWorkspace("add a program kind");
        RememberWorkspaceCatalog(
            catalogs => RememberUnlessDefault(catalogs.ProgramKinds, kind, AuthoringWorkspaceCatalog.DefaultProgramKinds));
        ProgramKind = kind;
        NewProgramKind = string.Empty;
        OnPropertyChanged(nameof(ProgramKindOptions));
        OnPropertyChanged(nameof(ProgramKindChoices));
    }

    public void AddInstrumentSlot() => RunCatalogEdit("AddInstrumentSlot", AddInstrumentSlotCore);

    private void AddInstrumentSlotCore()
    {
        if (Workspace is null)
        {
            throw new AuthoringWorkspaceException("Open a workspace before adding an instrument slot.");
        }

        if (Workspace.IsReadOnly)
        {
            throw new AuthoringWorkspaceException("Workspace is read-only; cannot add an instrument slot.");
        }

        if (SelectedProgram is null)
        {
            throw new AuthoringWorkspaceException("Select a program before adding an instrument slot.");
        }

        var slot = AuthoringWorkspaceCatalog.Normalize(NewInstrumentSlot);
        if (slot is null)
        {
            throw new AuthoringWorkspaceException("Instrument slot name is required.");
        }

        if (SelectedProgram.Instruments.Any(instrument =>
                string.Equals(instrument.SlotName, slot, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AuthoringWorkspaceException($"Instrument slot '{slot}' already exists.");
        }

        if (NewInstrumentCreationIssue() is { } issue) throw new AuthoringWorkspaceException(issue);
        var typeId = NewInstrumentTypeId;
        var visa = AuthoringWorkspaceCatalog.Normalize(NewInstrumentVisa)
                   ?? $"MOCK::INSTR{SelectedProgram.Instruments.Count}";
        var instrument = new InstrumentRef(slot, typeId, visa);
        // Validate the selected adapter without opening any instrument connection.
        AuthoringInstrumentCatalog.Create(instrument, InstrumentCreationHome);
        RememberWorkspaceCatalog(catalogs => AuthoringWorkspaceCatalog.Remember(catalogs.InstrumentSlotNames, slot));
        ReplaceSelected(SelectedProgram with
        {
            Instruments = [.. SelectedProgram.Instruments, instrument],
        });
        SelectedInstrumentSlot = slot;
        NewInstrumentSlot = string.Empty;
        NewInstrumentVisa = string.Empty;
        Status = $"Added slot {slot}";
        Error = null;
    }

    public void SetCleanupSlotIncluded(string slotName, bool include)
    {
        if (!HasCleanupEditor || SelectedProgram is null)
        {
            return;
        }

        var slot = AuthoringWorkspaceCatalog.Normalize(slotName);
        if (slot is null || HasCleanupSlot(slot) == include)
        {
            return;
        }

        var current = SelectedProgram.Cleanup.InstrumentSlots.ToList();
        if (include)
        {
            current.Add(slot);
        }
        else
        {
            current.RemoveAll(existing => string.Equals(existing, slot, StringComparison.OrdinalIgnoreCase));
        }

        var next = SelectedProgram.Cleanup with { InstrumentSlots = current };
        UpdateCleanupPolicy(next);
    }

    private void UpdateCleanupPolicy(CleanupPolicy next)
    {
        EnsureWritableWorkspace("edit cleanup settings");
        if (SelectedProgram is null) return;
        var sidecar = PlanCompiler.CloneSidecar(SelectedProgram.Sidecar);
        AuthoringCleanup.SyncSidecar(sidecar, next);
        ReplaceSelected(SelectedProgram with { Cleanup = next, Sidecar = sidecar }, rebuildLists: false);
    }

    private bool HasCleanupSlot(string slot)
        => SelectedProgram is not null
           && SelectedProgram.Cleanup.InstrumentSlots.Any(existing =>
               string.Equals(existing, slot, StringComparison.OrdinalIgnoreCase));

    public void SetRequiredFieldIncluded(string fieldId, bool include)
    {
        EnsureWritableWorkspace("change required field membership");
        if (SelectedProgram is null)
        {
            return;
        }

        var id = AuthoringWorkspaceCatalog.Normalize(fieldId);
        if (id is null || HasRequiredField(id) == include)
        {
            return;
        }

        var current = RequiredFieldIds.FromSidecar(SelectedProgram.Sidecar).ToList();
        if (include)
        {
            current.Add(id);
        }
        else
        {
            current.RemoveAll(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase));
        }

        SetSidecar(sidecar => RequiredFieldIds.Apply(sidecar, current));
    }

    public void AddRequiredField() => RunCatalogEdit("AddRequiredField", AddRequiredFieldCore);

    private void AddRequiredFieldCore()
    {
        var id = AuthoringWorkspaceCatalog.Normalize(NewRequiredField);
        if (id is null)
        {
            return;
        }

        EnsureWritableWorkspace("add a required field");
        RememberWorkspaceCatalog(
            catalogs => RememberUnlessDefault(catalogs.RequiredFields, id, RequiredFieldIds.Known));
        SetRequiredFieldIncluded(id, include: true);
        NewRequiredField = string.Empty;
        OnPropertyChanged(nameof(RequiredFieldOptions));
        OnPropertyChanged(nameof(RequiredFieldChoices));
    }

    private bool HasRequiredField(string fieldId)
        => SelectedProgram is not null
           && RequiredFieldIds.Contains(RequiredFieldIds.FromSidecar(SelectedProgram.Sidecar), fieldId);

    private bool HasReportKind(string kind)
        => SelectedProgram?.Sidecar.ReportKinds?.Contains(kind, StringComparer.OrdinalIgnoreCase) == true
           || (kind == "status" && SelectedProgram?.Sidecar.ReportKinds is null);

    private void SetReportKind(string kind, bool include)
    {
        if (SelectedProgram is null || HasReportKind(kind) == include)
        {
            return;
        }

        var current = SelectedProgram.Sidecar.ReportKinds?.ToList() ?? ["status"];
        if (include && !current.Contains(kind, StringComparer.OrdinalIgnoreCase))
        {
            current.Add(kind);
        }
        else if (!include)
        {
            current.RemoveAll(existing => string.Equals(existing, kind, StringComparison.OrdinalIgnoreCase));
        }

        if (current.Count == 0)
        {
            current.Add("status");
        }

        SetSidecar(s =>
        {
            s.ReportKinds = [.. current];
            if (!current.Contains(s.DefaultReportKind ?? "status", StringComparer.OrdinalIgnoreCase))
            {
                s.DefaultReportKind = current[0];
            }
        });
        OnPropertyChanged(nameof(ReportKindChoices));
        OnPropertyChanged(nameof(IncludedReportKinds));
        OnPropertyChanged(nameof(DefaultReportKind));
    }

    internal void RefreshInstrumentSlots()
    {
        var next = SelectedProgram?.Instruments.Select(instrument => instrument.SlotName).ToArray() ?? [];
        if (InstrumentSlots.SequenceEqual(next, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        InstrumentSlots = next;
        OnPropertyChanged(nameof(InstrumentSlots));
        OnPropertyChanged(nameof(CanAddInstrumentSlot));
        OnPropertyChanged(nameof(CanRemoveSelectedInstrumentSlot));
        OnPropertyChanged(nameof(InstrumentRemovalGuardText));
    }

    private void SetSidecarIfUnchanged<T>(T current, T next, Action<ProgramSidecar> mutate)
    {
        if (EqualityComparer<T>.Default.Equals(current, next))
        {
            return;
        }

        SetSidecar(mutate);
    }

    private void SetSidecar(Action<ProgramSidecar> mutate)
    {
        if (SelectedProgram is null)
        {
            return;
        }

        EnsureWritableWorkspace("edit program settings");
        var sidecar = PlanCompiler.CloneSidecar(SelectedProgram.Sidecar);
        mutate(sidecar);
        ReplaceProgramSidecar(SelectedProgram, sidecar);
        RaiseSidecarProperties();
    }

    private void EnsureWritableWorkspace(string action)
    {
        if (Workspace is null)
        {
            throw new AuthoringWorkspaceException($"Open a workspace before {action}.");
        }

        if (Workspace.IsReadOnly)
        {
            throw new AuthoringWorkspaceException($"Workspace is read-only; cannot {action}.");
        }
    }

    private static void RememberUnlessDefault(
        List<string> items,
        string token,
        IReadOnlyList<string> defaults)
    {
        if (AuthoringWorkspaceCatalog.Contains(defaults, token))
        {
            return;
        }

        AuthoringWorkspaceCatalog.Remember(items, token);
    }

    private void RememberWorkspaceCatalog(Action<AuthoringWorkspaceCatalogs> mutate)
    {
        if (Workspace is null || Workspace.IsReadOnly)
        {
            return;
        }

        var original = Workspace.Manifest.Catalogs;
        var catalogs = original is null ? new AuthoringWorkspaceCatalogs() : new AuthoringWorkspaceCatalogs
        {
            ReportKinds = [.. original.ReportKinds],
            ProgramKinds = [.. original.ProgramKinds],
            RequiredFields = [.. original.RequiredFields],
            InstrumentSlotNames = [.. original.InstrumentSlotNames],
            Hardware = [.. original.Hardware],
        };
        mutate(catalogs);
        if ((original?.ReportKinds ?? []).SequenceEqual(catalogs.ReportKinds)
            && (original?.ProgramKinds ?? []).SequenceEqual(catalogs.ProgramKinds)
            && (original?.RequiredFields ?? []).SequenceEqual(catalogs.RequiredFields)
            && (original?.InstrumentSlotNames ?? []).SequenceEqual(catalogs.InstrumentSlotNames)
            && (original?.Hardware ?? []).SequenceEqual(catalogs.Hardware)) return;
        var manifest = System.Text.Json.JsonSerializer.Deserialize(
            System.Text.Json.JsonSerializer.Serialize(Workspace.Manifest, AuthoringJsonContext.Default.AuthoringManifest),
            AuthoringJsonContext.Default.AuthoringManifest)!;
        manifest.Catalogs = catalogs;
        Workspace = Workspace with { Manifest = manifest };
        WorkspaceCatalogDirty = true;
        WorkspaceCatalogSaveFailure = null;
        Findings = [];
        FindingRows = [];
        RefreshDirtyState();
        RaiseHardwareProperties();
        OnPropertyChanged(nameof(ReportKindOptions));
        OnPropertyChanged(nameof(ReportKindChoices));
        OnPropertyChanged(nameof(IncludedReportKinds));
        OnPropertyChanged(nameof(RequiredFieldOptions));
        OnPropertyChanged(nameof(RequiredFieldChoices));
        OnPropertyChanged(nameof(ProgramKindOptions));
        OnPropertyChanged(nameof(ProgramKindChoices));
    }

    private void UpdateSelectedSetup(Func<SetupAction, SetupAction> mutate)
    {
        if (SelectedProgram is null || SelectedSequence is not { Section: SequenceSection.Setup } row
            || row.IndexPath.Count != 1)
        {
            return;
        }

        var setup = SelectedProgram.Setup.ToArray();
        var index = row.IndexPath[0];
        if (index < 0 || index >= setup.Length)
        {
            return;
        }

        var next = mutate(setup[index]);
        if (Equals(next, setup[index]))
        {
            return;
        }

        setup[index] = next;
        ReplaceSelected(SelectedProgram with { Setup = setup }, rebuildLists: false);
    }

    private static string FormatOptional(double? value)
        => value is { } number
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
}
