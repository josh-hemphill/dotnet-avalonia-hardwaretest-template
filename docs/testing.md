# Testing

UI/board tests stay separate from OpenTAP plan-behavior tests. Both share the `IOpenTapSession` contract; Avalonia never talks to OpenTAP except through that façade.

| Suite | Purpose | OpenTAP | Emulation |
| --- | --- | --- | --- |
| Architecture | Layering smoke (Avalonia/OpenTAP boundaries, single Window, `AppJsonContext`) | Load assemblies only | None |
| Session contracts | Shared `IOpenTapSession` behavior (real + fake) | Real or `FakeOpenTapSession` | MockDmm / canned trees |
| ViewModels | Run board / session / filters / rollup UX | `FakeOpenTapSession` (in-memory trees + optional recording replay) | No real instruments |
| Core/OpenTAP host | Plan load, hierarchy, Run Selected mask, SafeShutdown, progress/samples | Real `OpenTapSession` | `MockDmmInstrument` |
| Avalonia E2E | Shell wiring only (DUT → Run → Results/Inspect) | Real session | MockDmm + `UseMockVisa` |

CI runs Deno tasks from [`tools/ci/`](../tools/ci/) on **windows-latest** (required E2E) and **ubuntu-latest** (`linux-x64`; E2E advisory). See [containers.md](containers.md).

Platform roadmap (interactions, parameters, mixins): [opentap-platform.md](opentap-platform.md).
Hardening roadmap (gates, config, crash, CI): [platform-roadmap.md](platform-roadmap.md).

Build/version coverage (`BuildInfo`, `AppVersion` on `TestRunRecord`, Settings **Copy diagnostics**, `--version` parsing) lives in Core + ViewModels tests — see [phase-4-build-info.md](platform-phases/phase-4-build-info.md).
Schema gates and golden files under `tests/fixtures/schema/` are covered in Core tests — see [phase-5-schema-versioning.md](platform-phases/phase-5-schema-versioning.md).
Crash dossiers (writer, ring sink, redaction, dangling-run reconciliation) live in Core tests under `Crash/` — see [phase-6-crash-reporting.md](platform-phases/phase-6-crash-reporting.md).
Local CI tasks, coverage floors (TypeScript), and container rails — see [containers.md](containers.md) and [phase-7-containers-local-ci.md](platform-phases/phase-7-containers-local-ci.md).
Session contract tests (`HardwareTest.Session.Contracts`) run against both real and fake `IOpenTapSession` via the host and ViewModel suites — see [phase-8-session-contract-tests.md](platform-phases/phase-8-session-contract-tests.md).
Export targets, run retention, and free-space gates live in Core tests under `Storage/` — see [phase-10-export-storage-chrome.md](platform-phases/phase-10-export-storage-chrome.md).

## When to add which test

### Session contract (both implementations)

Put an assertion in `OpenTapSessionContractTests` only when it must hold for **both** the real session and `FakeOpenTapSession` (load/run/abort/pause/parameters/catalog). Implementation-specific edges (fake-only `BeginInteraction` / `LoadTreeFromNodes`, real instrument timing) stay in the host or ViewModel suite. Changing `IOpenTapSession` requires updating `IOpenTapSession.approved.txt` in the same commit.

### Architecture (layering smoke)

Put a rule here only when it is a short, stable layering claim already written in README / platform docs (e.g. "Core must not reference Avalonia"). Failure messages must name the rule and the doc. Behavioral coverage (plan runs, ViewModel flow, E2E) stays in the suites below — see [phase-2-architecture-tests.md](platform-phases/phase-2-architecture-tests.md).

### UI / board (ViewModels)

1. Build or load a tree via `FakeOpenTapSession` (`LoadSampleProgramAsync`, `LoadBoardDemoProgramAsync`, Fake-only `LoadPlanShapeAsync` / `LoadTreeFromNodes`).
2. Drive `RunTestViewModel` / `InspectViewModel` and assert StepRows, rollup chips, filters, or Inspect parity.
3. For a captured edge case offline: `ReplayRecording(dir, "cassette-name")` then refresh hierarchy/Inspect.

#### Run board child ViewModels (coordinator + owned children)

`RunTestViewModel` is a coordinator that owns one child ViewModel per panel (`StepDetail`, `Interaction`, `SessionPanel`, `ProgramSelection`, `StationOverrides`, `Live`, `StepTree`, `Run`). Children are constructed by the parent — not registered in DI — and receive services plus small `Func`/`Action` callbacks instead of a back-reference to the parent. The run pipeline takes the coordinator through `IRunBoardHost` so a stub can replace it. The UI flush pump (`IngestProgress`, `UiScheduler`, `RunOnUiAsync`) stays on the coordinator, which keeps every child dispatcher-agnostic. Feature files are capped at 600 lines by `ArchitectureRulesTests.Feature_source_files_stay_under_line_budget`; split into another child or a partial rather than raising the cap.

Pick the narrowest suite for what you are asserting:

- **One panel's own behavior** → construct the child directly with fakes/no-op callbacks, as in `RunBoardChildViewModelTests`. No dispatcher and no parent needed.
- **Cross-panel coordination** (a selection change refreshing detail, hero, plot and overrides together) → build the whole `RunTestViewModel`, set `UiScheduler = action => action()`, and assert through child paths such as `vm.StepTree.SelectedStep` or `vm.StepDetail.DetailChipText`.
- **AXAML bindings** use the same child paths (`{Binding StepDetail.DetailLines}`), so a test written against the child path matches what the view binds to.

### Plan behavior (OpenTAP host)

1. Prefer a C# factory in `PlanShapeFixtures` / `SampleProgramFactory` / `BoardDemoProgramFactory` / `SweepDemoProgramFactory` (optionally `SaveBeside` under `plans/opentap/fixtures/`).
2. Load with concrete `OpenTapSession.LoadPlanShapeAsync(...)` (not on `IOpenTapSession`) or the sample / board-demo / sweep-demo loaders.
3. Assert `StepTree` shape, unique paths, Run Selected enable-mask behavior, or SafeShutdown presence.
4. Keep host tests in the `OpenTapSerial` collection (`DisableParallelization`).

Named templates live in `PlanDiagnosticsTests` (`PlanDiagnostics_*`).

See also [adapting.md](adapting.md) for productizing plans, plugins, and reports.
### Record and adapt (progress/summary cassette)

Not a full SCPI VCR. Capture what the board already consumes:

1. Wrap `IProgress<OpenTapProgress>` with `OpenTapRunRecorder`, run via `OpenTapSession`, then `WriteBeside(dir, baseName, summary)`.
2. Commit `*.progress.json` + `*.summary.json` under `tests/fixtures/opentap/recordings/`.
3. Assert offline with `OpenTapRunRecorder.LoadBeside` (host) or `FakeOpenTapSession.ReplayRecording` (ViewModels).

To regenerate the checked-in `sample-pass` cassette, build host tests with `/p:DefineConstants=RECORD_OPENTAP_RUN` and run `Record_sample_pass_cassette` (see `#if RECORD_OPENTAP_RUN` in `OpenTapRunRecorderTests`).

## Plan contract (Run board)

- Prefer unique step paths (duplicate sibling names need path-qualified selection).
- Max useful nest depth for chrome is three levels (Stages → Sections → Nested); deeper nodes still appear as leaves under path.
- Include `SafeShutdownStep` when using Run Selected (selection keeps SafeShutdown enabled by default). Opt out with `selectionIncludesCleanup: false` in `{planId}.program.json` only when shutdown is suite-scoped and selection is software-only. Disabled siblings showing NotExecuted/Invalidated is expected — not “cleanup skipped.”
- Instruments must be extractable for the Instruments page (or document limits for foreign plugins).

## Local commands

```bash
# Same tasks CI runs (RID defaults to the host):
deno task --cwd tools/ci all -- --rid win-x64

# Or individual suites (OpenTAP host + E2E share process-global TapThread state):
deno run -A tools/ci/main.ts test:arch --rid win-x64
deno run -A tools/ci/main.ts test:host --rid win-x64
deno run -A tools/ci/main.ts test:vm --rid win-x64
deno run -A tools/ci/main.ts test:e2e --rid win-x64

# Raw dotnet still works:
dotnet test dirs.proj -r win-x64 -m:1
```
