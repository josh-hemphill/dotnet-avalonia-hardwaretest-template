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
    private readonly Func<Task> _loadSelectedProgramAsync;
    private readonly Action _onCatalogRefreshed;

    public ProgramSelectionViewModel(
        Action<string> setStatus,
        Func<bool>? isEngineerDebugMode = null,
        Func<Task>? loadSelectedProgramAsync = null,
        Action? onCatalogRefreshed = null)
    {
        _setStatus = setStatus;
        _isEngineerDebugMode = isEngineerDebugMode ?? (() => false);
        _loadSelectedProgramAsync = loadSelectedProgramAsync ?? (() => Task.CompletedTask);
        _onCatalogRefreshed = onCatalogRefreshed ?? (() => { });

        RefreshProgramsCommand = ReactiveCommand.CreateFromTask(RefreshProgramsAsync);
        OpenPlanFileCommand = ReactiveCommand.CreateFromTask(OpenPlanFileAsync);
    }

    public ObservableCollection<ProgramItemViewModel> Programs { get; } = [];

    /// Supplied by the view; returns a picked TapPlan path or null when cancelled.
    public Func<CancellationToken, Task<string?>>? RequestPlanFilePath { get; set; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshProgramsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenPlanFileCommand { get; }

    [Reactive] private ProgramItemViewModel? _selectedProgram;

    public async Task RefreshProgramsAsync()
    {
        Programs.Clear();
        foreach (var entry in ProgramCatalog.Enumerate())
        {
            Programs.Add(new ProgramItemViewModel
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
            });
        }

        SelectedProgram ??= Programs.FirstOrDefault();
        if (SelectedProgram is not null)
        {
            await _loadSelectedProgramAsync();
        }

        _setStatus($"Loaded {Programs.Count} program(s).");
        _onCatalogRefreshed();
    }

    private async Task OpenPlanFileAsync()
    {
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

        var path = await RequestPlanFilePath(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var item = new ProgramItemViewModel
        {
            Id = System.IO.Path.GetFileNameWithoutExtension(path),
            DisplayName = System.IO.Path.GetFileNameWithoutExtension(path),
            Path = path,
            DutFamily = "generic",
            LoadKind = ProgramLoadKind.TapPlanFile,
            Requirements = ProgramRequirements.FromFamily("generic"),
        };
        Programs.Add(item);
        SelectedProgram = item;
        _setStatus($"Opened {item.DisplayName}");
    }
}
