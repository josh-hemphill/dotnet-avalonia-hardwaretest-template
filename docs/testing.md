# Testing

UI/board tests stay separate from OpenTAP plan-behavior tests. Both share the OpenTAP session contract (aggregating `IOpenTapSession` and focused surfaces); Avalonia Feature ViewModels inject the narrow `IOpenTap*` surfaces.

| Suite | Purpose | OpenTAP | Emulation |
| --- | --- | --- | --- |
| Architecture | Layering smoke (Avalonia/OpenTAP boundaries, single Window, `AppJsonContext`, plugin source must not use `Ivi.Visa`, no static `StepRuntime` pause/interaction) | Load assemblies only | None |
| Session contracts | Shared `IOpenTapSession` behavior (real + fake) | Real or `FakeOpenTapSession` | MockDmm / canned trees |
| ViewModels | Run board / session / filters / rollup UX | `FakeOpenTapSession` (in-memory trees + optional recording replay) | No real instruments |
| Core/OpenTAP host | Plan load, hierarchy, Run Selected mask, SafeShutdown, progress/samples | Real in-process `OpenTapSession` (documented test-only host) | `MockDmmInstrument` |
| Avalonia E2E | Shell wiring only (DUT → Run → Results/Inspect) | Worker-backed session (`OpenTapWorkerClient`) | MockDmm + `UseMockVisa` |

CI runs Deno tasks from [`tools/ci/`](../tools/ci/) on **windows-latest** (required E2E) and **ubuntu-latest** (`linux-x64`; E2E advisory — the step is named **E2E smoke (advisory on Linux)**). Host tests run **without Coverlet**; `coverage` collects Core-safe tests only. See [containers.md](containers.md).

Where coverage lives:

- Build/version (`BuildInfo`, `AppVersion`, Settings **Copy diagnostics**, `--version`) — Core + ViewModels
- Schema gates and goldens under `tests/fixtures/schema/` — Core
- Crash dossiers (writer, ring sink, redaction, dangling-run reconciliation) — Core `Crash/`
- Session contracts (`HardwareTest.Session.Contracts`) — host + ViewModel suites against real and fake `IOpenTapSession`
- Export, retention, free-space — Core `Storage/`
- Clock skew — Core `Time/` (`IClock` / `FakeClock`; production idle/retention must not use `DateTimeOffset.UtcNow`)

First plan in TUI: [getting-started.md](getting-started.md). Productizing plans, plugins, and reports: [adapting.md](adapting.md).

## When to add which test

### Session contract (both implementations)

Put an assertion in `OpenTapSessionContractTests` only when it must hold for **both** the real session and `FakeOpenTapSession` (load/run/abort/pause/parameters/catalog). Implementation-specific edges (fake-only `BeginInteraction` / `LoadTreeFromNodes`, real instrument timing) stay in the host or ViewModel suite. Changing `IOpenTapSession` or a focused surface requires updating the matching `*.approved.txt` snapshot(s) in the same commit.

### Architecture (layering smoke)

Put a rule here only when it is a short, stable layering claim already written in README / adapting.md (e.g. "Core must not reference Avalonia"). Failure messages must name the rule and the doc. Behavioral coverage stays in the suites below.

- Plugin VISA must go through Core `IVisaBroker` — `ArchitectureRulesTests.Plugin_source_must_not_use_Ivi_Visa` scans `Plugins.Basic` / `Plugins.Visa` / `Plugins.Mixins`.
- Pause/interaction must not be process-global statics — `ArchitectureRulesTests.StepRuntime_must_not_expose_static_pause_or_interaction`.
- Idle/retention/run-complete must not call `DateTime.UtcNow` / `DateTimeOffset.UtcNow`; Safety Stop / worker kill must not wait on NTP.

### UI / board (ViewModels)

1. Build or load a tree via `FakeOpenTapSession` (`LoadSampleProgramAsync`, `LoadBoardDemoProgramAsync`, Fake-only `LoadPlanShapeAsync` / `LoadTreeFromNodes`).
2. Drive `RunTestViewModel` / `InspectViewModel` and assert StepRows, rollup chips, filters, or Inspect parity.
3. For a captured edge case offline: `ReplayRecording(dir, "cassette-name")` then refresh hierarchy/Inspect.

`RunTestViewModel` is a coordinator that owns one child ViewModel per panel (`StepDetail`, `Interaction`, `SessionPanel`, `ProgramSelection`, `StationOverrides`, `Live`, `StepTree`, `Run`). Children are constructed by the parent — not registered in DI — and receive services plus small `Func`/`Action` callbacks instead of a back-reference to the parent. The run pipeline takes the coordinator through `IRunBoardHost` so a stub can replace it. The UI flush pump (`IngestProgress`, `UiScheduler`, `RunOnUiAsync`) stays on the coordinator. Feature files are capped at 600 lines; split into another child or a partial rather than raising the cap.

Pick the narrowest suite:

- **One panel's own behavior** → construct the child directly with fakes/no-op callbacks (`RunBoardChildViewModelTests`). No dispatcher and no parent needed.
- **Cross-panel coordination** → build the whole `RunTestViewModel`, set `UiScheduler = action => action()`, and assert through child paths such as `vm.StepTree.SelectedStep`.
- **AXAML bindings** use the same child paths (`{Binding StepDetail.DetailLines}`).
- **Operator chrome / a11y** — type floor, compact Pause/Stop captions, live regions, and Settings headings live in `Phase21OperatorChromeTests`. Do not announce plot-sample floods; Engineer/debug tables may stay tighter than the Run operational floor.
- **Operator prompt / session (900×600)** — Continue stays docked outside `PromptBodyScroller`; session Enter confirms DUT; typed fields bind `TwoWay` + `PropertyChanged`. Contracts: `OperatorPromptChromeTests`; bind/focus: E2E `RunFlowE2ETests`.
- **Operator vs engineer nav** — default left nav is Home / Run / Results / Settings. Inspect and Instruments appear after saving Engineer / debug mode (presentation, not auth). Report Preview is contextual from Results. Compact Run board is `IsCompactLayout` below `ShellLayoutBreakpoints.CompactBoardWidth`.
- **Guided commissioning** — Run blocks unbound / demo-only slots via `StationReadinessEvaluator` and deep-links to Instruments. *IDN? writes `station-idn.json` (not AppSettings).
- **QA failure triage** — `RunTriageSummary.FromRecord` uses `StepAttempts` chronology (legacy `Steps` fallback). Opening a failed run sets `ResultFilter` to Failed and defaults the detail pane to failed steps.
- **Compare with previous** — Opening a run compares channel means to the latest earlier same DUT + plan. Missing metrics are listed as unavailable. Do not block Run on comparison failures.
- **Station health gate** — DUT sidecar `requireStationHealth` + `warn`|`block` evaluates `{DataDirectory}/station-health/{profileId}.json` with `IClock` (`StationHealthGateTests`). Missing/failed/old is stale. `HARDWARETEST_STATION_HEALTH_GATE=off` disables a sidecar block. Health programs are never gated. Do not skip the health step via `Enabled`.

### Plan behavior (OpenTAP host)

1. Prefer a C# factory in `PlanShapeFixtures` / `SampleProgramFactory` / `BoardDemoProgramFactory` / `SweepDemoProgramFactory` (optionally `SaveBeside` under `plans/opentap/fixtures/`).
2. Load with concrete `OpenTapSession.LoadPlanShapeAsync(...)` (not on `IOpenTapSession`) or the sample / board-demo / sweep-demo loaders. The in-process `OpenTapSession` is the **documented test-only host** for the serial `OpenTapSerial` suite — it does not pass a cancel token into `Execute` so Abort cannot poison `TapThread`.
3. Assert `StepTree` shape, unique paths, Run Selected enable-mask behavior, or SafeShutdown presence.
4. Keep in-process host tests that call `TestPlan.Execute` in the `OpenTapSerial` collection (`DisableParallelization`). Serial is required because TapThread / PluginManager are still process-global.
5. **Abort isolation:** Host `Abort` cancels cooperatively via CTS + `WaitIfPaused` / interaction gates — it does **not** call `TapThread.Abort`. Prefer draining run tasks in `finally` after Abort in tests.
6. **Worker kill:** `OpenTapWorkerKillTests` loads `HangForeverStep` through `OpenTapWorkerClient` (not `IOpenTapSession`). Abort then kill-timeout must run `ISafetyController.SafeIdle` and leave the client able to start a second run. ViewModels stay on `FakeOpenTapSession`.
7. **Run context isolation:** `OpenTapRunContextTests` may run in parallel. Two `OpenTapRunContext` / `IStepRuntime` instances must not share pause or interaction. Pause/Resume mutate the live control gate; `BeginRun` must not re-apply a snapshot.

Named templates live in `PlanDiagnosticsTests` (`PlanDiagnostics_*`).

### Record and adapt (progress/summary cassette)

Not a full SCPI VCR. Capture what the board already consumes:

1. Wrap `IProgress<OpenTapProgress>` with `OpenTapRunRecorder`, run via `OpenTapSession`, then `WriteBeside(dir, baseName, summary)`.
2. Commit `*.progress.json` + `*.summary.json` under `tests/fixtures/opentap/recordings/`.
3. Assert offline with `OpenTapRunRecorder.LoadBeside` (host) or `FakeOpenTapSession.ReplayRecording` (ViewModels).

To regenerate the checked-in `sample-pass` cassette, build host tests with `/p:DefineConstants=RECORD_OPENTAP_RUN` and run `Record_sample_pass_cassette` (see `#if RECORD_OPENTAP_RUN` in `OpenTapRunRecorderTests`).

## Plan contract

Host `PlanContractValidator` encodes the authoring checks for TUI/Editor authors (`HardwareTest --validate-plan`, `HardwareTest.PlanValidate`). The check table lives in [adapting.md](adapting.md#plan-contract). Warnings do not block operator Run.

Coverage lives in `PlanContractValidatorTests` (OpenTapSerial), including a strict gate over committed top-level `Programs/*.TapPlan` (fixtures excluded). `ConfigurationArgs` parse covers `--validate-plan` (including the bare flag). Named shape templates remain in `PlanDiagnosticsTests`.

## Local commands

```bash
# Same tasks CI runs (RID defaults to the host):
deno task --cwd tools/ci all -- --rid win-x64

# Or individual suites (OpenTAP host + E2E share process-global TapThread state):
deno run -A tools/ci/main.ts test:arch --rid win-x64
deno run -A tools/ci/main.ts test:host --rid win-x64
deno run -A tools/ci/main.ts test:vm --rid win-x64
deno run -A tools/ci/main.ts test:e2e --rid win-x64
deno run -A tools/ci/main.ts coverage --rid win-x64
deno run -A tools/ci/main.ts audit

# Raw dotnet still works:
dotnet test dirs.proj -r win-x64 -m:1
```
