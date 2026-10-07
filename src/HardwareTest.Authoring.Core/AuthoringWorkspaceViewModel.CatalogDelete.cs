using HardwareTest.Core.StationHealth;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    private Guid _workspaceSession = Guid.NewGuid();

    public bool CanRemoveSelectedInstrumentSlot
    {
        get
        {
            if (Workspace is not { IsReadOnly: false } || SelectedProgram is null) return false;
            try { PrepareSelectedInstrumentRemoval(); return true; }
            catch (AuthoringWorkspaceException) { return false; }
        }
    }

    public bool CanEditProgramSettings => Workspace is { IsReadOnly: false } && SelectedProgram is not null;
    public string InstrumentRemovalGuardText
    {
        get
        {
            try { PrepareSelectedInstrumentRemoval(); return "Removal requires review and an explicit compatible replacement. Save this program to persist the edit."; }
            catch (AuthoringWorkspaceException ex) { return ex.Message; }
        }
    }

    public CatalogDeletionImpact PrepareRequiredFieldDeletion(string fieldId) => PrepareCatalogDeletion(CatalogDeletionKind.RequiredField, fieldId);
    public CatalogDeletionImpact PrepareReportKindDeletion(string kind) => PrepareCatalogDeletion(CatalogDeletionKind.ReportKind, kind);
    public CatalogDeletionImpact PrepareProgramKindDeletion(string kind) => PrepareCatalogDeletion(CatalogDeletionKind.ProgramKind, kind);

    public CatalogDeletionImpact PrepareCatalogDeletion(CatalogDeletionKind kind, string target)
    {
        EnsureWritableWorkspace("review a workspace catalog deletion");
        var token = AuthoringWorkspaceCatalog.Normalize(target) ?? throw new AuthoringWorkspaceException("Select a catalog entry to remove.");
        GuardProtectedCatalog(kind, token);
        var options = kind switch
        {
            CatalogDeletionKind.RequiredField => RequiredFieldOptions,
            CatalogDeletionKind.ReportKind => ReportKindOptions,
            CatalogDeletionKind.ProgramKind => ProgramKindOptions,
            _ => throw new AuthoringWorkspaceException("Unknown catalog deletion kind."),
        };
        if (!options.Contains(token, StringComparer.OrdinalIgnoreCase)) throw new AuthoringWorkspaceException($"Catalog entry '{token}' no longer exists.");
        var affected = Programs.Where(p => CatalogUses(p.Sidecar, kind, token))
            .Select(p => new DestructiveProgramImpact(p.PlanId, p.Sidecar.DisplayName ?? p.PlanId,
                Array.AsReadOnly(new[] { kind.ToString() }))).ToArray();
        return new(_workspaceSession, Workspace!.Root, kind, token, ContentFingerprint(), affected);
    }

    public void ApplyCatalogDeletion(CatalogDeletionImpact reviewedImpact)
        => RunCatalogEdit("Remove workspace catalog entry", () => ApplyCatalogDeletionCore(reviewedImpact));

    private void ApplyCatalogDeletionCore(CatalogDeletionImpact reviewedImpact)
    {
        ArgumentNullException.ThrowIfNull(reviewedImpact);
        EnsureWritableWorkspace("remove a workspace catalog entry");
        RequireCurrentImpact(reviewedImpact.Session, reviewedImpact.WorkspaceRoot, reviewedImpact.ContentFingerprint);
        var current = PrepareCatalogDeletion(reviewedImpact.Kind, reviewedImpact.Target);
        foreach (var affected in current.AffectedPrograms)
        {
            var program = Programs.Single(p => p.PlanId == affected.PlanId);
            var sidecar = PlanCompiler.CloneSidecar(program.Sidecar);
            switch (current.Kind)
            {
                case CatalogDeletionKind.RequiredField:
                    var fields = RequiredFieldIds.FromSidecar(sidecar).Where(f => !Same(f, current.Target)).ToArray();
                    RequiredFieldIds.Apply(sidecar, fields); break;
                case CatalogDeletionKind.ReportKind:
                    if (sidecar.ReportKinds is { } listed)
                    {
                        var remaining = listed.Where(k => !Same(k, current.Target)).ToArray();
                        sidecar.ReportKinds = remaining.Length == 0 ? ["status"] : remaining;
                        if (!sidecar.ReportKinds.Contains(sidecar.DefaultReportKind ?? "status", StringComparer.OrdinalIgnoreCase))
                            sidecar.DefaultReportKind = sidecar.ReportKinds[0];
                    }
                    else if (Same(sidecar.DefaultReportKind, current.Target)) sidecar.DefaultReportKind = "status";
                    break;
                case CatalogDeletionKind.ProgramKind: sidecar.ProgramKind = ProgramKinds.Dut; break;
            }
            ReplaceProgramSidecar(program, sidecar);
        }
        RememberWorkspaceCatalog(catalogs => AuthoringWorkspaceCatalog.Forget(current.Kind switch
        {
            CatalogDeletionKind.RequiredField => catalogs.RequiredFields,
            CatalogDeletionKind.ReportKind => catalogs.ReportKinds,
            _ => catalogs.ProgramKinds,
        }, current.Target));
        Status = $"Removed {current.Kind switch { CatalogDeletionKind.RequiredField => "required field", CatalogDeletionKind.ReportKind => "report kind", _ => "program kind" }} {current.Target}; use Save All to persist workspace changes";
        Error = null;
    }

    // Legacy entrypoints retain meaningful blank/protected diagnostics but cannot bypass review.
    public void RemoveRequiredField(string fieldId) => RejectUnreviewedCatalogDeletion(CatalogDeletionKind.RequiredField, fieldId);
    public void RemoveReportKind(string kind) => RejectUnreviewedCatalogDeletion(CatalogDeletionKind.ReportKind, kind);
    public void RemoveProgramKindFromCatalog(string kind) => RejectUnreviewedCatalogDeletion(CatalogDeletionKind.ProgramKind, kind);
    private void RejectUnreviewedCatalogDeletion(CatalogDeletionKind kind, string target)
    {
        if (AuthoringWorkspaceCatalog.Normalize(target) is not { } token) return;
        EnsureWritableWorkspace("remove a workspace catalog entry");
        GuardProtectedCatalog(kind, token);
        throw new AuthoringWorkspaceException("Prepare and review the named workspace deletion impact before applying it.");
    }

    public InstrumentRemovalImpact PrepareSelectedInstrumentRemoval()
    {
        EnsureWritableWorkspace("review an instrument slot removal");
        var program = SelectedProgram ?? throw new AuthoringWorkspaceException("Select a program before removing an instrument slot.");
        var slot = AuthoringWorkspaceCatalog.Normalize(SelectedInstrumentSlot) ?? throw new AuthoringWorkspaceException("Select an instrument slot to remove.");
        var targets = program.Instruments.Where(i => Same(i.SlotName, slot)).ToArray();
        if (targets.Length == 0) throw new AuthoringWorkspaceException($"Instrument slot '{slot}' no longer exists.");
        var remaining = program.Instruments.Where(i => !Same(i.SlotName, slot)).ToArray();
        if (remaining.Length == 0) throw new AuthoringWorkspaceException("A program must keep at least one instrument slot with a distinct name.");
        if (AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(program))
            throw new AuthoringWorkspaceException($"Cannot remove instrument slot '{slot}'; raw or unknown steps, unknown algorithms, or legacy instrument-based algorithms have unresolved instrument bindings. Preserve the slot until bindings can be represented explicitly.");
        if (targets.Any(i => !AuthoringInstrumentCatalog.TryGet(i.TypeId, out _)))
            throw new AuthoringWorkspaceException("Cannot prove replacement compatibility for an unknown or unsupported instrument type.");
        var replacements = remaining.GroupBy(i => i.SlotName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1 && AuthoringInstrumentCatalog.CanReplace(program, slot, g.First()))
            .Select(g => g.First().SlotName).ToArray();
        if (replacements.Length == 0) throw new AuthoringWorkspaceException("Choose a distinct remaining slot with compatible registered instrument capabilities.");
        return new(_workspaceSession, Workspace!.Root, program.PlanId, slot, ContentFingerprint(), replacements,
            AuthoringInstrumentUsage.DescribeSlotUsage(program, slot));
    }

    public void ApplyInstrumentRemoval(InstrumentRemovalImpact reviewedImpact, string replacementSlot)
    {
        ArgumentNullException.ThrowIfNull(reviewedImpact);
        EnsureWritableWorkspace("remove an instrument slot");
        RequireCurrentImpact(reviewedImpact.Session, reviewedImpact.WorkspaceRoot, reviewedImpact.ContentFingerprint);
        if (SelectedProgram?.PlanId != reviewedImpact.PlanId || !Same(SelectedInstrumentSlot, reviewedImpact.TargetSlot))
            throw new AuthoringWorkspaceException("The selected program or instrument slot changed; review a new removal impact.");
        var current = PrepareSelectedInstrumentRemoval();
        var replacement = current.CompatibleReplacementSlots.FirstOrDefault(s => Same(s, AuthoringWorkspaceCatalog.Normalize(replacementSlot)))
            ?? throw new AuthoringWorkspaceException("Explicitly choose a distinct compatible replacement slot before removing the instrument.");
        var next = AuthoringInstrumentUsage.RetargetSlot(SelectedProgram!, current.TargetSlot, replacement);
        next = next with { Instruments = next.Instruments.Where(i => !Same(i.SlotName, current.TargetSlot)).ToArray() };
        ReplaceSelected(next);
        SelectedInstrumentSlot = replacement;
        Status = $"Removed slot {current.TargetSlot}; save the program to persist changes";
        Error = null;
    }

    public void RemoveSelectedInstrumentSlot()
    {
        PrepareSelectedInstrumentRemoval();
        throw new AuthoringWorkspaceException("Review the instrument removal impact and explicitly choose a compatible replacement slot before applying it.");
    }

    private string ContentFingerprint() => AuthoringContentFingerprint.Compute(Workspace!, Programs, DirtyPrograms, WorkspaceCatalogDirty);
    private void RequireCurrentImpact(Guid session, string root, string fingerprint)
    {
        if (session != _workspaceSession || root != Workspace!.Root || fingerprint != ContentFingerprint())
            throw new AuthoringWorkspaceException("Workspace, target or content changed since review; prepare and review a new impact.");
    }
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool CatalogUses(ProgramSidecar s, CatalogDeletionKind kind, string target) => kind switch
    {
        CatalogDeletionKind.RequiredField => RequiredFieldIds.FromSidecar(s).Any(f => Same(f, target)),
        CatalogDeletionKind.ReportKind => (s.ReportKinds?.Any(k => Same(k, target)) ?? false) || Same(s.DefaultReportKind, target),
        CatalogDeletionKind.ProgramKind => Same(s.ProgramKind, target),
        _ => false,
    };
    private static void GuardProtectedCatalog(CatalogDeletionKind kind, string target)
    {
        var reason = kind switch
        {
            CatalogDeletionKind.RequiredField when AuthoringWorkspaceCatalog.IsProtectedRequiredField(target) => "serial/partNumber/revision/operator stay",
            CatalogDeletionKind.ReportKind when AuthoringWorkspaceCatalog.IsProtectedReportKind(target) => "status and certification stay",
            CatalogDeletionKind.ProgramKind when AuthoringWorkspaceCatalog.IsProtectedProgramKind(target) => "dut and stationHealth stay",
            _ => null,
        };
        if (reason is not null) throw new AuthoringWorkspaceException($"Cannot remove '{target}'; {reason}.");
    }
    private void ReplaceProgramSidecar(ProgramDraft program, ProgramSidecar sidecar)
    {
        var next = program with { Sidecar = sidecar };
        if (!CommitDocument(next, "Edit program settings")) return;
        Programs = Programs.Select(p => string.Equals(p.PlanId, program.PlanId, StringComparison.OrdinalIgnoreCase) ? next : p).ToArray();
        if (string.Equals(_selectedProgram?.PlanId, program.PlanId, StringComparison.OrdinalIgnoreCase))
        {
            _selectedProgram = next;
            OnPropertyChanged(nameof(SelectedProgram));
        }
        RecomputeDocumentDirty();
        RaiseSidecarProperties();
    }
    private bool CanRemoveCatalogItem(string? id, Func<string?, bool> isProtected)
        => Workspace is { IsReadOnly: false } && AuthoringWorkspaceCatalog.Normalize(id) is { } token && !isProtected(token);
}
