using HardwareTest.Core.StationHealth;
using HardwareTest.OpenTap.Host;

namespace HardwareTest.Authoring;

public sealed partial class AuthoringWorkspaceViewModel
{
    public bool CanRemoveSelectedProgramKind
        => CanRemoveCatalogItem(ProgramKind, AuthoringWorkspaceCatalog.IsProtectedProgramKind);

    public bool CanRemoveSelectedInstrumentSlot
        => Workspace is not null
           && !Workspace.IsReadOnly
           && SelectedProgram is { Instruments.Count: > 1 }
           && AuthoringWorkspaceCatalog.Normalize(SelectedInstrumentSlot) is { } slot
           && !AuthoringInstrumentUsage.IsReferencedByIdentityOrMeasure(SelectedProgram, slot);

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
            fields.RemoveAll(existing => string.Equals(existing, id, StringComparison.OrdinalIgnoreCase));
            RequiredFieldIds.Apply(draft.Sidecar, fields);
            return draft;
        });
        RememberWorkspaceCatalog(catalogs => AuthoringWorkspaceCatalog.Forget(catalogs.RequiredFields, id));
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
            var current = draft.Sidecar.ReportKinds?.ToList() ?? ["status"];
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
        Status = $"Removed report kind {token}";
        Error = null;
    }

    public void RemoveProgramKindFromCatalog(string? kind = null)
    {
        var token = AuthoringWorkspaceCatalog.Normalize(kind) ?? AuthoringWorkspaceCatalog.Normalize(ProgramKind);
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

        if (SelectedProgram.Instruments.Count <= 1)
        {
            throw new AuthoringWorkspaceException("A program must keep at least one instrument slot.");
        }

        if (AuthoringInstrumentUsage.IsReferencedByIdentityOrMeasure(SelectedProgram, slot))
        {
            throw new AuthoringWorkspaceException(
                $"Cannot remove instrument slot '{slot}'; Identity Check or a measure step still uses it.");
        }

        var instruments = SelectedProgram.Instruments
            .Where(instrument => !string.Equals(instrument.SlotName, slot, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var cleanupSlots = SelectedProgram.Cleanup.InstrumentSlots
            .Where(existing => !string.Equals(existing, slot, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var cleanup = SelectedProgram.Cleanup with { InstrumentSlots = cleanupSlots };
        AuthoringCleanup.SyncSidecar(SelectedProgram.Sidecar, cleanup);
        ReplaceSelected(SelectedProgram with
        {
            Instruments = instruments,
            Cleanup = cleanup,
        });
        if (!Programs.Any(program =>
                program.Instruments.Any(instrument =>
                    string.Equals(instrument.SlotName, slot, StringComparison.OrdinalIgnoreCase))))
        {
            RememberWorkspaceCatalog(catalogs => AuthoringWorkspaceCatalog.Forget(catalogs.InstrumentSlotNames, slot));
        }

        SelectedInstrumentSlot = instruments[0].SlotName;
        Status = $"Removed slot {slot}";
        Error = null;
    }

    private bool CanRemoveCatalogItem(string? id, Func<string?, bool> isProtected)
        => Workspace is { IsReadOnly: false }
           && AuthoringWorkspaceCatalog.Normalize(id) is { } token
           && !isProtected(token);
}
