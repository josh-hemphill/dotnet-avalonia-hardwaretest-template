using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HardwareTest.OpenTap.Host;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.RunTest;

/// Program catalog dropdown: refresh from the catalog and open an arbitrary plan from disk.
public partial class ProgramSelectionViewModel : ReactiveObject
{
    private readonly Action<string> _setStatus;
    private readonly Func<bool> _isEngineerDebugMode;
    private readonly Func<CancellationToken, Task> _loadSelectedProgramAsync;
    private readonly Action _onCatalogRefreshed;
    private readonly Func<bool> _isSelectedProgramReady;
    private readonly Func<bool> _isRunning;

    public ProgramSelectionViewModel(
        Action<string> setStatus,
        Func<bool>? isEngineerDebugMode = null,
        Func<CancellationToken, Task>? loadSelectedProgramAsync = null,
        Action? onCatalogRefreshed = null,
        Func<bool>? isSelectedProgramReady = null,
        Func<bool>? isRunning = null)
    {
        _setStatus = setStatus;
        _isEngineerDebugMode = isEngineerDebugMode ?? (() => false);
        _loadSelectedProgramAsync = loadSelectedProgramAsync ?? (_ => Task.CompletedTask);
        _onCatalogRefreshed = onCatalogRefreshed ?? (() => { });
        _isSelectedProgramReady = isSelectedProgramReady ?? (() => true);
        _isRunning = isRunning ?? (() => false);

        RefreshProgramsCommand = ReactiveCommand.CreateFromTask(() => RefreshProgramsAsync());
        OpenPlanFileCommand = ReactiveCommand.CreateFromTask(OpenPlanFileAsync);
    }

    public ObservableCollection<ProgramItemViewModel> Programs { get; } = [];

    /// Supplied by the view; returns a picked TapPlan path or null when cancelled.
    public Func<CancellationToken, Task<string?>>? RequestPlanFilePath { get; set; }

    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> RefreshProgramsCommand { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> OpenPlanFileCommand { get; }

    [Reactive] private ProgramItemViewModel? _selectedProgram;
    [Reactive] private bool _isBusy;

    public async Task RefreshProgramsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanChangePrograms()) return;
        if (IsBusy)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        IsBusy = true;
        try
        {
            var previousId = SelectedProgram?.Id;
            Programs.Clear();
            foreach (var entry in ProgramCatalog.Enumerate())
            {
                cancellationToken.ThrowIfCancellationRequested();
                Programs.Add(FromCatalogEntry(entry));
            }

            SelectedProgram = Programs.FirstOrDefault(p =>
                                  previousId is not null
                                  && string.Equals(p.Id, previousId, StringComparison.OrdinalIgnoreCase))
                              ?? Programs.FirstOrDefault();
            cancellationToken.ThrowIfCancellationRequested();
            if (SelectedProgram is not null)
            {
                await _loadSelectedProgramAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (SelectedProgram is null)
                _setStatus("No test programs are available. Ask a station technician to install the assigned program, then Reload programs.");
            else if (_isSelectedProgramReady())
                _setStatus($"Loaded {Programs.Count} program(s).");
            _onCatalogRefreshed();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenPlanFileAsync()
    {
        if (!CanChangePrograms()) return;
        if (!_isEngineerDebugMode())
        {
            _setStatus("Open arbitrary plan requires Engineer/Debug mode.");
            return;
        }

        if (RequestPlanFilePath is null)
        {
            _setStatus("File picker unavailable.");
            return;
        }

        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var path = await RequestPlanFilePath(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            if (!CanChangePrograms()) return;

            BenchProgramHandoff.RequireCustomPlanId(System.IO.Path.GetFileNameWithoutExtension(path));
            var item = FromCatalogEntry(ProgramCatalog.FromFile(path));
            Programs.Add(item);
            SelectedProgram = item;
            await _loadSelectedProgramAsync(CancellationToken.None);
            if (_isSelectedProgramReady()) _setStatus($"Opened {item.DisplayName}");
        }
        catch (Exception ex)
        {
            _setStatus($"Cannot open this program: {ex.Message}. Restore the complete compiled plan and sidecar, then retry.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static ProgramItemViewModel FromCatalogEntry(ProgramCatalogEntry entry) => new()
    {
        Id = entry.Id,
        DisplayName = entry.DisplayName,
        Path = entry.Path,
        DutFamily = entry.DutFamily,
        IsSample = entry.IsBuiltIn,
        LoadKind = entry.LoadKind,
        Requirements = entry.Requirements,
        ReportKinds = entry.ReportKinds,
        SelectionIncludesCleanup = entry.SelectionIncludesCleanup,
        ProgramKind = entry.ProgramKind,
        StationHealthProfileId = entry.StationHealthProfileId,
        RequireStationHealth = entry.RequireStationHealth,
        StationHealthMaxAgeHours = entry.StationHealthMaxAgeHours,
        StationHealthGate = entry.StationHealthGate,
        ReferenceRules = entry.ReferenceRules,
        ReferenceDeclarationError = entry.ReferenceDeclarationError,
        PlanSha256 = entry.PlanSha256,
        SidecarSha256 = entry.SidecarSha256,
    };

    private bool CanChangePrograms()
    {
        if (!_isRunning()) return true;
        _setStatus("Finish or Stop Run before changing test programs.");
        return false;
    }
}
