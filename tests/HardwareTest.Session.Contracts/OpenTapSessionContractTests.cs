using System.Reflection;
using System.Text;
using HardwareTest.Core.Runs;
using HardwareTest.OpenTap.Host;
using HardwareTest.OpenTap.Plugins.Basic;
using Xunit;

namespace HardwareTest.Session.Contracts;

/// Behavioral contract for <see cref="IOpenTapSession"/> — assert against both real and fake.
public abstract class OpenTapSessionContractTests
{
    protected abstract Task<IOpenTapSession> CreateUnloadedSessionAsync();

    protected abstract Task<IOpenTapSession> CreateLoadedSessionAsync(ContractPlan plan);

    [Fact]
    public async Task Unloaded_reports_empty_initial_state()
    {
        var session = await CreateUnloadedSessionAsync();
        Assert.True(string.IsNullOrEmpty(session.LoadedPlanName));
        Assert.True(string.IsNullOrEmpty(session.LoadedPlanPath));
        Assert.Empty(session.StepTree);
        Assert.False(session.IsAwaitingOperator);
        Assert.Null(session.PendingInteraction);
    }

    [Theory]
    [MemberData(nameof(AllPlans))]
    public async Task Load_sets_plan_identity_unique_paths_and_safe_shutdown(ContractPlan plan)
    {
        var session = await CreateLoadedSessionAsync(plan);
        Assert.False(string.IsNullOrWhiteSpace(session.LoadedPlanName));
        Assert.False(string.IsNullOrWhiteSpace(session.LoadedPlanPath));
        Assert.NotEmpty(session.StepTree);

        var paths = Flatten(session.StepTree).Select(n => n.Path).ToList();
        Assert.Equal(paths.Count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(
            Flatten(session.StepTree),
            n => n.Name.Contains("Safe Shutdown", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Run_progress_is_monotonic_and_reaches_terminal_verdict()
    {
        var session = await CreateLoadedSessionAsync(ContractPlan.Simple);
        await ApplyDefaultStationAsync(session);

        var percents = new List<double>();
        var progress = new Progress<OpenTapProgress>(p => percents.Add(p.OverallPercent));
        var summary = await session.RunAsync(progress);

        Assert.NotEmpty(percents);
        for (var i = 1; i < percents.Count; i++)
        {
            Assert.True(
                percents[i] + 0.001 >= percents[i - 1],
                $"Progress decreased: {percents[i - 1]} → {percents[i]}");
        }

        Assert.True(IsTerminal(summary.Result), $"Non-terminal result: {summary.Result}");
        Assert.Contains(percents, p => p >= 100 || Math.Abs(p - 100) < 0.001);
    }

    [Fact]
    public async Task Pause_blocks_until_resume_then_run_completes()
    {
        var session = await CreateLoadedSessionAsync(ContractPlan.WithLoop);
        await ApplyDefaultStationAsync(session);

        var completed = new TaskCompletionSource<OpenTapRunSummary>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runTask = session.RunAsync().ContinueWith(
            t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    completed.TrySetResult(t.Result);
                }
                else if (t.IsFaulted)
                {
                    completed.TrySetException(t.Exception!.InnerExceptions);
                }
                else
                {
                    completed.TrySetCanceled();
                }
            },
            TaskScheduler.Default);

        await Task.Delay(20);
        session.Pause();
        var stillRunning = await Task.WhenAny(completed.Task, Task.Delay(150)) != completed.Task;
        Assert.True(stillRunning, "Run completed while paused.");

        session.Resume();
        var finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(30))) == completed.Task;
        Assert.True(finished, "Run did not complete after Resume.");
        var summary = await completed.Task;
        Assert.True(IsTerminal(summary.Result), $"Non-terminal result: {summary.Result}");
        await runTask;
    }

    [Fact]
    public async Task Abort_reaches_terminal_state_and_second_abort_is_noop()
    {
        var session = await CreateLoadedSessionAsync(ContractPlan.WithInteraction);
        await ApplyDefaultStationAsync(session);

        var runTask = session.RunAsync();
        _ = Task.Run(async () =>
        {
            await Task.Delay(40);
            if (session.IsAwaitingOperator)
            {
                session.Resume(session.PendingInteraction is { } pending
                    ? OperatorInteractionResponse.Continue(pending.Id)
                    : null);
            }

            await Task.Delay(30);
            session.Abort(safetyStop: true);
            session.Abort(safetyStop: true);
        });

        var summary = await runTask.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(IsTerminal(summary.Result), $"Non-terminal result: {summary.Result}");
        Assert.Equal(RunResult.Cancelled, summary.Result);
    }

    [Fact]
    public async Task Run_selection_keeps_safe_shutdown_enabled()
    {
        var session = await CreateLoadedSessionAsync(ContractPlan.Simple);
        await ApplyDefaultStationAsync(session);

        var leaf = Flatten(session.StepTree)
            .First(n => n.Children.Count == 0
                        && !n.Name.Contains("Safe Shutdown", StringComparison.OrdinalIgnoreCase));
        var summary = await session.RunSelectionAsync(leaf.Path);
        Assert.True(IsTerminal(summary.Result), $"Non-terminal result: {summary.Result}");

        var shutdown = Flatten(session.StepTree)
            .First(n => n.Name.Contains("Safe Shutdown", StringComparison.OrdinalIgnoreCase));
        Assert.True(shutdown.Enabled, "SafeShutdown must stay enabled under selection mask.");
    }

    [Fact]
    public async Task Interaction_sets_awaiting_and_resume_clears()
    {
        var session = await CreateLoadedSessionAsync(ContractPlan.WithInteraction);
        await ApplyDefaultStationAsync(session);

        var changed = new HashSet<string>(StringComparer.Ordinal);
        session.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
            {
                changed.Add(e.PropertyName);
            }
        };

        var awaiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sawFirst = false;
        var progress = new Progress<OpenTapProgress>(p =>
        {
            if (!p.AwaitingOperator && !session.IsAwaitingOperator)
            {
                return;
            }

            if (session.PendingInteraction is not { } pending)
            {
                return;
            }

            if (!sawFirst)
            {
                sawFirst = true;
                awaiting.TrySetResult(true);
                return;
            }

            // Later prompts in the same plan must also be cleared so the run can finish.
            session.Resume(BuildContinueResponse(pending));
        });

        var runTask = session.RunAsync(progress);
        try
        {
            var saw = await Task.WhenAny(awaiting.Task, Task.Delay(TimeSpan.FromSeconds(30))) == awaiting.Task;
            Assert.True(saw, "Expected IsAwaitingOperator during interaction plan run.");
            Assert.True(session.IsAwaitingOperator);
            var firstPending = session.PendingInteraction;
            Assert.NotNull(firstPending);
            Assert.Contains(nameof(IOpenTapSession.IsAwaitingOperator), changed);
            Assert.Contains(nameof(IOpenTapSession.PendingInteraction), changed);

            session.Resume(BuildContinueResponse(firstPending));

            // Drain any further prompts the progress handler might miss between frames.
            while (!runTask.IsCompleted)
            {
                if (session.IsAwaitingOperator && session.PendingInteraction is { } pending)
                {
                    session.Resume(BuildContinueResponse(pending));
                }

                var done = await Task.WhenAny(runTask, Task.Delay(25));
                if (done == runTask)
                {
                    break;
                }
            }

            var summary = await runTask.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(session.IsAwaitingOperator);
            Assert.Null(session.PendingInteraction);
            Assert.True(IsTerminal(summary.Result), $"Non-terminal result: {summary.Result}");
        }
        finally
        {
            if (!runTask.IsCompleted)
            {
                session.Abort(safetyStop: true);
                try
                {
                    await runTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch
                {
                    // drain
                }
            }
        }
    }

    private static OperatorInteractionResponse BuildContinueResponse(OperatorInteractionRequest pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in pending.Fields ?? [])
        {
            if (field is null || string.IsNullOrWhiteSpace(field.Id))
            {
                continue;
            }

            values[field.Id] = field.Kind switch
            {
                OperatorInteractionFieldKind.Number => field.DefaultValue ?? "1",
                OperatorInteractionFieldKind.Boolean => field.DefaultValue ?? "true",
                _ => field.DefaultValue ?? "contract",
            };
        }

        return OperatorInteractionResponse.Continue(pending.Id, values);
    }

    [Fact]
    public async Task Parameters_enumerate_round_trip_and_unknown_key_returns_false()
    {
        var session = await CreateLoadedSessionAsync(ContractPlan.Simple);
        var leaf = Flatten(session.StepTree)
            .First(n => n.Children.Count == 0
                        && !n.Name.Contains("Safe Shutdown", StringComparison.OrdinalIgnoreCase));
        var listed = session.EnumerateParameters(
            OpenTapParameterScope.Step,
            stepPath: leaf.Path,
            includeReadOnly: true,
            listing: OpenTapParameterListing.AllEditable);
        Assert.NotNull(listed);
        Assert.NotEmpty(listed);

        var writable = listed.FirstOrDefault(p =>
                         !p.IsReadOnly
                         && p.Kind != OperatorInteractionFieldKind.Boolean
                         && !string.IsNullOrWhiteSpace(p.MemberKey))
                     ?? listed.FirstOrDefault(p => !p.IsReadOnly && !string.IsNullOrWhiteSpace(p.MemberKey));
        Assert.NotNull(writable);

        var keys = listed.Select(p => p.MemberKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var original = writable!.Value;
        string next;
        if (writable.Kind == OperatorInteractionFieldKind.Boolean)
        {
            next = string.Equals(original, "true", StringComparison.OrdinalIgnoreCase) ? "false" : "true";
        }
        else
        {
            next = string.Equals(original, "1", StringComparison.Ordinal) ? "2" : "1";
        }

        Assert.True(session.TrySetParameter(writable.MemberKey, next));
        Assert.True(session.TryGetParameter(writable.MemberKey, out var got));
        Assert.Equal(next, got);

        Assert.False(session.TrySetParameter("00000000-0000-0000-0000-000000000000/NotARealMember", "x"));
        Assert.False(session.TryGetParameter("00000000-0000-0000-0000-000000000000/NotARealMember", out _));
    }

    [Fact]
    public async Task Catalog_apis_return_non_null_and_do_not_throw()
    {
        var session = await CreateUnloadedSessionAsync();
        Assert.NotNull(session.ListPluginDirectories());
        Assert.NotNull(session.ListInstalledPackages());
        Assert.NotNull(session.ListDiscoveredDeviceAddresses());

        var loaded = await CreateLoadedSessionAsync(ContractPlan.Simple);
        Assert.NotNull(loaded.ListPluginDirectories());
        Assert.NotNull(loaded.ListInstalledPackages());
        Assert.NotNull(loaded.ListDiscoveredDeviceAddresses());
    }

    [Fact]
    public void Approved_surface_matches_IOpenTapSession()
    {
        var actual = FormatApprovedSurface(typeof(IOpenTapSession));
        var approvedPath = FindApprovedSnapshotPath();
        Assert.True(File.Exists(approvedPath), $"Missing approved snapshot: {approvedPath}");
        var expected = NormalizeNewlines(File.ReadAllText(approvedPath).TrimEnd() + "\n");
        var normalizedActual = NormalizeNewlines(actual.TrimEnd() + "\n");
        Assert.Equal(expected, normalizedActual);
    }

    public static TheoryData<ContractPlan> AllPlans()
        => [ContractPlan.Simple, ContractPlan.WithLoop, ContractPlan.WithInteraction];

    protected virtual Task ApplyDefaultStationAsync(IOpenTapSession session)
        => session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-CONTRACT", Family: "demo"));

    private static bool IsTerminal(RunResult result)
        => result is RunResult.Passed or RunResult.Failed or RunResult.Error or RunResult.Cancelled;

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

    private static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindApprovedSnapshotPath()
    {
        var asmDir = Path.GetDirectoryName(typeof(OpenTapSessionContractTests).Assembly.Location)!;
        var beside = Path.Combine(asmDir, "IOpenTapSession.approved.txt");
        if (File.Exists(beside))
        {
            return beside;
        }

        // Walk up from this source file's project when running from IDE without copy.
        var dir = new DirectoryInfo(asmDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "HardwareTest.Session.Contracts", "IOpenTapSession.approved.txt");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return beside;
    }

    public static string FormatApprovedSurface(Type type)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Approved surface: {type.FullName}");
        sb.AppendLine("# Update this file in the same commit when IOpenTapSession changes.");

        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                     .OrderBy(m => m.MemberType)
                     .ThenBy(m => m.Name, StringComparer.Ordinal))
        {
            switch (member)
            {
                case PropertyInfo prop:
                    sb.Append("prop ").Append(TypeName(prop.PropertyType)).Append(' ').Append(prop.Name);
                    sb.Append(" {");
                    if (prop.CanRead) sb.Append(" get;");
                    if (prop.CanWrite) sb.Append(" set;");
                    sb.AppendLine(" }");
                    break;
                case MethodInfo method when method.IsSpecialName:
                    break;
                case MethodInfo method:
                    sb.Append("method ").Append(TypeName(method.ReturnType)).Append(' ').Append(method.Name).Append('(');
                    sb.Append(string.Join(", ", method.GetParameters().Select(FormatParam)));
                    sb.AppendLine(")");
                    break;
                case EventInfo evt:
                    sb.Append("event ").Append(TypeName(evt.EventHandlerType!)).Append(' ').AppendLine(evt.Name);
                    break;
            }
        }

        // INotifyPropertyChanged.PropertyChanged is inherited — include it explicitly.
        sb.AppendLine("event System.ComponentModel.PropertyChangedEventHandler PropertyChanged");
        return sb.ToString();
    }

    private static string FormatParam(ParameterInfo p)
    {
        var prefix = p.IsOut ? "out " : p.ParameterType.IsByRef && !p.IsOut ? "ref " : string.Empty;
        var type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
        return $"{prefix}{TypeName(type)} {p.Name}";
    }

    private static string TypeName(Type type)
    {
        if (type == typeof(void)) return "void";
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(int)) return "int";
        if (type == typeof(double)) return "double";
        if (type.IsGenericType)
        {
            var name = type.Name;
            var tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick > 0) name = name[..tick];
            var args = string.Join(", ", type.GetGenericArguments().Select(TypeName));
            return $"{type.Namespace}.{name}<{args}>";
        }

        if (type.IsArray)
        {
            return TypeName(type.GetElementType()!) + "[]";
        }

        return type.FullName ?? type.Name;
    }
}
