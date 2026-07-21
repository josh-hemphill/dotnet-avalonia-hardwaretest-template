# Testing

UI/board tests stay separate from OpenTAP plan-behavior tests. Both share the `IOpenTapSession` contract; Avalonia never talks to OpenTAP except through that façade.

| Suite | Purpose | OpenTAP | Emulation |
| --- | --- | --- | --- |
| ViewModels | Run board / session / filters / rollup UX | `FakeOpenTapSession` (in-memory trees + optional recording replay) | No real instruments |
| Core/OpenTAP host | Plan load, hierarchy, Run Selected mask, SafeShutdown, progress/samples | Real `OpenTapSession` | `MockDmmInstrument` |
| Avalonia E2E | Shell wiring only (DUT → Run → Results/Inspect) | Real session | MockDmm + `UseMockVisa` |

CI runs one `test` job with three labeled steps: ViewModels, OpenTAP host + fixtures, E2E smoke.

## When to add which test

### UI / board (ViewModels)

1. Build or load a tree via `FakeOpenTapSession` (`LoadSampleProgramAsync`, `LoadBoardDemoProgramAsync`, `LoadPlanShapeAsync`, or `LoadTreeFromNodes`).
2. Drive `RunTestViewModel` / `InspectViewModel` and assert StepRows, rollup chips, filters, or Inspect parity.
3. For a captured edge case offline: `ReplayRecording(dir, "cassette-name")` then refresh hierarchy/Inspect.

### Plan behavior (OpenTAP host)

1. Prefer a C# factory in `PlanShapeFixtures` / `SampleProgramFactory` / `BoardDemoProgramFactory` (optionally `SaveBeside` under `plans/opentap/fixtures/`).
2. Load with `OpenTapSession.LoadPlanShapeAsync(...)` or the sample/board-demo loaders.
3. Assert `StepTree` shape, unique paths, Run Selected enable-mask behavior, or SafeShutdown presence.
4. Keep host tests in the `OpenTapSerial` collection (`DisableParallelization`).

Named templates live in `PlanDiagnosticsTests` (`PlanDiagnostics_*`).

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
dotnet test tests/HardwareTest.ViewModels.Tests -r win-x64
dotnet test tests/HardwareTest.Tests -r win-x64 --filter "FullyQualifiedName~OpenTap"
dotnet test tests/HardwareTest.E2E.Tests -r win-x64
```
