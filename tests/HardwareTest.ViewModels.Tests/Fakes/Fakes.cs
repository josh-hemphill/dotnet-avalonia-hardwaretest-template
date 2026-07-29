using System.ComponentModel;
using HardwareTest.Core.Engine;
using HardwareTest.Core.Hardware;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;

namespace HardwareTest.ViewModels.Tests.Fakes;

public sealed class FakeOpenTapSession : IOpenTapSession
{
    private CancellationTokenSource? _runCts;
    private bool _isAwaitingOperator;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? LoadedPlanPath { get; private set; } = SampleProgramFactory.EmbeddedName;
    public string? LoadedPlanName { get; private set; } = "Sample Hardware Suite";
    public List<OpenTapStepNode> Tree { get; } = [BuildSampleTree()];

    public List<OpenTapInstrumentSlot> Slots { get; } =
    [
        new()
        {
            Name = "DMM",
            TypeName = "MockDmmInstrument",
            RoleHint = "dmm",
            ResourceName = "MOCK::INSTR0",
        },
    ];

    public IReadOnlyList<OpenTapStepNode> StepTree => Tree;
    public IReadOnlyList<OpenTapInstrumentSlot> InstrumentSlots => Slots;
    public bool IsAwaitingOperator
    {
        get => _isAwaitingOperator;
        set
        {
            if (_isAwaitingOperator == value)
            {
                return;
            }

            _isAwaitingOperator = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAwaitingOperator)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OperatorPromptMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingInteraction)));
        }
    }
    public string? OperatorPromptMessage { get; set; }
    public OperatorInteractionRequest? PendingInteraction { get; private set; }
    public OperatorInteractionResponse? LastInteractionResponse { get; private set; }
    public Queue<OperatorInteractionResponse> InteractionResponses { get; } = new();
    public Dictionary<string, string> ParameterValues { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<OpenTapParameterInfo> ParameterCatalog { get; } = [];

    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(30);
    public bool EmitLoopProgress { get; set; }
    public RunResult CompletionResult { get; set; } = RunResult.Passed;
    public int RunCount { get; private set; }
    public int SelectionRunCount { get; private set; }
    public bool ReportSamples { get; set; } = true;
    /// When true with ReportSamples, also emits a scalar presentation Sample for gauge tiles.
    public bool ReportPresentationMetrics { get; set; } = true;
    public DutIdentity? LastDut { get; private set; }
    public StationProfile? LastStation { get; private set; }
    public string? LastSelectionPath { get; private set; }

    public Task LoadPlanAsync(string tapPlanPath, CancellationToken cancellationToken = default)
    {
        LoadedPlanPath = tapPlanPath;
        LoadedPlanName = Path.GetFileNameWithoutExtension(tapPlanPath);
        return Task.CompletedTask;
    }

    public Task LoadSampleProgramAsync(CancellationToken cancellationToken = default)
    {
        LoadedPlanPath = SampleProgramFactory.EmbeddedName;
        LoadedPlanName = "Sample Hardware Suite";
        Tree.Clear();
        Tree.Add(BuildSampleTree());
        return Task.CompletedTask;
    }

    public Task LoadBoardDemoProgramAsync(CancellationToken cancellationToken = default)
    {
        LoadedPlanPath = BoardDemoProgramFactory.EmbeddedName;
        LoadedPlanName = BoardDemoProgramFactory.DisplayName;
        Tree.Clear();
        Tree.Add(BuildBoardDemoTree());
        return Task.CompletedTask;
    }

    public Task LoadSweepDemoProgramAsync(CancellationToken cancellationToken = default)
    {
        LoadedPlanPath = SweepDemoProgramFactory.EmbeddedName;
        LoadedPlanName = SweepDemoProgramFactory.DisplayName;
        Tree.Clear();
        foreach (var node in BuildSweepDemoTrees())
        {
            Tree.Add(node);
        }

        return Task.CompletedTask;
    }

    public Task LoadPlanShapeAsync(string fixtureFileName, CancellationToken cancellationToken = default)
    {
        LoadedPlanPath = fixtureFileName;
        LoadedPlanName = Path.GetFileNameWithoutExtension(fixtureFileName);
        Tree.Clear();
        foreach (var root in BuildPlanShapeTrees(fixtureFileName))
        {
            Tree.Add(root);
        }

        return Task.CompletedTask;
    }

    /// Replace the in-memory tree (for UI/board tests without OpenTAP).
    public void LoadTreeFromNodes(params OpenTapStepNode[] roots)
    {
        Tree.Clear();
        Tree.AddRange(roots);
    }

    /// Apply a checked-in progress/summary cassette to the current tree.
    public OpenTapRunSummary ReplayRecording(string directory, string baseName)
    {
        var recording = OpenTapRunRecordingStore.LoadBeside(directory, baseName);
        LastReplayedRecording = recording;
        ApplySummaryToTree(recording.Summary);
        return recording.Summary.ToSummary();
    }

    public OpenTapRunRecording? LastReplayedRecording { get; private set; }

    private void ApplySummaryToTree(OpenTapRunSummaryDto summary)
    {
        foreach (var node in Flatten(Tree))
        {
            node.StatusText = "Pending";
            node.Verdict = "NotSet";
        }

        foreach (var step in summary.Steps)
        {
            var node = Flatten(Tree).FirstOrDefault(n =>
                (!string.IsNullOrWhiteSpace(step.StepPath)
                 && string.Equals(n.Path, step.StepPath, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(step.StepId)
                    && string.Equals(n.Id, step.StepId, StringComparison.OrdinalIgnoreCase)));
            if (node is null)
            {
                continue;
            }

            node.StatusText = step.Passed ? "Pass" : "Fail";
            node.Verdict = step.Passed ? "Pass" : "Fail";
            node.KeyValue = step.Message;
        }

        if (summary.Steps.Count == 0)
        {
            MarkTreeStatuses(summary.Result);
        }
    }

    private static IEnumerable<OpenTapStepNode> BuildPlanShapeTrees(string fixtureFileName)
    {
        if (!IsKnownPlanShape(fixtureFileName))
        {
            throw new ArgumentException($"Unknown plan-shape fixture '{fixtureFileName}'.", nameof(fixtureFileName));
        }

        return BuildPlanShapeTreesCore(fixtureFileName);
    }

    private static bool IsKnownPlanShape(string fixtureFileName)
        => string.Equals(fixtureFileName, PlanShapeFixtures.FlatLeavesName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(fixtureFileName, PlanShapeFixtures.DeepNestName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(fixtureFileName, PlanShapeFixtures.DuplicateNamesName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(fixtureFileName, PlanShapeFixtures.EmptyGroupName, StringComparison.OrdinalIgnoreCase)
           || string.Equals(fixtureFileName, PlanShapeFixtures.NoSafeShutdownName, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<OpenTapStepNode> BuildPlanShapeTreesCore(string fixtureFileName)
    {
        static OpenTapStepNode Leaf(string id, string name, string path) => new()
        {
            Id = id,
            Name = name,
            Path = path,
        };

        static OpenTapStepNode Group(string id, string name, string path, params OpenTapStepNode[] children) => new()
        {
            Id = id,
            Name = name,
            Path = path,
            IsStage = true,
            Children = children.ToList(),
        };

        if (string.Equals(fixtureFileName, PlanShapeFixtures.FlatLeavesName, StringComparison.OrdinalIgnoreCase))
        {
            yield return Leaf("a", "Leaf A", "Leaf A");
            yield return Leaf("b", "Leaf B", "Leaf B");
            yield return Leaf("ss", "Safe Shutdown", "Safe Shutdown");
            yield break;
        }

        if (string.Equals(fixtureFileName, PlanShapeFixtures.DeepNestName, StringComparison.OrdinalIgnoreCase))
        {
            var leaf = Leaf("deep", "Deep Acquire", "Deep Nest Suite/Level1/Level2/Level3/Level4/Deep Acquire");
            var l4 = Group("l4", "Level4", "Deep Nest Suite/Level1/Level2/Level3/Level4", leaf);
            var l3 = Group("l3", "Level3", "Deep Nest Suite/Level1/Level2/Level3", l4);
            var l2 = Group("l2", "Level2", "Deep Nest Suite/Level1/Level2", l3);
            var l1 = Group("l1", "Level1", "Deep Nest Suite/Level1", l2);
            yield return Group(
                "deep-root",
                "Deep Nest Suite",
                "Deep Nest Suite",
                l1,
                Leaf("ss", "Safe Shutdown", "Deep Nest Suite/Safe Shutdown"));
            yield break;
        }

        if (string.Equals(fixtureFileName, PlanShapeFixtures.DuplicateNamesName, StringComparison.OrdinalIgnoreCase))
        {
            yield return Group(
                "dup",
                "Duplicate Names Suite",
                "Duplicate Names Suite",
                Group(
                    "ba",
                    "Bank A",
                    "Duplicate Names Suite/Bank A",
                    Leaf("a1", "Acquire", "Duplicate Names Suite/Bank A/Acquire")),
                Group(
                    "bb",
                    "Bank B",
                    "Duplicate Names Suite/Bank B",
                    Leaf("a2", "Acquire", "Duplicate Names Suite/Bank B/Acquire")),
                Leaf("ss", "Safe Shutdown", "Duplicate Names Suite/Safe Shutdown"));
            yield break;
        }

        if (string.Equals(fixtureFileName, PlanShapeFixtures.EmptyGroupName, StringComparison.OrdinalIgnoreCase))
        {
            yield return Group(
                "empty",
                "Empty Group Suite",
                "Empty Group Suite",
                Group("es", "Empty Section", "Empty Group Suite/Empty Section"),
                Group(
                    "ps",
                    "Populated Section",
                    "Empty Group Suite/Populated Section",
                    Leaf("id", "Identity Check", "Empty Group Suite/Populated Section/Identity Check")),
                Leaf("ss", "Safe Shutdown", "Empty Group Suite/Safe Shutdown"));
            yield break;
        }

        yield return Group(
            "nss",
            "No Safe Shutdown Suite",
            "No Safe Shutdown Suite",
            Leaf("acq", "Only Acquire", "No Safe Shutdown Suite/Only Acquire"));
    }

    private static OpenTapStepNode BuildSampleTree() => new()
    {
        Id = "root",
        Name = "Sample Hardware Suite",
        Path = "Sample Hardware Suite",
        IsStage = true,
        Children =
        [
            new()
            {
                Id = "id",
                Name = "Identity",
                Path = "Sample Hardware Suite/Identity",
                IsStage = true,
                Children =
                [
                    new() { Id = "id-check", Name = "Identity Check", Path = "Sample Hardware Suite/Identity/Identity Check" },
                ],
            },
            new()
            {
                Id = "confirm-clear",
                Name = "Confirm Sweep Area Clear",
                Path = "Sample Hardware Suite/Confirm Sweep Area Clear",
            },
            new()
            {
                Id = "fixture",
                Name = "Install Sweep Fixture",
                Path = "Sample Hardware Suite/Install Sweep Fixture",
            },
            new()
            {
                Id = "sweep",
                Name = "Voltage Sweep",
                Path = "Sample Hardware Suite/Voltage Sweep",
                IsStage = true,
                Children =
                [
                    new()
                    {
                        Id = SampleProgramFactory.AcquireStepId.ToString(),
                        Name = "Acquire VDC",
                        Path = "Sample Hardware Suite/Voltage Sweep/Acquire VDC",
                    },
                    new()
                    {
                        Id = SampleProgramFactory.MeanGteStepId.ToString(),
                        Name = "Mean GTE",
                        Path = "Sample Hardware Suite/Voltage Sweep/Mean GTE",
                    },
                ],
            },
            new()
            {
                Id = "safety",
                Name = "Safe Shutdown",
                Path = "Sample Hardware Suite/Safe Shutdown",
            },
        ],
    };

    private static OpenTapStepNode BuildBoardDemoTree()
    {
        static OpenTapStepNode Leaf(string id, string name, string path) => new()
        {
            Id = id,
            Name = name,
            Path = path,
        };

        static OpenTapStepNode Group(string id, string name, string path, params OpenTapStepNode[] children) => new()
        {
            Id = id,
            Name = name,
            Path = path,
            IsStage = true,
            Children = children.ToList(),
        };

        var root = BoardDemoProgramFactory.DisplayName;
        return Group(
            "demo-root",
            root,
            root,
            Group(
                "power",
                "Power Rails",
                $"{root}/Power Rails",
                Group(
                    "3v3",
                    "3V3 Rail",
                    $"{root}/Power Rails/3V3 Rail",
                    Leaf(BoardDemoProgramFactory.Acquire3V3StepId.ToString(), "Acquire 3V3", $"{root}/Power Rails/3V3 Rail/Acquire 3V3"),
                    Leaf(BoardDemoProgramFactory.MeanGte3V3StepId.ToString(), "Mean GTE 3V3", $"{root}/Power Rails/3V3 Rail/Mean GTE 3V3")),
                Group(
                    "5v",
                    "5V Rail",
                    $"{root}/Power Rails/5V Rail",
                    Leaf("acq-5v", "Acquire 5V", $"{root}/Power Rails/5V Rail/Acquire 5V"),
                    Leaf("mean-5v", "Mean GTE 5V", $"{root}/Power Rails/5V Rail/Mean GTE 5V"))),
            Group(
                "comms",
                "Communications",
                $"{root}/Communications",
                Group(
                    "id",
                    "Identity",
                    $"{root}/Communications/Identity",
                    Leaf("id-check", "Identity Check", $"{root}/Communications/Identity/Identity Check")),
                Group(
                    "bus",
                    "Bus Stress",
                    $"{root}/Communications/Bus Stress",
                    Leaf("long-acq", "Long Acquire VDC", $"{root}/Communications/Bus Stress/Long Acquire VDC"),
                    Leaf("mean-bus", "Mean GTE Bus", $"{root}/Communications/Bus Stress/Mean GTE Bus"))),
            Group(
                "op",
                "Operator",
                $"{root}/Operator",
                Leaf("prompt", "Seat Board Fixture", $"{root}/Operator/Seat Board Fixture"),
                Leaf("sticker", "Record Board Sticker", $"{root}/Operator/Record Board Sticker")),
            Group(
                "safe",
                "Safety",
                $"{root}/Safety",
                Leaf("shutdown", "Safe Shutdown", $"{root}/Safety/Safe Shutdown")));
    }

    private static IEnumerable<OpenTapStepNode> BuildSweepDemoTrees()
    {
        static OpenTapStepNode Leaf(string id, string name, string path) => new()
        {
            Id = id,
            Name = name,
            Path = path,
        };

        static OpenTapStepNode Group(string id, string name, string path, params OpenTapStepNode[] children) => new()
        {
            Id = id,
            Name = name,
            Path = path,
            IsStage = true,
            Children = children.ToList(),
        };

        yield return Group(
            "repeat",
            "Repeat Sweep",
            "Repeat Sweep",
            Leaf("acq", "Acquire VDC", "Repeat Sweep/Acquire VDC"));
        yield return Leaf("ss", "Safe Shutdown", "Safe Shutdown");
    }

    public Task ApplyStationAndDutAsync(StationProfile station, DutIdentity dut, CancellationToken cancellationToken = default)
    {
        LastStation = station;
        LastDut = dut;
        return Task.CompletedTask;
    }

    public async Task<OpenTapRunSummary> RunAsync(IProgress<OpenTapProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        RunCount++;
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        progress?.Report(new OpenTapProgress { Message = "Started", OverallPercent = 0 });
        if (EmitLoopProgress)
        {
            for (var i = 1; i <= 3; i++)
            {
                progress?.Report(new OpenTapProgress
                {
                    Message = $"Loop body {i}/3",
                    StepName = "Acquire VDC",
                    StatusText = "Running",
                    OverallPercent = i * 20,
                    IterationIndex = i,
                    IterationTotal = 3,
                    IterationText = $"{i}/3",
                });
            }
        }

        if (ReportSamples)
        {
            var acquirePath = Flatten(Tree).FirstOrDefault(n =>
                n.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase))?.Path
                ?? Flatten(Tree).FirstOrDefault(n => n.Children.Count == 0)?.Path;
            var meanPath = Flatten(Tree).FirstOrDefault(n =>
                n.Name.Contains("Mean", StringComparison.OrdinalIgnoreCase))?.Path
                ?? acquirePath;

            progress?.Report(new OpenTapProgress
            {
                Message = "Sample",
                StepPath = acquirePath,
                StepName = "Acquire VDC",
                Sample = new MeasurementSampleEvent(
                    "VDC",
                    0,
                    1.25,
                    DateTimeOffset.UtcNow,
                    MetricKey: "VDC",
                    DisplayRole: "timeseries",
                    Unit: "V"),
                OverallPercent = 40,
            });

            if (ReportPresentationMetrics)
            {
                progress?.Report(new OpenTapProgress
                {
                    Message = "VDC.mean [scalar] 1.25 V",
                    StepPath = meanPath,
                    StepName = "Mean GTE",
                    KeyValue = "VDC.mean [scalar] 1.25 V",
                    StatusText = "scalar",
                    Sample = new MeasurementSampleEvent(
                        "Mean",
                        0,
                        1.25,
                        DateTimeOffset.UtcNow,
                        MetricKey: "VDC.mean",
                        DisplayRole: "scalar",
                        Unit: "V",
                        LimitLow: 0),
                    OverallPercent = 70,
                });
            }
        }

        try
        {
            await Task.Delay(Delay, _runCts.Token);
        }
        catch (OperationCanceledException)
        {
            MarkTreeStatuses(RunResult.Cancelled);
            var cancelled = new OpenTapRunSummary
            {
                RunId = Guid.NewGuid().ToString("N"),
                PlanName = LoadedPlanName ?? "plan",
                Result = RunResult.Cancelled,
                DutSerial = LastDut?.Serial,
                DutPartNumber = LastDut?.PartNumber,
                DutRevision = LastDut?.Revision,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            progress?.Report(new OpenTapProgress
            {
                Message = "Cancelled",
                OverallPercent = 100,
                IsCompleted = true,
                Result = RunResult.Cancelled,
            });
            return cancelled;
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
        }

        MarkTreeStatuses(CompletionResult);
        var leaf = Flatten(Tree).FirstOrDefault(n => n.Children.Count == 0);
        var summary = new OpenTapRunSummary
        {
            RunId = Guid.NewGuid().ToString("N"),
            PlanName = LoadedPlanName ?? "plan",
            Result = CompletionResult,
            DutSerial = LastDut?.Serial,
            DutPartNumber = LastDut?.PartNumber,
            DutRevision = LastDut?.Revision,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            Samples =
            [
                new StoredSample
                {
                    Channel = "VDC",
                    MetricKey = "VDC",
                    DisplayRole = "timeseries",
                    Unit = "V",
                    Timestamp = DateTimeOffset.UtcNow,
                    Value = 1.25,
                },
                new StoredSample
                {
                    Channel = "Mean",
                    MetricKey = "VDC.mean",
                    DisplayRole = "scalar",
                    Unit = "V",
                    LimitLow = 0,
                    Timestamp = DateTimeOffset.UtcNow,
                    Value = 1.25,
                },
            ],
            Steps = Flatten(Tree)
                .Where(n => n.Children.Count == 0)
                .Select(n => new StepResultRecord
                {
                    StepId = n.Id,
                    StepType = n.Name,
                    StepPath = n.Path,
                    Passed = CompletionResult == RunResult.Passed,
                    Message = CompletionResult == RunResult.Passed ? "Pass" : "Fail",
                    StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
                    CompletedAt = DateTimeOffset.UtcNow,
                })
                .DefaultIfEmpty(new StepResultRecord
                {
                    StepId = leaf?.Id ?? "Identity Check",
                    StepType = leaf?.Name ?? "Identity Check",
                    StepPath = leaf?.Path ?? "Sample Hardware Suite/Identity/Identity Check",
                    Passed = CompletionResult == RunResult.Passed,
                    Message = CompletionResult == RunResult.Passed ? "Pass" : "Fail",
                    StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
                    CompletedAt = DateTimeOffset.UtcNow,
                })
                .ToList(),
        };
        progress?.Report(new OpenTapProgress
        {
            Message = $"Completed: {summary.Result}",
            OverallPercent = 100,
            IsCompleted = true,
            Result = summary.Result,
            StepId = leaf?.Id,
            StepPath = leaf?.Path,
            StatusText = CompletionResult == RunResult.Passed ? "Pass" : "Fail",
            Verdict = CompletionResult == RunResult.Passed ? "Pass" : "Fail",
        });
        return summary;
    }

    private void MarkTreeStatuses(RunResult result, Func<OpenTapStepNode, bool>? include = null)
    {
        var status = result switch
        {
            RunResult.Passed => "Pass",
            RunResult.Cancelled => "Cancelled",
            _ => "Fail",
        };
        foreach (var node in Flatten(Tree))
        {
            if (include is not null && !include(node))
            {
                continue;
            }

            node.StatusText = status;
            node.Verdict = status;
        }
    }

    private static IEnumerable<OpenTapStepNode> Flatten(IEnumerable<OpenTapStepNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static OpenTapStepNode? FindNode(IEnumerable<OpenTapStepNode> roots, string stepPath)
        => Flatten(roots).FirstOrDefault(n =>
            string.Equals(n.Path, stepPath, StringComparison.OrdinalIgnoreCase));

    private static bool IsNodeInSelectionScope(OpenTapStepNode node, string selectionPath)
    {
        if (string.Equals(node.Path, selectionPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = selectionPath.TrimEnd('/') + "/";
        if (node.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // SafeShutdown stays enabled on real selection runs.
        return node.Name.Contains("Safe Shutdown", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<OpenTapRunSummary> RunSelectionAsync(
        string stepPath,
        IProgress<OpenTapProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        SelectionRunCount++;
        LastSelectionPath = stepPath;

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        progress?.Report(new OpenTapProgress { Message = "Started selection", OverallPercent = 0 });

        try
        {
            await Task.Delay(Delay, _runCts.Token);
        }
        catch (OperationCanceledException)
        {
            MarkTreeStatuses(RunResult.Cancelled, n => IsNodeInSelectionScope(n, stepPath));
            var cancelled = new OpenTapRunSummary
            {
                RunId = Guid.NewGuid().ToString("N"),
                PlanName = LoadedPlanName ?? "plan",
                Result = RunResult.Cancelled,
                DutSerial = LastDut?.Serial,
                DutPartNumber = LastDut?.PartNumber,
                DutRevision = LastDut?.Revision,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
            };
            progress?.Report(new OpenTapProgress
            {
                Message = "Cancelled",
                OverallPercent = 100,
                IsCompleted = true,
                Result = RunResult.Cancelled,
            });
            return cancelled;
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
        }

        // Reset then mark only the selection scope so siblings keep prior live status.
        foreach (var node in Flatten(Tree).Where(n => IsNodeInSelectionScope(n, stepPath)))
        {
            node.StatusText = "Pending";
            node.Verdict = "NotSet";
            node.KeyValue = null;
        }

        MarkTreeStatuses(CompletionResult, n => IsNodeInSelectionScope(n, stepPath));

        var scopeLeaves = Flatten(Tree)
            .Where(n => n.Children.Count == 0 && IsNodeInSelectionScope(n, stepPath))
            .ToList();
        var leaf = scopeLeaves.FirstOrDefault()
                   ?? Flatten(Tree).FirstOrDefault(n => n.Children.Count == 0);

        var summary = new OpenTapRunSummary
        {
            RunId = Guid.NewGuid().ToString("N"),
            PlanName = LoadedPlanName ?? "plan",
            Result = CompletionResult,
            DutSerial = LastDut?.Serial,
            DutPartNumber = LastDut?.PartNumber,
            DutRevision = LastDut?.Revision,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            Samples =
            [
                new StoredSample { Channel = "VDC", Timestamp = DateTimeOffset.UtcNow, Value = 1.25, StepPath = leaf?.Path ?? stepPath },
            ],
            Steps = scopeLeaves
                .Select(n => new StepResultRecord
                {
                    StepId = n.Id,
                    StepType = n.Name,
                    StepPath = n.Path,
                    Passed = CompletionResult == RunResult.Passed,
                    Message = CompletionResult == RunResult.Passed ? "Pass" : "Fail",
                    StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
                    CompletedAt = DateTimeOffset.UtcNow,
                })
                .DefaultIfEmpty(new StepResultRecord
                {
                    StepId = leaf?.Id ?? "Identity Check",
                    StepType = leaf?.Name ?? "Identity Check",
                    StepPath = leaf?.Path ?? stepPath,
                    Passed = CompletionResult == RunResult.Passed,
                    Message = CompletionResult == RunResult.Passed ? "Pass" : "Fail",
                    StartedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
                    CompletedAt = DateTimeOffset.UtcNow,
                })
                .ToList(),
        };
        progress?.Report(new OpenTapProgress
        {
            Message = $"Completed: {summary.Result}",
            OverallPercent = 100,
            IsCompleted = true,
            Result = summary.Result,
            StepId = leaf?.Id,
            StepPath = leaf?.Path,
            StatusText = CompletionResult == RunResult.Passed ? "Pass" : "Fail",
            Verdict = CompletionResult == RunResult.Passed ? "Pass" : "Fail",
        });
        return summary;
    }

    public void Pause()
    {
        // Soft pause only (matches OpenTapSession). Operator prompts use BeginInteraction.
    }

    public void Resume(OperatorInteractionResponse? response = null)
    {
        if (PendingInteraction is not null)
        {
            LastInteractionResponse = response
                ?? (InteractionResponses.Count > 0
                    ? InteractionResponses.Dequeue()
                    : OperatorInteractionResponse.Continue(PendingInteraction.Id));
        }

        IsAwaitingOperator = false;
        OperatorPromptMessage = null;
        PendingInteraction = null;
    }

    /// Simulates a step requesting interaction (for ViewModel tests without OpenTAP).
    public void BeginInteraction(OperatorInteractionRequest request)
    {
        PendingInteraction = request;
        IsAwaitingOperator = true;
        OperatorPromptMessage = request.Message;
        if (InteractionResponses.Count > 0)
        {
            Resume(InteractionResponses.Dequeue());
        }
    }

    public void Abort(bool safetyStop = false)
    {
        try
        {
            _runCts?.Cancel();
        }
        catch
        {
            // ignore
        }
    }

    public bool TrySetStepEnabled(string stepPath, bool enabled) => true;
    public bool TrySetAcquireSettings(string stepPath, int? sampleCount, int? intervalMs) => true;
    public bool TrySetMeanGteThreshold(string stepPath, double threshold) => true;

    public bool TryGetStepConditionSummary(string stepPath, out string? summary)
    {
        var node = FindNode(Tree, stepPath);
        if (node is null)
        {
            summary = null;
            return false;
        }

        if (node.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase))
        {
            summary = "Samples=32, IntervalMs=5, Enabled=True";
            return true;
        }

        if (node.Name.Contains("Mean", StringComparison.OrdinalIgnoreCase)
            || node.Name.Contains("Check", StringComparison.OrdinalIgnoreCase))
        {
            summary = "Mean ≥ 0, Enabled=True";
            return true;
        }

        summary = $"Enabled={node.Enabled}";
        return true;
    }

    public bool TryRebindDmmResource(string resource) => true;
    public bool TryBindSlotResource(string slotName, string resource)
    {
        var slot = Slots.FirstOrDefault(s => string.Equals(s.Name, slotName, StringComparison.OrdinalIgnoreCase));
        if (slot is null)
        {
            return false;
        }

        slot.ResourceName = resource;
        return true;
    }

    public IReadOnlyList<OpenTapParameterInfo> EnumerateParameters(
        OpenTapParameterScope scope,
        string? stepPath = null,
        bool includeReadOnly = false,
        OpenTapParameterListing listing = OpenTapParameterListing.StationOverrides)
    {
        if (ParameterCatalog.Count > 0)
        {
            return ParameterCatalog
                .Where(p =>
                {
                    if (scope == OpenTapParameterScope.Plan)
                    {
                        return p.MemberKey.StartsWith("plan/", StringComparison.OrdinalIgnoreCase);
                    }

                    return string.IsNullOrWhiteSpace(stepPath)
                           || string.Equals(p.StepPath, stepPath, StringComparison.OrdinalIgnoreCase);
                })
                .Where(p => includeReadOnly || !p.IsReadOnly)
                .Where(p => listing != OpenTapParameterListing.StationOverrides
                            || p.Role == OpenTapParameterRole.StationOverride)
                .Select(CloneWithLiveValue)
                .ToList();
        }

        if (scope != OpenTapParameterScope.Step || string.IsNullOrWhiteSpace(stepPath))
        {
            return [];
        }

        var node = FindNode(Tree, stepPath);
        if (node is null)
        {
            return [];
        }

        var list = new List<OpenTapParameterInfo>();
        var isPromptStep = node.Name.Contains("Install", StringComparison.OrdinalIgnoreCase)
                           || node.Name.Contains("Prompt", StringComparison.OrdinalIgnoreCase)
                           || node.Name.Contains("Fixture", StringComparison.OrdinalIgnoreCase);

        if (!isPromptStep && node.Name.Contains("Acquire", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(MakeParam(node, "SampleCount", "Sample Count", OperatorInteractionFieldKind.Number, "32"));
            list.Add(MakeParam(node, "IntervalMs", "Interval Ms", OperatorInteractionFieldKind.Number, "5"));
            list.Add(MakeParam(node, "Channel", "Channel", OperatorInteractionFieldKind.String, "VDC"));
            list.Add(MakeParam(
                node,
                "ChannelKey",
                "Channel key",
                OperatorInteractionFieldKind.String,
                "VDC",
                group: "Presentation",
                isMixinEmbedded: true));
            list.Add(MakeParam(
                node,
                "DisplayRole",
                "Display role",
                OperatorInteractionFieldKind.String,
                "timeseries",
                group: "Presentation",
                isMixinEmbedded: true));
            list.Add(MakeParam(
                node,
                "YUnit",
                "Y unit",
                OperatorInteractionFieldKind.String,
                "V",
                group: "Presentation",
                isMixinEmbedded: true));
        }

        if (!isPromptStep && node.Name.Contains("Mean", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(MakeParam(node, "SampleCount", "Sample Count", OperatorInteractionFieldKind.Number, "8"));
            list.Add(MakeParam(node, "Threshold", "Threshold", OperatorInteractionFieldKind.Number, "0"));
            list.Add(MakeParam(
                node,
                "ChannelKey",
                "Channel key",
                OperatorInteractionFieldKind.String,
                "VDC.mean",
                group: "Presentation",
                isMixinEmbedded: true));
            list.Add(MakeParam(
                node,
                "DisplayRole",
                "Display role",
                OperatorInteractionFieldKind.String,
                "scalar",
                group: "Presentation",
                isMixinEmbedded: true));
            list.Add(MakeParam(
                node,
                "YUnit",
                "Y unit",
                OperatorInteractionFieldKind.String,
                "V",
                group: "Presentation",
                isMixinEmbedded: true));
        }

        if (node.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(MakeParam(
                node,
                "Note",
                "Note",
                OperatorInteractionFieldKind.String,
                string.Empty,
                group: "Annotation",
                isMixinEmbedded: true));
            list.Add(MakeParam(
                node,
                "IncludeInReport",
                "Include in report",
                OperatorInteractionFieldKind.Boolean,
                "false",
                group: "Annotation",
                isMixinEmbedded: true));
        }

        if (isPromptStep && listing == OpenTapParameterListing.AllEditable)
        {
            list.Add(MakeParam(
                node,
                "Message",
                "Message",
                OperatorInteractionFieldKind.String,
                "Paused for operator",
                OpenTapParameterRole.OperatorPromptSchema));
        }

        list.Add(MakeParam(node, "Enabled", "Enabled", OperatorInteractionFieldKind.Boolean, node.Enabled ? "true" : "false"));
        return list
            .Where(p => includeReadOnly || !p.IsReadOnly)
            .Where(p => listing != OpenTapParameterListing.StationOverrides
                        || p.Role == OpenTapParameterRole.StationOverride)
            .Select(CloneWithLiveValue)
            .ToList();
    }

    public bool TryGetParameter(string memberKey, out string? value)
    {
        if (ParameterValues.TryGetValue(memberKey, out var stored))
        {
            value = stored;
            return true;
        }

        var match = EnumerateParameters(OpenTapParameterScope.Step)
            .Concat(EnumerateParameters(OpenTapParameterScope.Plan))
            .FirstOrDefault(p => string.Equals(p.MemberKey, memberKey, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            value = null;
            return false;
        }

        value = match.Value;
        return true;
    }

    public bool TrySetParameter(string memberKey, string value)
    {
        ParameterValues[memberKey] = value;
        if (memberKey.EndsWith("/Enabled", StringComparison.OrdinalIgnoreCase)
            && bool.TryParse(value, out var enabled)
            && OpenTapParameterInfo.TryParseMemberKey(memberKey, out var stepId, out _))
        {
            var node = Flatten(Tree).FirstOrDefault(n =>
                string.Equals(n.Id, stepId, StringComparison.OrdinalIgnoreCase));
            if (node is not null)
            {
                node.Enabled = enabled;
            }
        }

        return true;
    }

    public List<OpenTapPluginDirectoryInfo> PluginDirectories { get; } =
    [
        new() { Path = @"C:\Plugins\Basic", Source = "Basic" },
        new() { Path = @"C:\Plugins\Extra", Source = "Settings" },
    ];

    public List<OpenTapPackageInfo> InstalledPackages { get; } =
    [
        new() { Name = "OpenTAP", Version = "9.32.2", Path = @"C:\OpenTAP\Packages\OpenTAP" },
        new() { Name = "HardwareTest.Basic", Version = "1.0.0", Path = @"C:\Plugins\Basic" },
    ];

    public IReadOnlyList<OpenTapPluginDirectoryInfo> ListPluginDirectories() => PluginDirectories;

    public IReadOnlyList<OpenTapPackageInfo> ListInstalledPackages() => InstalledPackages;

    public List<OpenTapDiscoveredAddress> OpenTapDiscoveredAddresses { get; } =
    [
        new()
        {
            Address = "MOCK::OPENTAP0",
            Source = "FakeDeviceDiscovery",
            Kind = "VisaAddress",
            Interface = "MOCK",
            Detail = "OPENTAP0",
            SupportsMessageQuery = true,
        },
        new()
        {
            Address = "TCPIP0::FAKE::INSTR",
            Source = "FakeDeviceDiscovery",
            Kind = "VisaAddress",
            Interface = "TCPIP",
            Detail = "FAKE",
            SupportsMessageQuery = true,
        },
    ];

    public IReadOnlyList<OpenTapDiscoveredAddress> ListDiscoveredDeviceAddresses() => OpenTapDiscoveredAddresses;

    private OpenTapParameterInfo CloneWithLiveValue(OpenTapParameterInfo info)
    {
        var value = ParameterValues.TryGetValue(info.MemberKey, out var stored) ? stored : info.Value;
        return new OpenTapParameterInfo
        {
            MemberKey = info.MemberKey,
            DisplayName = info.DisplayName,
            Group = info.Group,
            Kind = info.Kind,
            Value = value,
            IsExternal = info.IsExternal,
            IsReadOnly = info.IsReadOnly,
            IsMixinEmbedded = info.IsMixinEmbedded,
            Role = info.Role,
            StepId = info.StepId,
            StepPath = info.StepPath,
        };
    }

    private static OpenTapParameterInfo MakeParam(
        OpenTapStepNode node,
        string memberName,
        string displayName,
        OperatorInteractionFieldKind kind,
        string defaultValue,
        OpenTapParameterRole role = OpenTapParameterRole.StationOverride,
        string? group = null,
        bool isMixinEmbedded = false)
        => new()
        {
            MemberKey = OpenTapParameterInfo.FormatStepMemberKey(node.Id, memberName),
            DisplayName = displayName,
            Group = group,
            Kind = kind,
            Value = defaultValue,
            Role = role,
            IsMixinEmbedded = isMixinEmbedded,
            StepId = node.Id,
            StepPath = node.Path,
        };
}

public sealed class FakeRunControl : IRunControl
{
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private CancellationTokenSource? _runCts;

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsSafetyStopping { get; private set; }
    public bool WasSafetyStopRequested { get; private set; }
    public CancellationToken SafetyShutdownToken => CancellationToken.None;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AttachRun(CancellationTokenSource runCts)
    {
        _runCts = runCts;
        IsRunning = true;
        IsPaused = false;
        IsSafetyStopping = false;
        WasSafetyStopRequested = false;
        _pauseEvent.Set();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
    }

    public void DetachRun()
    {
        _runCts = null;
        IsRunning = false;
        IsPaused = false;
        IsSafetyStopping = false;
        _pauseEvent.Set();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
    }

    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        IsPaused = true;
        _pauseEvent.Reset();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaused)));
    }

    public void Resume()
    {
        IsPaused = false;
        _pauseEvent.Set();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaused)));
    }

    public void RequestCancel()
    {
        Resume();
        _runCts?.Cancel();
    }

    public void RequestSafetyStop()
    {
        WasSafetyStopRequested = true;
        IsSafetyStopping = true;
        Resume();
        _runCts?.Cancel();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSafetyStopping)));
    }

    public void CancelSafetyShutdown()
    {
    }

    public Task WaitIfPausedAsync(CancellationToken cancellationToken = default)
    {
        while (!_pauseEvent.IsSet)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _pauseEvent.Wait(50);
        }

        return Task.CompletedTask;
    }
}

public sealed class FakeReportService : IReportService
{
    public int GenerateCount { get; private set; }
    public string PdfPath { get; set; } = Path.GetTempFileName();
    public IReadOnlyList<string>? LastKinds { get; private set; }

    public async Task<string> GeneratePdfAsync(TestRunRecord run, CancellationToken cancellationToken = default)
    {
        var artifacts = await GenerateReportsAsync(run, [ReportKinds.Status], null, cancellationToken);
        return artifacts.FirstOrDefault()?.PdfPath ?? PdfPath;
    }

    public Task<IReadOnlyList<RunReportArtifact>> GenerateReportsAsync(
        TestRunRecord run,
        IReadOnlyList<string> kinds,
        DutHistoryReport? history = null,
        CancellationToken cancellationToken = default)
    {
        GenerateCount++;
        LastKinds = kinds.ToArray();
        var artifacts = kinds
            .Select(k => new RunReportArtifact
            {
                Kind = k,
                Title = k,
                PdfPath = string.Equals(k, ReportKinds.Status, StringComparison.OrdinalIgnoreCase)
                    ? PdfPath
                    : PdfPath + "." + k + ".pdf",
                GeneratedAt = DateTimeOffset.UtcNow,
            })
            .ToList();
        if (artifacts.Count == 0)
        {
            artifacts.Add(new RunReportArtifact
            {
                Kind = ReportKinds.Status,
                Title = "Status",
                PdfPath = PdfPath,
                GeneratedAt = DateTimeOffset.UtcNow,
            });
        }

        run.Reports = artifacts;
        run.ReportPdfPath = artifacts.FirstOrDefault(a =>
                                string.Equals(a.Kind, ReportKinds.Status, StringComparison.OrdinalIgnoreCase))
                            ?.PdfPath
                            ?? artifacts[0].PdfPath;
        return Task.FromResult((IReadOnlyList<RunReportArtifact>)artifacts);
    }

    public Task<string> GenerateSuitePdfAsync(SuiteRunRecord suiteRun, CancellationToken cancellationToken = default)
    {
        GenerateCount++;
        suiteRun.ReportPdfPath = PdfPath;
        return Task.FromResult(PdfPath);
    }

    public Task<byte[]> CompileTemplateAsync(TestRunRecord run, CancellationToken cancellationToken = default)
        => Task.FromResult("%PDF-fake"u8.ToArray());
}

public sealed class FakeRunStore : IRunStore
{
    private readonly Dictionary<string, TestRunRecord> _runs = new(StringComparer.Ordinal);

    public void Seed(TestRunRecord run) => _runs[run.RunId] = run;

    public Task SaveAsync(TestRunRecord run, CancellationToken cancellationToken = default)
    {
        _runs[run.RunId] = run;
        return Task.CompletedTask;
    }

    public Task<TestRunRecord?> LoadAsync(string runId, CancellationToken cancellationToken = default)
        => Task.FromResult(_runs.TryGetValue(runId, out var r) ? r : null);

    public Task<IReadOnlyList<TestRunSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TestRunSummary> list = _runs.Values
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new TestRunSummary
            {
                RunId = r.RunId,
                PlanName = r.PlanName,
                PlanId = r.PlanId,
                StartedAt = r.StartedAt,
                Result = r.Result,
                DutSerial = r.DutSerial,
                DutPartNumber = r.DutPartNumber,
                SessionId = r.SessionId,
                OperatorName = r.OperatorName,
            })
            .ToArray();
        return Task.FromResult(list);
    }

    public string GetRunDirectory(string runId)
        => Path.Combine(Path.GetTempPath(), "fake-runs", runId);
}

public sealed class FakeSettingsStore : ISettingsStore
{
    public FakeSettingsStore()
    {
        AppSettings = new AppSettings { UseMockVisa = true, DefaultVisaResource = "MOCK::0" };
        UiState = new UiState { SelectedPageId = "Home" };
        RootDirectory = Path.Combine(Path.GetTempPath(), "fake-settings");
        RunsDirectory = Path.Combine(RootDirectory, "runs");
        SettingsPath = Path.Combine(RootDirectory, "settings.json");
        Provenance = [];
        IsSettingsWritable = true;
    }

    public AppSettings AppSettings { get; }
    public UiState UiState { get; }
    public string RootDirectory { get; }
    public string RunsDirectory { get; }
    public string SettingsPath { get; }
    public IReadOnlyList<SettingProvenance> Provenance { get; set; }
    public bool IsSettingsWritable { get; set; }
    public string? LastPersistenceError { get; set; }
    public int SaveAppCount { get; private set; }
    public int SaveUiCount { get; private set; }

    public bool IsOverridden(string key)
        => Provenance.Any(p =>
            string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)
            && p.Source is SettingSource.Environment or SettingSource.CommandLine);

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task LoadAsync(
        IReadOnlyDictionary<string, string>? environmentOverlays,
        IReadOnlyDictionary<string, string>? commandLineOverlays,
        Action<string>? warn = null,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task SaveAppSettingsAsync(CancellationToken cancellationToken = default)
    {
        SaveAppCount++;
        return Task.CompletedTask;
    }

    public Task SaveUiStateAsync(CancellationToken cancellationToken = default)
    {
        SaveUiCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeVisaDiscovery : IVisaResourceDiscovery
{
    public Task<IReadOnlyList<VisaResourceInfo>> FindAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(MockVisaResourceDiscovery.Catalog);
}
