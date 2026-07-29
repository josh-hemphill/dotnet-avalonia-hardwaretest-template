# Testing

UI/board tests stay separate from OpenTAP plan-behavior tests. Both share the `IOpenTapSession` contract; Avalonia never talks to OpenTAP except through that façade.

| Suite | Purpose | OpenTAP | Emulation |
| --- | --- | --- | --- |
| Architecture | Layering smoke (Avalonia/OpenTAP boundaries, single Window, `AppJsonContext`) | Load assemblies only | None |
| ViewModels | Run board / session / filters / rollup UX | `FakeOpenTapSession` (in-memory trees + optional recording replay) | No real instruments |
| Core/OpenTAP host | Plan load, hierarchy, Run Selected mask, SafeShutdown, progress/samples | Real `OpenTapSession` | `MockDmmInstrument` |
| Avalonia E2E | Shell wiring only (DUT → Run → Results/Inspect) | Real session | MockDmm + `UseMockVisa` |

CI runs one `test` job with labeled steps: Architecture, ViewModels, OpenTAP host + fixtures, E2E smoke.

Platform roadmap (interactions, parameters, mixins): [opentap-platform.md](opentap-platform.md).
Hardening roadmap (gates, config, crash, CI): [platform-roadmap.md](platform-roadmap.md).

Build/version coverage (`BuildInfo`, `AppVersion` on `TestRunRecord`, Settings **Copy diagnostics**, `--version` parsing) lives in Core + ViewModels tests — see [phase-4-build-info.md](platform-phases/phase-4-build-info.md).

## When to add which test

### Architecture (layering smoke)

Put a rule here only when it is a short, stable layering claim already written in README / platform docs (e.g. "Core must not reference Avalonia"). Failure messages must name the rule and the doc. Behavioral coverage (plan runs, ViewModel flow, E2E) stays in the suites below — see [phase-2-architecture-tests.md](platform-phases/phase-2-architecture-tests.md).

### UI / board (ViewModels)

1. Build or load a tree via `FakeOpenTapSession` (`LoadSampleProgramAsync`, `LoadBoardDemoProgramAsync`, Fake-only `LoadPlanShapeAsync` / `LoadTreeFromNodes`).
2. Drive `RunTestViewModel` / `InspectViewModel` and assert StepRows, rollup chips, filters, or Inspect parity.
3. For a captured edge case offline: `ReplayRecording(dir, "cassette-name")` then refresh hierarchy/Inspect.

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
- Include `SafeShutdownStep` when using Run Selected (selection keeps SafeShutdown enabled).
- Instruments must be extractable for the Instruments page (or document limits for foreign plugins).

## Local commands

```bash
# Prefer CI-shaped sequential suite runs (OpenTAP host + E2E share process-global TapThread state):
dotnet test tests/HardwareTest.Architecture.Tests -r win-x64
dotnet test tests/HardwareTest.Tests -r win-x64
dotnet test tests/HardwareTest.ViewModels.Tests -r win-x64
dotnet test tests/HardwareTest.E2E.Tests -r win-x64

# Or traversal with serial MSBuild (dirs.proj sets BuildInParallel=false):
dotnet test dirs.proj -r win-x64 -m:1
```
