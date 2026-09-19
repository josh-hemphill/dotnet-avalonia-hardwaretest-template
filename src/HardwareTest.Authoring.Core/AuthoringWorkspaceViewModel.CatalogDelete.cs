using HardwareTest.Core.StationHealth;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public bool CanRemoveSelectedInstrumentSlot
        => Workspace is not null
           && !Workspace.IsReadOnly
           && SelectedProgram is not null
           && AuthoringWorkspaceCatalog.Normalize(SelectedInstrumentSlot) is { } slot
           && RemainingInstrumentCount(SelectedProgram, slot) >= 1
           && !AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(SelectedProgram);

    public void RemoveRequiredField(string fieldId)
    {
        var id = AuthoringWorkspaceCatalog.Normalize(fieldId);
        if (id is null)
        {
            return;
        }

        EnsureWritableWorkspace("remove a required field");
        if (AuthoringWorkspaceCatalog.IsProtectedRequiredField(id))
        {
            throw new AuthoringWorkspaceException(
                $"Cannot remove required field '{id}'; serial/partNumber/revision/operator stay.");
        }

        MapAllPrograms(draft =>
        {
            var fields = RequiredFieldIds.FromSidecar(draft.Sidecar).ToList();
            if (fields.RemoveAll(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                return draft;
            }

            RequiredFieldIds.Apply(draft.Sidecar, fields);
            return draft;
        });
        RememberWorkspaceCatalog(catalogs => AuthoringWorkspaceCatalog.Forget(catalogs.RequiredFields, id));
        PersistProgramSidecars();
        Status = $"Removed required field {id}";
        Error = null;
    }

    public void RemoveReportKind(string kind)
    {
        var token = AuthoringWorkspaceCatalog.Normalize(kind);
        if (token is null)
        {
            return;
        }

        EnsureWritableWorkspace("remove a report kind");
        if (AuthoringWorkspaceCatalog.IsProtectedReportKind(token))
        {
            throw new AuthoringWorkspaceException(
                $"Cannot remove report kind '{token}'; status and certification stay.");
        }

        MapAllPrograms(draft =>
        {
            if (draft.Sidecar.ReportKinds is not { Length: > 0 } listed)
            {
                if (string.Equals(draft.Sidecar.DefaultReportKind, token, StringComparison.OrdinalIgnoreCase))
                {
                    draft.Sidecar.DefaultReportKind = "status";
                }

                return draft;
            }

            if (!listed.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                return draft;
            }

            var current = listed.ToList();
            current.RemoveAll(existing => string.Equals(existing, token, StringComparison.OrdinalIgnoreCase));
            if (current.Count == 0)
            {
                current.Add("status");
            }

            draft.Sidecar.ReportKinds = [.. current];
            if (!current.Contains(draft.Sidecar.DefaultReportKind ?? "status", StringComparer.OrdinalIgnoreCase))
            {
                draft.Sidecar.DefaultReportKind = current[0];
            }

            return draft;
        });
        RememberWorkspaceCatalog(catalogs => AuthoringWorkspaceCatalog.Forget(catalogs.ReportKinds, token));
        PersistProgramSidecars();
        Status = $"Removed report kind {token}";
        Error = null;
    }

    public void RemoveProgramKindFromCatalog(string kind)
    {
        var token = AuthoringWorkspaceCatalog.Normalize(kind);
        if (token is null)
        {
            return;
        }

        EnsureWritableWorkspace("remove a program kind");
        if (AuthoringWorkspaceCatalog.IsProtectedProgramKind(token))
        {
            throw new AuthoringWorkspaceException(
                $"Cannot remove program kind '{token}'; dut and stationHealth stay.");
        }

        MapAllPrograms(draft =>
        {
            if (string.Equals(draft.Sidecar.ProgramKind, token, StringComparison.OrdinalIgnoreCase))
            {
                draft.Sidecar.ProgramKind = ProgramKinds.Dut;
            }

            return draft;
        });
        RememberWorkspaceCatalog(catalogs => AuthoringWorkspaceCatalog.Forget(catalogs.ProgramKinds, token));
        PersistProgramSidecars();
        Status = $"Removed program kind {token}";
        Error = null;
    }

    public void RemoveSelectedInstrumentSlot()
    {
        EnsureWritableWorkspace("remove an instrument slot");
        if (SelectedProgram is null)
        {
            throw new AuthoringWorkspaceException("Select a program before removing an instrument slot.");
        }

        var slot = AuthoringWorkspaceCatalog.Normalize(SelectedInstrumentSlot);
        if (slot is null)
        {
            throw new AuthoringWorkspaceException("Select an instrument slot to remove.");
        }

        if (SelectedProgram.Instruments.Count <= 1
            || RemainingInstrumentCount(SelectedProgram, slot) < 1)
        {
            throw new AuthoringWorkspaceException("A program must keep at least one instrument slot.");
        }

        if (AuthoringInstrumentUsage.HasOpaqueInstrumentRefs(SelectedProgram))
        {
            throw new AuthoringWorkspaceException(
                $"Cannot remove instrument slot '{slot}'; a raw or unknown Identity/measure step may still use it.");
        }

        var instruments = SelectedProgram.Instruments
            .Where(instrument => !string.Equals(instrument.SlotName, slot, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var replacement = instruments[0].SlotName;
        var cleanupSlots = SelectedProgram.Cleanup.InstrumentSlots
            .Where(existing => !string.Equals(existing, slot, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var cleanup = SelectedProgram.Cleanup with { InstrumentSlots = cleanupSlots };
        var next = AuthoringInstrumentUsage.RetargetSlot(SelectedProgram, slot, replacement) with
        {
            Instruments = instruments,
            Cleanup = cleanup,
            Sidecar = PlanCompiler.CloneSidecar(SelectedProgram.Sidecar),
        };
        AuthoringCleanup.SyncSidecar(next.Sidecar, cleanup);
        ReplaceSelected(next);
        SelectedInstrumentSlot = replacement;
        var existingTapPlan = TryExistingTapPlanPath(next.PlanId);
        if (!string.IsNullOrWhiteSpace(existingTapPlan))
        {
            try
            {
                _compiler.Save(next, existingTapPlan);
            }
            catch (Exception ex)
            {
                Status = $"Removed slot {slot}";
                Error = ex is AuthoringWorkspaceException
                    ? ex.Message
                    : $"Failed to save plan after removing slot '{slot}': {ex.Message}";
                return;
            }
        }

        if (!Programs.Any(program =>
                program.Instruments.Any(instrument =>
                    string.Equals(instrument.SlotName, slot, StringComparison.OrdinalIgnoreCase))))
        {
            RememberWorkspaceCatalog(catalogs => AuthoringWorkspaceCatalog.Forget(catalogs.InstrumentSlotNames, slot));
        }

        Status = $"Removed slot {slot}";
        Error = null;
    }

    private static int RemainingInstrumentCount(ProgramDraft draft, string slot)
        => draft.Instruments.Count(instrument =>
            !string.Equals(instrument.SlotName, slot, StringComparison.OrdinalIgnoreCase));

    internal void PersistProgramSidecars()
    {
        if (Workspace is null || Workspace.IsReadOnly)
        {
            return;
        }

        foreach (var program in Programs)
        {
            var tapPlanPath = TryExistingTapPlanPath(program.PlanId);
            if (string.IsNullOrWhiteSpace(tapPlanPath))
            {
                continue;
            }

            _compiler.SaveSidecar(tapPlanPath, program.Sidecar);
        }
    }

    private bool CanRemoveCatalogItem(string? id, Func<string?, bool> isProtected)
        => Workspace is { IsReadOnly: false }
           && AuthoringWorkspaceCatalog.Normalize(id) is { } token
           && !isProtected(token);
}
