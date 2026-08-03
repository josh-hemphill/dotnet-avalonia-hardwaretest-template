# Fresh-eyes review after Phase 15 (findings → phases)

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Status:** Findings addressed in follow-up commits on this branch (F1–F6 implemented with regression tests). Kept as the round-2 finding map.
**Source:** Fresh-context review of `latest` @ `58cee18`, after platform Phases 1–15 and OpenTAP A–J
**Predecessor:** [review-remediation.md](review-remediation.md) (all of its routed findings were re-checked; see [Closed from the previous review](#closed-from-the-previous-review))

Every finding below was **reproduced or read directly in code**. Findings that could not be
reproduced are listed under [Checked and rejected](#checked-and-rejected) so they are not
re-litigated in a later review.

## Verification method

Run on `linux-x64` with .NET `10.0.302`:

- `dotnet build dirs.proj -r linux-x64` — succeeds, 0 warnings (`TreatWarningsAsErrors=true`).
- `dotnet test dirs.proj -r linux-x64` — **362 passed / 0 failed** (arch 15, E2E 9, host+core 166, VM 172).
- `dotnet format HardwareTest.slnx --verify-no-changes` — exits **2**, 132 findings across 48 files
  (CI's `dotnet format dirs.proj` exits 0 without checking anything — see
  [CI and supply chain](#ci-and-supply-chain)).
- Throwaway probe tests were added, executed, and deleted. Each finding marked **Reproduced**
  below includes the probe so it can be re-run or promoted into a regression test.

## Priority order

| Order | Finding | Status |
| --- | --- | --- |
| 1 | [F1 — Injected `AppSettings` goes stale after save](#f1--injected-appsettings-goes-stale-after-the-first-save) | **Fixed** — `ReapplyOverlays` / `LoadAsync` copy onto the existing instance |
| 2 | [F2 — Results / session-timer mutate UI state off the UI thread](#f2--results-and-the-session-idle-timer-mutate-ui-bound-state-off-the-ui-thread) | **Fixed** — shared `UiDispatch` + Results/timers/Instruments/Preview marshalling |
| 3 | [F3 — `OpenTapSession` has no run-in-progress guard](#f3--opentapsession-has-no-run-in-progress-guard) | **Fixed** — single-flight gate + mid-run mutation reject (`IsExecuting`) |
| 4 | [F4 — Safety Stop is live while idle](#f4--safety-stop-is-live-while-idle) | **Fixed** — `CanSafetyStop` binding + `Cancel()` guard |
| 5 | [F5 — Path containment is prefix-based](#f5--path-containment-checks-are-prefix-based) | **Fixed** — `PathContainment` + `PortableFileNames` rejects `.` / `..` |
| 6 | [F6 — Non-atomic writes for settings and runs](#f6--settings-and-run-writes-are-not-atomic) | **Fixed** — shared `AtomicFile` used by settings / runs / suites |
| — | [Doc drift](#doc-drift) | **Fixed** in the docs commit on this branch |
| — | [CI and supply chain](#ci-and-supply-chain) | Still open (format gate inert, no lock files) |

---

## Confirmed defects

### F1 — Injected `AppSettings` goes stale after the first save

**Severity:** High · **Reproduced** · `src/HardwareTest.Core/CoreServiceCollectionExtensions.cs`,
`src/HardwareTest.Core/Settings/SettingsStore.cs`

DI registers the `AppSettings` **instance that exists at registration time**:

```csharp
// CoreServiceCollectionExtensions.AddHardwareTestCore
services.AddSingleton(settingsStore.AppSettings);
services.AddSingleton(settingsStore.UiState);
```

`SettingsStore.AppSettings` is a `{ get; private set; }` property that is **replaced with a new
clone** on every successful save:

```csharp
// SettingsStore.SaveAppSettingsAsync → ReapplyOverlays
private void ReapplyOverlays()
{
    AppSettings = CloneSettings(_fileBaseline);   // new instance
    ...
}
```

So after the operator saves Settings once, `ISettingsStore.AppSettings` and the DI singleton are
**different objects**. Everything constructed from the injected instance keeps the pre-save values
for the rest of the process:

| Service | Constructed with | Settings it then ignores |
| --- | --- | --- |
| `RunRetentionService` | `settingsStore.AppSettings` | `RunRetentionDays`, `RunRetentionMaxRuns` |
| `ExportTargetService` | `settingsStore.AppSettings` | `ExportDirectory` |
| `StorageHealthService` | `settingsStore.AppSettings` | `DataFreeSpaceWarnBytes` / `CriticalBytes` |
| `TypstReportService` | `settingsStore.AppSettings` | `EmbedPlotsInReport`, `ReportTemplateName` |
| `OpenTapSession` | `sp.GetRequiredService<AppSettings>()` | `ExportOpenTapResults`, `DataDirectory` |

The Settings page itself is **not** affected — `SaveCoreAsync` re-reads
`var s = _settingsStore.AppSettings` on every save, so `settings.json` is written correctly and the
UI shows the new value. That is exactly what makes this dangerous: the operator gets "Saved at
14:32", the file on disk is right, and the running appliance still prunes, exports, and reports
with the old configuration until restart.

This is the same class of defect Phase 13 fixed for `UseMockVisa`, which was solved with a live
`IVisaModeController` rather than a frozen snapshot. Every other setting still has the frozen
snapshot.

**Probe (fails today):**

```csharp
var store = new SettingsStore(temp.Path);
await store.LoadAsync(new Dictionary<string, string>(), new Dictionary<string, string>());
var provider = new ServiceCollection().AddHardwareTestCore(store).BuildServiceProvider();
var injected = provider.GetRequiredService<AppSettings>();

Assert.Same(store.AppSettings, injected);        // passes before save
store.AppSettings.RunRetentionMaxRuns = 4242;
await store.SaveAppSettingsAsync();
Assert.Same(store.AppSettings, injected);        // FAILS: instances diverged
Assert.Equal(4242, injected.RunRetentionMaxRuns); // FAILS
```

**Suggested fix.** Mutate `AppSettings` in place instead of replacing the reference (make
`ReapplyOverlays` copy fields into the existing instance). That keeps one identity for the process
and every existing consumer becomes live with no signature changes. Alternatively register
`sp => sp.GetRequiredService<ISettingsStore>().AppSettings` **and** stop capturing `AppSettings` in
constructors — but the in-place mutation is the smaller, safer change.

**Guard:** an architecture or DI test asserting `ReferenceEquals(store.AppSettings, resolved)` still
holds after a save.

---

### F2 — Results and the session idle timer mutate UI-bound state off the UI thread

**Severity:** High · **Reproduced** · `src/HardwareTest/Features/Results/ResultsViewModel.Detail.cs`,
`src/HardwareTest/Features/RunTest/OperatorSessionPanelViewModel.cs`

The Run board solved this properly in Phase 12 and the code says so out loud:

```csharp
// RunTestViewModel.UiPump.cs — PostToUi
// Do NOT run action() on the current (background) thread — that would mutate
// UI-bound state off the UI thread. Log and no-op instead.
```

Two surfaces never got the same treatment.

**F2a — `ResultsViewModel.OpenAsync` / `RefreshAsync`.** Both `await` the run store and then mutate
bound `ObservableCollection`s (`Runs`, `StepDetails`, `SampleDetails`, `PresentationTiles`,
`HistoryMetrics`) and `[Reactive]` properties with no dispatcher marshalling. `RefreshAsync` also
starts with `await _busyGate.WaitAsync().ConfigureAwait(false)`, which explicitly drops UI-thread
affinity before touching collections.

**Probe (fails today).** The existing `FakeRunStore` returns already-completed tasks, so every
`await` resumes inline and the current ViewModel tests **cannot** catch this. Swapping in a store
whose methods actually yield — which is what `FileRunStore` does — exposes it:

```csharp
// AsyncRunStore: same IRunStore, but each method does `await Task.Delay(5)` first.
var vm = new ResultsViewModel(SeededStore(), new FakeReportService());
await vm.RefreshCommand.ExecuteAsync();
var caller = Environment.CurrentManagedThreadId;
var threads = new List<int>();
((INotifyCollectionChanged)vm.StepDetails).CollectionChanged += (_, _) => threads.Add(Environment.CurrentManagedThreadId);
vm.SelectedRun = vm.Runs[0];
await vm.OpenCommand.ExecuteAsync();
// FAILS: "2/4 StepDetails mutations happened off the caller thread (caller=4, mutating=6,4)"
```

**F2b — `OperatorSessionPanelViewModel` idle timer.** A `System.Timers.Timer` fires on a thread-pool
thread every 15 s and directly runs session-gate logic:

```csharp
_idleTimer = new System.Timers.Timer(15_000) { AutoReset = true };
_idleTimer.Elapsed += (_, _) =>
{
    try
    {
        ApplyIdleStaleCheck();     // sets OperatorSession state
        RefreshSessionSummary();   // sets [Reactive] props incl. SessionBlocked
    }
    catch
    {
        // Timer must not crash the process.
    }
};
```

This is the highest-consequence instance because `SessionBlocked` feeds `CanStartRun`. The bare
`catch` also means a cross-thread exception here silently disables idle/stale enforcement for the
rest of the session — the operator would never learn that the stale-DUT gate stopped working.
`SettingsViewModel`'s debounce timer has the same shape (it calls `SaveAsync` from the timer thread).

**Suggested fix.** Give `ResultsViewModel`, `InstrumentsViewModel`, `ReportPreviewViewModel`, and the
two timers the same `PostToUi` seam the Run board already has, and log in the `catch` instead of
swallowing. Since `ResultsViewModel` already exposes a `UiScheduler` test seam, most of the
plumbing exists.

**Test-suite implication.** This class of bug is currently **structurally invisible** to
`HardwareTest.ViewModels.Tests` because every fake completes synchronously. Adding one
deliberately-yielding fake store would make these tests able to fail. That is the single
highest-leverage test change in this review.

---

### F3 — `OpenTapSession` has no run-in-progress guard

**Severity:** High (latent today, blocking for Phase K) · **Reproduced** ·
`src/HardwareTest.OpenTap.Host/OpenTapSession.cs`

`RunAsyncCore` takes `_sync` only to swap fields, then releases it for the whole execution. There is
no `IsRunning` flag, no gate, and no rejection of a second concurrent call. A second call clears
`_samples` / `_steps` / `_stepStarted` while the first run's `ProgressResultListener` is still
appending to those same lists, and overwrites the process-global `StepRuntime.WaitIfPaused` /
`StepRuntime.RequestInteraction` callbacks.

**Probe.** Two overlapping `RunAsync()` calls on `flat-leaves.TapPlan` (single-run baseline:
**3 steps, 8 samples**), repeated five times:

```
baseline steps=3 samples=8
#0: A=OK(steps=4,samples=13,result=Passed)  B=THREW(NullReferenceException)
#1: A=THREW(NullReferenceException)         B=OK(steps=4,samples=13,result=Passed)
#2: A=OK(steps=4,samples=12,result=Passed)  B=THREW(NullReferenceException)
#3: A=THREW(NullReferenceException)         B=OK(steps=6,samples=16,result=Passed)
#4: A=OK(steps=2,samples=10,result=Error)   B=THREW(NullReferenceException)
```

A separate attempt had **both** calls return "success" with merged results and duplicated steps:

```
runA=fd72f939 runB=9892c8ab sameStepCount=True
duplicateStepPathsInA=[Leaf A x2, Leaf B x2, Safe Shutdown x2]
```

So the outcomes are: a `NullReferenceException` escaping to the caller, step/sample counts that are
wrong in both directions, a **fabricated `result=Error`** on a passing plan, and two distinct
`RunId`s persisting the same doubled step list. For an appliance whose output is a pass/fail record
attached to a physical serial number, a silently merged run record is a traceability problem, not
just a crash.

**Also unguarded: plan mutation during a run.** `TrySetStepEnabled`, `TrySetParameter`, and
`BindPlan` accept writes mid-execution. Probe: while a run was in flight, **10 step-enable
mutations were accepted** (`"10 step-enable mutations were accepted mid-run (e.g. Repeat Sweep)"`).

**Reachability — read this before assigning urgency.** The only production caller is
`RunExecutionViewModel.RunPipelineAsync`, and the `if (_host.IsRunning)` check through
`_host.IsRunning = true` is a straight-line synchronous block on the UI thread with no `await`, so
**double-clicking Run cannot reach this today** and neither can `Run` + `Run Selected` racing. This
is a latent defect, not a live operator-facing bug. It matters because:

1. The session is a public five-interface surface whose contract implies it is safe to call.
2. OpenTAP [Phase K](../opentap-phases/phase-k-multi-dut-parallel.md) (multi-DUT parallel) is the
   next planned OpenTAP phase and will drive exactly this path.
3. Phase 14 split the façade into focused surfaces but left one god object with shared mutable
   state behind all five — the split improved consumer clarity, not run isolation.

**Suggested fix.** An `Interlocked`-based single-flight gate in the session that rejects a second
run with `InvalidOperationException` and rejects mutating calls while held, plus a contract test in
`HardwareTest.Session.Contracts` so the fake and the real session agree. Do this **before** Phase K
rather than as part of it — Phase K needs per-run state (`_samples`, `_steps`, `StepRuntime`
callbacks moved off statics into a per-run context), and that refactor is much safer with the
guard and its test already in place.

---

### F4 — Safety Stop is live while idle

**Severity:** Medium · **Reproduced by inspection** ·
`src/HardwareTest/Features/RunTest/RunTestView.axaml`,
`src/HardwareTest/Features/RunTest/RunExecutionViewModel.cs`

The Run board's Safety Stop button has **no `IsEnabled` binding**, unlike the footer transport in
`MainWindow.axaml` which binds `IsEnabled="{Binding IsRunning}"`:

```xml
<Button Content="Safety Stop" Command="{Binding Run.CancelCommand}" Classes="danger" ... />
```

and `Cancel()` has no guard:

```csharp
public void Cancel()
{
    _runControl.RequestSafetyStop();
    _runSession.Abort(safetyStop: true);
}
```

`RunControl.RequestSafetyStop()` does not check `IsRunning`, so an idle click latches
`IsSafetyStopping = true` / `WasSafetyStopRequested = true` and calls `_gate.PreemptWaiters()`.
Consequences: the transport shows a safety-stop state with nothing running, and any in-flight
non-run VISA I/O is preempted — the Instruments page does `*IDN?` queries and discovery outside a
run, so an idle Safety Stop click can kill a discovery in progress. `AttachRun` resets both flags,
so the latch self-heals at the next run; the reachable damage is a confusing transport state plus a
cancelled instrument query. `MainWindowViewModel.SafetyStop` already guards with
`if (!_runControl.IsRunning) return;` — the Run board just diverged from it.

**Suggested fix.** Bind `IsEnabled` to `IsRunning || IsAwaitingOperator` (the button legitimately
also cancels an operator prompt, per its own tooltip) and add the same early return to `Cancel()`.

---

### F5 — Path containment checks are prefix-based

**Severity:** Medium (hardening — not reachable from operator input today) · **Reproduced** ·
`src/HardwareTest.Core/Storage/ExportTargetService.cs`,
`src/HardwareTest.Core/IO/PortableFileNames.cs`

**F5a — sibling-directory prefix bypass.** `WriteAtomic` validates containment with a raw
`StartsWith`:

```csharp
var dest = Path.GetFullPath(Path.Combine(target.RootPath, relativePath));
if (!dest.StartsWith(Path.GetFullPath(target.RootPath), StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Export path escapes target root.");
}
```

`/data/export` is a string prefix of `/data/export-evil`, so the check passes for a path that is
outside the root. Probe: `WriteAtomic(target, "../export-evil/payload.bin", ...)` with root
`/tmp/X/export` **wrote to `/tmp/X/export-evil/payload.bin`** without throwing.
`SanitizeRelative` does not help — it splits on separators and sanitizes each segment, and
`Sanitize("..")` returns `".."`.

**F5b — `..` survives filename sanitization.** `PortableFileNames.Sanitize` replaces
`Path.GetInvalidFileNameChars()` plus `/ \ : * ? " < > |` and control characters, but never treats
`.` or `..` as special. So `FileRunStore.GetRunDirectory("..")` resolves to the **parent** of the
runs directory (probe confirmed the resolved path is outside `runsDirectory`).

**Reachability.** Multi-level traversal is impossible because separators are replaced, and today
every `runId` is an internally generated `Guid.NewGuid().ToString("N")` and every export
`relativePath` / package name is internally composed. **Neither is reachable from operator input in
the current code.** Treat this as defense-in-depth on a boundary that is one careless
`runId: dutSerial` away from mattering — the fix is a few lines and removes the class of bug.

**Suggested fix.** One shared helper: `Path.GetRelativePath(root, candidate)` must not be rooted and
must not start with `..`; compare with a trailing directory separator on the root. Apply it in
`WriteAtomic`, `ExportPackage`, `FileRunStore.GetRunDirectory`, and
`TypstReportService.LoadReportFile` (which resolves an operator-settable `ReportTemplateName` under
`{DataDirectory}/reports` with no containment check at all — that one **is** settings-reachable, so
it is the most worthwhile of the three).

---

### F6 — Settings and run writes are not atomic

**Severity:** Medium · **By inspection** · `src/HardwareTest.Core/Settings/SettingsStore.cs`,
`src/HardwareTest.Core/Runs/FileRunStore.cs`, `src/HardwareTest.Core/Runs/FileSuiteRunStore.cs`,
`src/HardwareTest.Core/Crash/CrashDossierWriter.cs`

All of these serialize straight onto the destination path:

```csharp
await using var stream = File.Create(_settingsPath);      // SettingsStore.SaveAppSettingsAsync
await using var stream = File.Create(path);               // FileRunStore.SaveAsync → run.json
```

`File.Create` truncates first, so power loss mid-write leaves a truncated or empty document.
`settings.json` then falls back to defaults on next boot (the operator's configuration is gone) and
a truncated `run.json` is skipped by `ListAsync` — which has no `try/catch` around
`JsonSerializer.DeserializeAsync`, so one corrupt run makes the whole Results list throw rather than
skipping the bad entry.

This is notable because `ExportTargetService.WriteAtomic` in the same assembly already implements
the correct temp → flush-to-disk → rename pattern. The primary stores just don't use it.

**Suggested fix.** Extract the `WriteAtomic` pattern into a shared helper and use it for
`settings.json`, `ui-state.json`, `run.json`, and `suite-run.json`; wrap the per-run deserialize in
`ListAsync` so one bad record degrades to a skip plus a warning.

---

## Lower-priority code findings

| Sev | Location | Finding |
| --- | --- | --- |
| Medium | `OpenTapSession.OnTestStepRunCompleted` | `passed = stepRun.Verdict is Verdict.Pass or Verdict.NotSet` records a step that never set a verdict as **passed**. Not observed in the current fixtures (OpenTAP assigned `Pass` to every step including an empty group), so this is a latent mapping choice — but "no verdict" rolling up as pass is the wrong default for a test record. |
| Medium | `Hardware/VisaModeController.TryApply` | `IsRunning` / `IsBusy` are checked **outside** the `lock (_sync)` that swaps the factories, so a run starting in that window can have the VISA factory swapped under it. Re-check inside the lock. |
| Medium | `Plugins.Basic/VisaDmmInstrument` | Opens `Ivi.Visa.GlobalResourceManager` directly, bypassing the Core `VisaSessionGate` that serializes all other VISA I/O. With real hardware, OpenTAP steps and the Core engine can hold independent IVI sessions. |
| Medium | `Reporting/TypstReportService.GenerateReportsAsync` | `_runStore.SaveAsync(...).GetAwaiter().GetResult()` inside `Task.Run` blocks a pool thread for the whole save. |
| Medium | `Plugins.Basic/Steps.cs` — `MeanGteStep.Run` | `values.Average()` throws on `SampleCount == 0`; `TrySetParameter` accepts `"0"` (only `TrySetAcquireSettings` guards it). |
| Medium | `OpenTapParameterBridge.TryParseValue` | Parses as `double` then casts to `int`, so `"3.9"` silently becomes `3` on a golden plan. Use `int.TryParse` for int targets. |
| Low | `Reporting/TypstReportService.CompileTemplateCore` | `{Temp}/HardwareTestTypst/{runId}/{kind}/` is created per report and never deleted — unbounded temp growth on a long-lived bench. |
| Low | `Crash/CrashDossierWriter.ListUnreviewed` | Returns `ExceptionMessage` straight from disk with no redaction pass, so operator/DUT identifiers can reach the recovery UI even when `RedactIdentifiersInDiagnostics` was off at capture time. |
| Low | `OpenTapSession` | `_pauseGate` / `_interactionGate` (`ManualResetEventSlim`) are never disposed; `Abort(bool safetyStop)` ignores its parameter entirely (identical behavior either way). |
| Low | `Engine/RunControl.DetachRun` | Does not dispose `_safetyCts` (one CTS leaked per run until the next `AttachRun`). |
| Low | `AppSettingsEnvironmentBinder` | Int/long bindings parse with `InvariantCulture` but never range-check, so `SyslogPort=0` or `PlotRefreshHz=-1` are accepted silently. `OperatorSessionIdle` clamps; nothing else does. |
| Low | `Widgets/Presentation/MetricGaugeView.BindFromContext` | Early return when `DataContext` is not a tile skips the `PropertyChanged -=` for the previous tile. |
| Low | Several `.axaml` | Hardcoded light-theme hex colors (`#FFEBEE`, `#C62828`, `#FB8C00`, `#FFCDD2`) in Home/Run chrome, while `BannerBrushConverter` is theme-aware — poor contrast in dark theme. |

---

## Doc drift

The roadmap currently contradicts the shipped code. These are cheap and worth fixing immediately
because they misrepresent deployment readiness.

| Severity | File | Says | Reality |
| --- | --- | --- | --- |
| Medium | `platform-roadmap.md` "Known gaps" | "Operator session still confirm-clock based", "UseMockVisa can diverge from DI factories after save", "First-impression feedback / Settings Save & About" listed as open | Phases 11, 13, 15 are **Done** and implemented |
| Medium | `platform-roadmap.md` phase table | Phase 1 — **In progress** | CI is green on `latest`, tests pass, tree clean; only the non-blocking format gate remains |
| Medium | `opentap-platform.md` | "UI talks to OpenTAP only through `IOpenTapSession`" | After Phase 14 Feature ViewModels inject the focused surfaces; an architecture test *forbids* `IOpenTapSession` in Features. The roadmap's own known-gaps bullet already says this correctly |
| Low | `README.md`, `opentap-platform.md`, `deferred/README.md` | platform phases "(1–14)" / "(11–14)" | Phase 15 shipped |
| Low | `review-remediation.md` | "**Status:** Planning" | Phases 11–15 all shipped |
| Low | `phase-1-repo-gates.md` | CI-green and clean-tree exit boxes unchecked | Both satisfied |

Documented commands are **correct** — the Deno task names in `README.md` / `testing.md` /
`containers.md` match `tools/ci/deno.json` and the catalog assertion in `ci.yml`, and every
documented project path exists.

Every relative link across the 45 doc files resolves to an existing **file**, but checking
**anchors** as well turned up one silent mis-target: `opentap-platform.md` linked to
`adapting.md#10-custom-mixins`, and `adapting.md` renumbered that section to `## 11. Custom mixins`
(`#10-` is now "Configuration reference"). Anchor-level link checking is worth adding to whatever
checks file-level links, since a stale anchor fails silently by scrolling to the wrong section
rather than 404-ing.

---

## CI and supply chain

CI is structurally good — a Deno task catalog shared by Actions and local runs, a drift test that
asserts `ci.yml` invokes every task, coverage floors with their own parser tests, a digest-pinned
non-root `Containerfile.ci`, and `Nullable=enable` + `TreatWarningsAsErrors=true` repo-wide. The
gaps are all Phase 1 follow-ups that were never closed.

| Sev | Gap | Evidence |
| --- | --- | --- |
| High | **The format check is inert, not merely advisory.** `dotnet format dirs.proj` cannot format a traversal project — it prints `Could not format '/workspace/dirs.proj'. Format currently supports only C# and Visual Basic projects.` and **exits 0**. The `continue-on-error: true` is irrelevant because the command never fails. Pointing it at the real solution instead exits **2** with a backlog CI has never seen: **132 findings across 48 files** (131 × `RCS1139`, 1 × `IDE0040`). Note the backlog is dominated by a rule that conflicts with this repo's deliberate one-line `///` comment convention, so the fix is to configure `RCS1139` in `.editorconfig`, not to rewrite 131 comments. The Linux job has no format step at all | `ci.yml` `Format check`; `dotnet format HardwareTest.slnx --verify-no-changes` |
| Medium | **`Deterministic=true` is undermined by the version stamp.** `StampHardwareTestInformationalVersion` embeds `$([System.DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))` in `InformationalVersion`, so two builds of the same commit produce different assemblies. For an appliance that reports build provenance in crash dossiers and `print-config`, prefer the commit SHA (already resolved into `SourceRevisionId`) or a `SOURCE_DATE_EPOCH`-style pinned timestamp | `Directory.Build.props` — `_UtcBuildDate` |
| High | **No dependency pinning.** No `packages.lock.json` (0 found), no `NuGet.config`, no `Directory.Packages.props` — yet the cache key already references `**/packages.lock.json`, so restores float on NuGet.org and the cache is only partly effective | `ci.yml` `cache-dependency-path` |
| High | **No vulnerability scanning.** Nothing runs `dotnet list package --vulnerable`; no Dependabot | no workflow step |
| Medium | **Actions pinned to floating tags** (`actions/checkout@v4`, `setup-dotnet@v4`, `setup-deno@v2`, `upload-artifact@v4`) rather than commit SHAs | `ci.yml` |
| Medium | **Linux does not gate publish.** `publish-win` has `needs: test` (Windows only), so Windows-green + Linux-red still publishes artifacts; Linux E2E is additionally `continue-on-error` | `ci.yml` |
| Medium | **Coverage floors only cover `HardwareTest.Core`.** The 70/80% floors exclude `HardwareTest`, `HardwareTest.OpenTap.Host`, and the plugins, so the UI and OpenTAP host can regress with CI green | `tests/coverage.runsettings` — `<Include>[HardwareTest.Core]*</Include>` |
| Medium | **No `permissions:`, `timeout-minutes:`, or `concurrency:`** in any workflow (0 occurrences) — default token scope, no hung-job cap, no cancel-in-progress | `.github/workflows/` |
| Low | No `.dockerignore`, so container builds ship `bin/`, `obj/`, and `.git` into the build context; `deploy/quadlets/hardwaretest-ci.container` pins `:latest` | repo root, `deploy/` |

Package versions are **consistently aligned** across all projects (`OpenTAP` 9.32.2, Avalonia
12.1.0, xunit.v3 3.2.2, `Microsoft.Extensions.*` 10.0.10, ScottPlot 5.1.59) with no preview
dependencies and the SDK pinned to `10.0.302` with `allowPrerelease: false`. No version drift found.

---

## Test-suite blind spots

The suites are substantial (362 tests) and the shared `Session.Contracts` suite genuinely runs
against **both** the real and fake sessions with approved surface snapshots — that is a strong
pattern worth keeping. The gaps that matter:

1. **Synchronous fakes hide threading bugs.** Every fake completes synchronously, so no ViewModel
   test can observe a continuation resuming off the UI thread. This is why F2 is invisible today.
   One deliberately-yielding `IRunStore` fake would fix that.
2. **No file-I/O failure tests.** Corrupt/truncated `settings.json`, `run.json`, or `ui-state.json`;
   disk-full (`ExportTargetService.EnsureSpace` throws but is untested); partial writes. The
   handlers exist; nothing exercises them.
3. **No concurrency tests at the seams.** Double-start, overlapping `RunAsync`, dispose-while-running,
   and mid-run mutation are all untested (F3).
4. **Time is not abstracted.** `DateTimeOffset.UtcNow` is used directly in production paths, and the
   suites lean on `Task.Delay` / `Thread.Sleep` polling with 30 s–2 min wall-clock timeouts.
   `RunRetentionService` already accepts an injectable `utcNow` — extending that pattern (or
   `TimeProvider`) would remove a class of flake. One real flake was observed on Linux during this
   review: `Interaction_sets_awaiting_and_resume_clears` against the real session.
5. **Architecture tests miss transitive references.** `ArchitectureRulesTests` skips any assembly
   whose name does not start with `HardwareTest`, so `Core → SomeThirdParty → Avalonia` would not
   trip `Core_must_not_reference_Avalonia`. Some rules are also `file.Text.Contains(...)` string
   scans, and a newly added `src/` project is not checked until someone manually adds a marker test.
6. **A few tautological assertions.** `Catalog_apis_return_non_null_and_do_not_throw`,
   `CoreCompositionTests` (`Assert.NotNull` only), and `ChipBrushConverter_maps_*`
   (`IsAssignableFrom<IBrush>` with no value check). `FakeOpenTapSession` also returns hardcoded
   Windows paths (`C:\Plugins\...`) that the contract test only null-checks.

---

## Checked and rejected

Recorded so a future review does not spend time here.

| Claim | Verdict |
| --- | --- |
| Double-click Run / `Run` + `Run Selected` can start two runs (TOCTOU on `IsRunning`) | **No.** `ExecuteRunAsync` runs straight-line and synchronously from the guard through `IsRunning = true` with no `await`, on the UI thread. The session-level gap is real (F3); the UI race is not |
| Progress can exceed 100% or go backwards on loop plans | **No.** Measured on the sweep/repeat fixture: `max=100.0%`, `monotonic=True`, no regressions. `ReportStepProgress` uses `Math.Max(_stepCount, _stepIndex)` as denominator. The uncapped expression in `PublishSamples` did not produce an over-100 frame, and `ProgressBar Maximum="100"` clamps the view |
| Settings saves are lost because the ViewModel writes a stale instance | **No.** `SaveCoreAsync` re-reads `_settingsStore.AppSettings` on every save, so `settings.json` is always written correctly. The staleness is in the DI-injected copy only (F1) |
| Report-preview `Bitmap` leak (routed to Phase 12) | **Fixed.** `LoadFromPathAsync` disposes each bitmap before `Pages.Clear()` |
| `UseMockVisa` split-brain (routed to Phase 13) | **Fixed.** Settings, Instruments, and run preflight all read `IVisaModeController.EffectiveUseMockVisa` |
| Broken doc links / wrong documented commands | **None found.** All 44 doc files' relative links resolve; Deno task names match `tools/ci/deno.json` |
| Package version drift between projects | **None found.** All shared packages aligned |

---

## Closed from the previous review

All findings routed by [review-remediation.md](review-remediation.md) were re-checked against code.
Confirmed implemented: `UseMockVisa` live semantics (13); activity-aware idle, soft-warn,
confirm-every-run, `RequireOperator` binding, Stale technician field (11); UI-thread marshalling on
the Run board, progress reset instead of fake 100%, Run-while-running block, fail-filter chip,
interaction/abort labels, Home CTAs and empty states, crash-status visibility (12/15); façade split
(14); disabled-Run tooltips, busy affordances, sticky Settings Save (15).

**Not carried out, still open:** README/roadmap phase-range hygiene (now worse — Phase 15 missing
everywhere), Phase 1's blocking format gate, and the Phase 9 "~300-line parent" target
(`RunTestViewModel.cs` 563 + `.UiPump.cs` 547 ≈ 1,110 lines across partials; the per-file 600-line
architecture guard passes, and the phase's own exit criteria only require the coordinator role, so
this is a missed aspiration rather than a regression).

---

## Deferred-work risk

Re-reading `docs/deferred/` against an unattended-appliance deployment, two items stand out as
carrying more risk than their docs imply:

- **Clock discipline** — every idle/stale decision, run ordering, and retention prune keys off
  `DateTimeOffset.UtcNow` with no skew detection. A wrong RTC on a sealed offline appliance produces
  false stale sessions and prunes runs at the wrong time. This is already the first bullet in the
  roadmap's known gaps; it deserves promotion to a phase rather than staying deferred.
- **Auto-update** — with no update channel, fleet drift compounds against the Phase 5 schema gates:
  a unit that reads a newer document becomes read-only and stays that way until someone updates it
  by hand.

The rest (remote crash upload, package feed install, run comparison, localization, bench profile UI,
kiosk image bake) are safely deferred for the current scope. Schema migration is adequately fenced
by the Phase 5 gates for now, but the first real schema bump will need a transform plus golden
before/after fixtures — `SchemaUpgradeRegistry` currently contains only a no-op 1→2 step.

---

## Suggested routing

| Finding | Suggested destination |
| --- | --- |
| F1 stale injected `AppSettings` | New phase (or Phase 13 follow-on — same class of defect it fixed for VISA) |
| F2 off-UI-thread mutation in Results / timers | Phase 12 follow-on (extends the `PostToUi` pattern it introduced) |
| F3 run guard + per-run state | **Prerequisite for OpenTAP [Phase K](../opentap-phases/phase-k-multi-dut-parallel.md)** — land the guard and its contract test first |
| F4 Safety Stop enablement | Phase 15 follow-on (operator chrome) |
| F5 path containment, F6 atomic writes | New storage-hardening phase |
| Verdict mapping, VISA lock window, parameter narrowing | Fold into the phases above |
| Doc drift | Doc hygiene — do now, it is actively misleading |
| CI / supply chain | Reopen Phase 1 follow-ups |
| Synchronous-fake / file-I/O / concurrency test gaps | Phase 8 follow-on (contract + fake fidelity) |
| Clock discipline | Promote from deferred to a phase |
