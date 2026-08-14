using System.ComponentModel;
using System.Runtime.CompilerServices;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;
using Serilog;
using ILogger = Serilog.ILogger;

namespace HardwareTest.OpenTap.Host;

/// OpenTAP-backed run session used by the killable worker process.
/// Façade over <see cref="OpenTapRunContext"/>, <see cref="OpenTapHostCatalog"/>, and station/plan helpers.
/// Host tests may construct this in-process as the documented test-only host
/// (serial <c>OpenTapSerial</c> suite). The UI process uses <c>OpenTapWorkerClient</c>.
public sealed partial class OpenTapSession : IOpenTapSession, INotifyPropertyChanged
{
    private readonly ILogger _logger;
    private readonly AppSettings _settings;
    private readonly object _sync = new();
    private readonly OpenTapHostCatalog _catalog;
    private TestPlan? _plan;
    private readonly List<Instrument> _instruments = [];
    private HardwareDut? _dut;
    private DutIdentity? _dutIdentity;
    private List<OpenTapStepNode> _stepTree = [];
    private List<OpenTapInstrumentSlot> _slots = [];
    private List<StoredSample> _lastSamples = [];
    private readonly IBenchOperationCoordinator? _bench;
    private readonly bool _cancelExecuteWithToken;
    /// 0 = idle, 1 = a run holds the single-flight gate.
    private int _runGate;
    private OpenTapRunContext? _activeContext;
    private bool _pausedBeforeRun;

    /// <param name="cancelExecuteWithToken">
    /// When true (OpenTAP worker), pass the run CTS into <c>TestPlan.ExecuteAsync</c>.
    /// When false (in-process test-only host), keep <c>Execute</c> with
    /// <see cref="CancellationToken.None"/> so cooperative Abort cannot poison TapThread.
    /// </param>
    public OpenTapSession(
        AppSettings? settings = null,
        ILogger? logger = null,
        IVisaBroker? visaBroker = null,
        IBenchOperationCoordinator? bench = null,
        bool cancelExecuteWithToken = false)
    {
        _settings = settings ?? new AppSettings();
        _logger = logger ?? Serilog.Log.ForContext<OpenTapSession>();
        _bench = bench;
        _cancelExecuteWithToken = cancelExecuteWithToken;
        _catalog = new OpenTapHostCatalog(_settings, _logger, visaBroker);
    }

    public string? LoadedPlanPath { get; private set; }
    public string? LoadedPlanName { get; private set; }
    public IReadOnlyList<OpenTapStepNode> StepTree => _stepTree;
    public IReadOnlyList<OpenTapInstrumentSlot> InstrumentSlots => _slots;
    public bool IsExecuting => Volatile.Read(ref _runGate) != 0;
    public bool IsAwaitingOperator => _activeContext?.IsAwaitingOperator ?? false;
    public string? OperatorPromptMessage => _activeContext?.OperatorPromptMessage;
    public OperatorInteractionRequest? PendingInteraction => _activeContext?.PendingInteraction;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task LoadSampleProgramAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _catalog.EnsurePlugins();
        var plan = SampleProgramFactory.Create();
        BindPlan(plan, SampleProgramFactory.EmbeddedName, "Sample Hardware Suite (Demo)");
        return Task.CompletedTask;
    }

    public Task LoadBoardDemoProgramAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _catalog.EnsurePlugins();
        var plan = BoardDemoProgramFactory.Create();
        BindPlan(plan, BoardDemoProgramFactory.EmbeddedName, BoardDemoProgramFactory.DisplayName);
        return Task.CompletedTask;
    }

    public Task LoadSweepDemoProgramAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _catalog.EnsurePlugins();
        var plan = SweepDemoProgramFactory.Create();
        BindPlan(plan, SweepDemoProgramFactory.EmbeddedName, SweepDemoProgramFactory.DisplayName);
        return Task.CompletedTask;
    }

    public Task LoadTimingDemoProgramAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _catalog.EnsurePlugins();
        var plan = TimingDemoProgramFactory.Create();
        BindPlan(plan, TimingDemoProgramFactory.EmbeddedName, TimingDemoProgramFactory.DisplayName);
        return Task.CompletedTask;
    }

    public Task LoadPlanShapeAsync(string fixtureFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _catalog.EnsurePlugins();
        var match = PlanShapeFixtures.All.FirstOrDefault(f =>
            string.Equals(f.FileName, fixtureFileName, StringComparison.OrdinalIgnoreCase));
        if (match.Create is null)
        {
            throw new ArgumentException($"Unknown plan-shape fixture '{fixtureFileName}'.", nameof(fixtureFileName));
        }

        var display = Path.GetFileNameWithoutExtension(fixtureFileName);
        BindPlan(match.Create(), fixtureFileName, display);
        return Task.CompletedTask;
    }

    public Task LoadPlanAsync(string tapPlanPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _catalog.EnsurePlugins();
        if (!File.Exists(tapPlanPath))
        {
            throw new FileNotFoundException("Test plan not found.", tapPlanPath);
        }

        var plan = TestPlan.Load(tapPlanPath);
        BindPlan(plan, tapPlanPath, string.IsNullOrWhiteSpace(plan.Name) ? Path.GetFileNameWithoutExtension(tapPlanPath) : plan.Name);
        return Task.CompletedTask;
    }

    private void BindPlan(TestPlan plan, string path, string displayName)
    {
        lock (_sync)
        {
            ThrowIfExecuting("load or bind a plan");
            _plan = plan;
            _instruments.Clear();
            foreach (var instr in InstrumentResourceAccess.CollectFromPlan(plan))
            {
                _instruments.Add(instr);
            }

            _dut = OpenTapStepTree.FindDut(plan);
            LoadedPlanPath = path;
            LoadedPlanName = displayName;
            _stepTree = OpenTapStepTree.Build(plan);
            _slots = InstrumentSlotCollector.FromPlan(plan).ToList();
        }

        Raise(nameof(LoadedPlanPath));
        Raise(nameof(LoadedPlanName));
        Raise(nameof(StepTree));
        Raise(nameof(InstrumentSlots));
    }

    public Task ApplyStationAndDutAsync(StationProfile station, DutIdentity dut, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfExecuting("apply station or DUT bindings");
            _dutIdentity = dut;
            if (_dut is not null)
            {
                _dut.SerialNumber = dut.Serial;
                _dut.PartNumber = dut.PartNumber ?? string.Empty;
                _dut.Revision = dut.Revision ?? string.Empty;
                _dut.Family = dut.Family;
            }

            foreach (var slot in _slots)
            {
                if ((station.RoleToResource.TryGetValue(slot.RoleHint, out var resource)
                     || station.RoleToResource.TryGetValue(slot.Name, out resource))
                    && !string.IsNullOrWhiteSpace(resource))
                {
                    TryBindSlotResource_NoLock(slot.Name, resource);
                }
            }

            // Legacy: any "dmm" binding applies to first instrument if role hint missed.
            if (station.RoleToResource.TryGetValue("dmm", out var dmm)
                && !string.IsNullOrWhiteSpace(dmm)
                && _instruments.Count > 0)
            {
                TryBindSlotResource_NoLock(_slots.FirstOrDefault()?.Name ?? _instruments[0].Name, dmm);
            }
        }

        Raise(nameof(InstrumentSlots));
        return Task.CompletedTask;
    }

    public async Task<OpenTapRunSummary> RunSelectionAsync(
        string stepPath,
        IProgress<OpenTapProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? runId = null,
        bool includeCleanup = true)
    {
        if (_plan is null)
        {
            throw new InvalidOperationException("Load a plan before running.");
        }

        var selected = FindStepByPath(stepPath)
                       ?? throw new InvalidOperationException($"Step not found: {stepPath}");
        var mask = OpenTapStepTree.Flatten(_plan).ToDictionary(s => s.Id, s => s.Enabled);

        // Reset only steps that will execute (subtree + optional SafeShutdown). Ancestors stay enabled for
        // OpenTAP structure but keep prior live status; rollup refreshes them afterward.
        var resetStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sampleScopePaths = new List<string>();
        foreach (var step in OpenTapStepTree.Flatten(_plan))
        {
            var isCleanup = includeCleanup && step is SafeShutdownStep;
            if (!OpenTapStepTree.IsInSubtree(step, selected) && !isCleanup)
            {
                continue;
            }

            resetStepIds.Add(step.Id.ToString());
            var node = OpenTapStepTree.FindNode(_stepTree, step.Id.ToString());
            if (node is not null && !string.IsNullOrWhiteSpace(node.Path))
            {
                sampleScopePaths.Add(node.Path);
            }
        }

        try
        {
            var cleanupSteps = includeCleanup
                ? OpenTapStepTree.Flatten(_plan).Where(s => s is SafeShutdownStep).ToList()
                : [];
            foreach (var step in OpenTapStepTree.Flatten(_plan))
            {
                var keep = OpenTapStepTree.IsInSubtree(step, selected)
                           || OpenTapStepTree.IsAncestorOf(step, selected)
                           || cleanupSteps.Any(c =>
                               ReferenceEquals(step, c) || OpenTapStepTree.IsAncestorOf(step, c));
                step.Enabled = keep;
            }

            // Also reset ancestors of cleanup so live status is refreshed.
            foreach (var cleanup in cleanupSteps)
            {
                foreach (var step in OpenTapStepTree.Flatten(_plan))
                {
                    if (ReferenceEquals(step, cleanup) || OpenTapStepTree.IsAncestorOf(step, cleanup))
                    {
                        resetStepIds.Add(step.Id.ToString());
                    }
                }
            }

            RefreshTreeEnabled();
            return await RunAsyncCore(progress, cancellationToken, resetStepIds, sampleScopePaths, runId)
                .ConfigureAwait(false);
        }
        finally
        {
            foreach (var step in OpenTapStepTree.Flatten(_plan))
            {
                if (mask.TryGetValue(step.Id, out var enabled))
                {
                    step.Enabled = enabled;
                }
            }

            RefreshTreeEnabled();
        }
    }

    public Task<OpenTapRunSummary> RunAsync(
        IProgress<OpenTapProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? runId = null)
        => RunAsyncCore(progress, cancellationToken, resetStepIds: null, sampleScopePaths: null, runId);

    private async Task<OpenTapRunSummary> RunAsyncCore(
        IProgress<OpenTapProgress>? progress,
        CancellationToken cancellationToken,
        HashSet<string>? resetStepIds,
        IReadOnlyList<string>? sampleScopePaths,
        string? runId = null)
    {
        IDisposable? benchLease = null;
        if (_bench is not null && !_bench.TryEnter(BenchOperation.Run, out benchLease, out var busy))
        {
            throw new InvalidOperationException(busy);
        }

        if (Interlocked.CompareExchange(ref _runGate, 1, 0) != 0)
        {
            benchLease?.Dispose();
            throw new InvalidOperationException("A run is already in progress.");
        }

        Raise(nameof(IsExecuting));
        try
        {
            return await RunAsyncCoreUnderGate(
                    progress,
                    cancellationToken,
                    resetStepIds,
                    sampleScopePaths,
                    runId)
                .ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _runGate, 0);
            Raise(nameof(IsExecuting));
            benchLease?.Dispose();
        }
    }

    private async Task<OpenTapRunSummary> RunAsyncCoreUnderGate(
        IProgress<OpenTapProgress>? progress,
        CancellationToken cancellationToken,
        HashSet<string>? resetStepIds,
        IReadOnlyList<string>? sampleScopePaths,
        string? runId)
    {
        TestPlan plan;
        List<StoredSample>? preservedSamples = null;
        OpenTapRunContext context;
        lock (_sync)
        {
            plan = _plan ?? throw new InvalidOperationException("Load a plan before running.");
            if (sampleScopePaths is not null)
            {
                preservedSamples = _lastSamples.ToList();
            }

            OpenTapStepTree.ResetLiveState(_stepTree, resetStepIds);
            context = new OpenTapRunContext(_settings, _logger, _cancelExecuteWithToken);
            _activeContext = context;
            // Begin CTS before releasing the lock so Abort in the worker IPC loop cannot
            // miss the run token. Apply live pause here (not a snapshot into BeginRun).
            // Subscribe after BeginRun so its OperatorStateChanged cannot re-enter Pause
            // on this lock via PropertyChanged handlers.
            context.BeginRun(cancellationToken);
            context.OperatorStateChanged += RaiseOperatorState;
            if (_pausedBeforeRun)
            {
                context.Pause();
            }
        }

        RaiseOperatorState();
        try
        {
            var summary = await context.ExecuteAsync(
                    plan,
                    LoadedPlanName,
                    _dutIdentity,
                    _dut,
                    progress,
                    cancellationToken,
                    runId ?? string.Empty,
                    UpdateNodeLive,
                    ResolveStepPath,
                    preservedSamples,
                    sampleScopePaths)
                .ConfigureAwait(false);
            _lastSamples = summary.Samples.ToList();
            return summary;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeContext, context))
                {
                    _activeContext = null;
                }

                _pausedBeforeRun = false;
            }

            context.OperatorStateChanged -= RaiseOperatorState;
            context.Dispose();
            RaiseOperatorState();
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            _pausedBeforeRun = true;
            _activeContext?.Pause();
        }
    }

    public void Resume(OperatorInteractionResponse? response = null)
    {
        lock (_sync)
        {
            _pausedBeforeRun = false;
            _activeContext?.Resume(response);
        }

        RaiseOperatorState();
    }

    public void Abort(bool safetyStop = false)
    {
        lock (_sync)
        {
            _pausedBeforeRun = false;
            _activeContext?.Abort();
        }

        RaiseOperatorState();
        _logger.Warning("OpenTAP abort requested (safety={Safety})", safetyStop);
    }

    private void UpdateNodeLive(string stepId, string? name, string status, string verdict, string? keyValue)
    {
        var node = OpenTapStepTree.FindNode(_stepTree, stepId) ?? OpenTapStepTree.FindNodeByName(_stepTree, name);
        if (node is null)
        {
            return;
        }

        node.StatusText = status;
        node.Verdict = verdict;
        if (keyValue is not null)
        {
            node.KeyValue = keyValue;
        }

        Raise(nameof(StepTree));
    }

    private string? ResolveStepPath(string stepId, string? name)
    {
        var node = OpenTapStepTree.FindNode(_stepTree, stepId) ?? OpenTapStepTree.FindNodeByName(_stepTree, name);
        return node?.Path;
    }

    private ITestStep? FindStepByPath(string stepPath)
    {
        if (_plan is null || string.IsNullOrWhiteSpace(stepPath))
        {
            return null;
        }

        return OpenTapStepTree.Flatten(_plan).FirstOrDefault(s =>
        {
            var node = OpenTapStepTree.FindNode(_stepTree, s.Id.ToString());
            return (node is not null && string.Equals(node.Path, stepPath, StringComparison.OrdinalIgnoreCase))
                   || string.Equals(s.Name, stepPath, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void RefreshTreeEnabled()
    {
        if (_plan is null)
        {
            return;
        }

        void Sync(OpenTapStepNode node, ITestStep step)
        {
            node.Enabled = step.Enabled;
            for (var i = 0; i < Math.Min(node.Children.Count, step.ChildTestSteps.Count); i++)
            {
                Sync(node.Children[i], step.ChildTestSteps[i]);
            }
        }

        for (var i = 0; i < Math.Min(_stepTree.Count, _plan.ChildTestSteps.Count); i++)
        {
            Sync(_stepTree[i], _plan.ChildTestSteps[i]);
        }

        Raise(nameof(StepTree));
    }

    public IReadOnlyList<OpenTapPluginDirectoryInfo> ListPluginDirectories()
        => _catalog.ListPluginDirectories();

    public IReadOnlyList<OpenTapPackageInfo> ListInstalledPackages()
        => _catalog.ListInstalledPackages();

    public IReadOnlyList<OpenTapDiscoveredAddress> ListDiscoveredDeviceAddresses()
        => _catalog.ListDiscoveredDeviceAddresses();

    private void ThrowIfExecuting(string action)
    {
        if (IsExecuting)
        {
            throw new InvalidOperationException($"Cannot {action} while a run is in progress.");
        }
    }

    private void RaiseOperatorState()
    {
        Raise(nameof(IsAwaitingOperator));
        Raise(nameof(OperatorPromptMessage));
        Raise(nameof(PendingInteraction));
    }

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
