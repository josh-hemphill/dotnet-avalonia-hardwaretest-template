using System.Threading;
using ReactiveUI;

namespace HardwareTest.Features.RunTest;

public partial class RunTestViewModel
{
    private readonly SemaphoreSlim _programLoadGate = new(1, 1);
    private ProgramItemViewModel? _readyProgram;
    private int _programSelectionVersion;
    private int _readyProgramVersion = -1;
    private bool _hasAttemptedProgramLoad;

    public bool IsProgramLoading { get; private set; }
    public bool IsSelectedProgramReady => ProgramSelection.SelectedProgram is { } program && IsProgramLoadedFor(program);
    public bool ShowProgramRecovery => !IsProgramLoading && !ProgramSelection.IsBusy && !IsSelectedProgramReady;
    public string ProgramDetailsText
    {
        get => ProgramSelection.SelectedProgram?.DetailsText
        + (string.IsNullOrWhiteSpace(field) ? string.Empty : $"\n\nLoad details: {field}"); private set;
    } = string.Empty;

    public string ProgramAvailabilityMessage
    {
        get
        {
            if (IsProgramLoading || ProgramSelection.IsBusy)
                return "Loading the selected test program. Run stays disabled until loading finishes.";
            if (!string.IsNullOrWhiteSpace(field)) return field;
            return "Choose the assigned test program, then confirm DUT. If it is missing, ask a station technician to install it, then Reload programs.";
        }

        private set;
    } = string.Empty;

    internal bool IsProgramReadyFor(ProgramItemViewModel program)
        => !ProgramSelection.IsBusy && IsProgramLoadedFor(program);

    private bool IsProgramLoadedFor(ProgramItemViewModel program)
        => !IsProgramLoading && ReferenceEquals(program, ProgramSelection.SelectedProgram)
            && ReferenceEquals(program, _readyProgram)
            && _programSelectionVersion == _readyProgramVersion
            && string.Equals(_plan.LoadedPlanPath, program.Path, StringComparison.OrdinalIgnoreCase);

    private void InvalidateSelectedProgram()
    {
        _programSelectionVersion++;
        _readyProgram = null;
        _readyProgramVersion = -1;
        IsProgramLoading = false;
        ProgramAvailabilityMessage = string.Empty;
        ProgramDetailsText = string.Empty;
        RaiseProgramAvailability();
    }

    private void RaiseProgramAvailability()
    {
        this.RaisePropertyChanged(nameof(IsProgramLoading));
        this.RaisePropertyChanged(nameof(IsSelectedProgramReady));
        this.RaisePropertyChanged(nameof(ShowProgramRecovery));
        this.RaisePropertyChanged(nameof(ProgramAvailabilityMessage));
        this.RaisePropertyChanged(nameof(ProgramDetailsText));
        RaiseStartGates();
    }

    private async Task LoadSelectedProgramSafelyAsync(string? preserveStagePath, string? preserveStepPath, CancellationToken cancellationToken)
    {
        var program = ProgramSelection.SelectedProgram;
        if (program is null) return;
        var version = _programSelectionVersion;
        var wasReady = IsProgramLoadedFor(program);
        bool IsCurrent()
        {
            return version == _programSelectionVersion && ReferenceEquals(program, ProgramSelection.SelectedProgram);
        }

        await RunOnUiAsync(() =>
        {
            if (!IsCurrent()) return;
            _readyProgram = null;
            IsProgramLoading = true;
            RaiseProgramAvailability();
        }).ConfigureAwait(false);
        var held = false;
        try
        {
            await _programLoadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            held = true;
            if (!IsCurrent()) return;
            if (!string.IsNullOrWhiteSpace(program.ReferenceDeclarationError))
                throw new InvalidOperationException(program.ReferenceDeclarationError);
            var beforeArtifacts = ReadReferenceArtifactHashes(program);
            // A host may already own the exact initial plan (for example restored replay
            // evidence). Adopt it once; a later failed/changed selection must load again.
            var canAdoptInitialHostPlan = !_hasAttemptedProgramLoad && version == 1;
            _hasAttemptedProgramLoad = true;
            await RunOnUiAsync(() =>
            {
                if (!IsCurrent()) return;
                SessionPanel.ApplyIdleStaleCheck();
                ApplySelectedProgram(program);
            }).ConfigureAwait(false);
            if (!IsCurrent()) return;
            var alreadyLoaded = (wasReady || canAdoptInitialHostPlan)
                && string.Equals(_plan.LoadedPlanPath, program.Path, StringComparison.OrdinalIgnoreCase);
            if (!alreadyLoaded)
                await LoadProgramEntryAsync(program, cancellationToken).ConfigureAwait(false);
            var afterArtifacts = ReadReferenceArtifactHashes(program);
            if (beforeArtifacts != afterArtifacts)
                throw new InvalidOperationException("The compiled plan or sidecar changed while loading. Reload the complete bench program before Run.");
            cancellationToken.ThrowIfCancellationRequested();
            await RunOnUiAsync(() =>
            {
                if (!IsCurrent()) return;
                if (!alreadyLoaded || (StepTree.Hierarchy.Count == 0 && StepTree.FullHierarchy.Count == 0))
                    StepTree.RebuildFromHost(preserveStagePath, preserveStepPath);
                else if (!string.IsNullOrWhiteSpace(preserveStepPath))
                    StepTree.ResolveSelectedStep(preserveStepPath);
                else if (!string.IsNullOrWhiteSpace(preserveStagePath))
                    StepTree.RestoreSelection(preserveStagePath, preserveStepPath);
                StationOverrides.RefreshStationSlotSummary();
                StationOverrides.ApplySavedParameterOverrides();
                StationOverrides.RefreshParameterFields();
                SessionPanel.RefreshSessionSummary();
                if (!alreadyLoaded) Workspace.ResetToSteps();
                ProgramAvailabilityMessage = string.Empty;
                ProgramDetailsText = string.Empty;
                _readyProgram = program;
                _readyProgramVersion = version;
                _loadedReferenceArtifacts = afterArtifacts;
            }).ConfigureAwait(false);
            await RefreshBenchReferencesAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SetProgramLoadErrorAsync("Loading was cancelled. Reload programs before Run.", "Loading was cancelled.").ConfigureAwait(false);
            throw;
        }
        catch (ReferenceConfigurationException ex)
        {
            await SetProgramLoadErrorAsync(ex.Message, ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await SetProgramLoadErrorAsync("Cannot load this test program. Choose the assigned program or ask a station technician to restore it, then Reload programs.", ex.Message)
                .ConfigureAwait(false);
        }
        finally
        {
            if (held) _ = _programLoadGate.Release();
            await RunOnUiAsync(() =>
            {
                if (!IsCurrent()) return;
                IsProgramLoading = false;
                RaiseProgramAvailability();
            }).ConfigureAwait(false);
        }

        Task SetProgramLoadErrorAsync(string message, string details)
        {
            return RunOnUiAsync(() =>
        {
            if (!IsCurrent()) return;
            _readyProgram = null;
            _readyProgramVersion = -1;
            ProgramAvailabilityMessage = message;
            ProgramDetailsText = details;
            Status = message;
        });
        }
    }
}
