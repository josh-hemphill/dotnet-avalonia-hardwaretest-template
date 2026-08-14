# Phase 23 — Safety Stop + OpenTAP worker process

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 19](phase-19-immediate-correctness.md) (honest Stop Run copy), [Phase 22](phase-22-visa-broker.md) (plan I/O on a preemptable broker)
**Unblocks:** unattended benches; [Phase 24](phase-24-session-decomposition.md) may share a train but worker isolation should land first if they conflict
**Status:** Done
**Also absorbs:** Round-3 R3-14 (cooperative abort, `CancellationToken.None`, no hardware interlock)

## Goal

Make **Stop** a layered, honest control: software cancel for cooperative steps, a real `ISafetyController` seam for hardware interlocks, and an OpenTAP **worker process** that can be killed when a step ignores cancel — without using `TapThread.Abort` in the UI process.

## Why this exists

Phase 19 only renamed the chrome. `plan.Execute` still runs with `CancellationToken.None`. Third-party or blocking steps can ignore Stop. Docs already reject `TapThread.Abort` because it poisons in-process OpenTAP (and the serial host suite). A long-lived hardware product cannot ship “Safety Stop” as a CTS flag.

## Locked decisions

- **Layered model (do not collapse):**
  1. **Stop Run** — cooperative software cancel (today’s Abort + CTS, plus pass a real token into Execute if OpenTAP allows).
  2. **`ISafetyController`** — output/interlock hardware (ESTOP loop, output disable). Default implementation may be a no-op log + status until a bench adapter exists. Never pretend the no-op is wired.
  3. **Kill worker** — last resort: terminate the OpenTAP child process, then run `ISafetyController.SafeIdle`.
- **Do not use `TapThread.Abort`.**
- OpenTAP engine runs in a **killable worker process**. The UI process owns chrome, settings, and the safety controller. IPC is explicit (existing `IOpenTap*` surfaces stay the client API).
- Killing the worker **must** attempt `ISafetyController.SafeIdle` before bookkeeping (roadmap: safety outranks diagnostics).
- Host tests that need a real engine run against the worker (or a documented in-process test-only host). UI/ViewModel tests keep `FakeOpenTapSession`.
- No new operator OS dialogs. Stop confirmation stays in-panel if required at all (prefer none — Stop is immediate).

## Workstreams

### A — `ISafetyController`

- Core interface: `SafeIdle()`, `IsArmed`, optional channel list. Avalonia-free, OpenTAP-free.
- Composition: no-op implementation until a bench adapter is registered. Settings / About must not say “interlock armed” for the no-op.
- Call `SafeIdle` from UI-process Stop **and** from worker-death handling.

### B — Worker process

- Host the current `OpenTapSession` (or its successor from Phase 24) in a child process.
- Client in the UI process implements `IOpenTapPlanSession` / `IOpenTapRunSession` / `IOpenTapStationSession` / `IOpenTapHostCatalog` over IPC.
- Abort: signal cancel; after timeout, kill the process; always `SafeIdle`.
- Crash dossiers capture worker exit code / stderr.

### C — Execute cancellation

- Pass a linked CTS into plan execute if the OpenTAP version supports it; keep cooperative plugin `TapThread.ThrowIfAborted` / `StepRuntime` behavior.
- Blocking VISA in the worker goes through Phase 22’s broker so cancel can preempt I/O.

### D — Tests

- Contract tests (Phase 8) run against the **client** façade.
- A kill-timeout test: a hanging test step is terminated; UI process remains usable for a second run.
- Serial in-process OpenTAP suite is retired or moved to the worker fixture.

## Exit criteria

- [x] Operator Stop Run cancels cooperative steps
- [x] Hung step: worker is killed, `SafeIdle` runs, UI can start another run
- [x] `ISafetyController` exists; no-op cannot be labeled as an armed interlock
- [x] No `TapThread.Abort` in the UI process
- [x] Phase 8 contracts still pass on the client surfaces
- [x] ViewModels stay on fakes; host tests do not poison the UI process

## Out of scope

- Wiring a specific ESTOP PLC / bench relay (adapter is a follow-up once the seam exists)
- Multi-DUT parallel workers ([Phase K](../opentap-phases/phase-k-multi-dut-parallel.md) — this phase should not paint K into a corner)
- Splitting `OpenTapSession` internals ([Phase 24](phase-24-session-decomposition.md) — may proceed in parallel on the worker side)

## Landed

- `ISafetyController` / `NoOpSafetyController` in Core. `IsArmed` is false; Settings About shows **Not wired** and never “armed”. Stop Run and crash capture call `SafeIdle` before abort/bookkeeping.
- UI `Composition` registers `OpenTapWorkerClient` for all `IOpenTap*` surfaces. The OpenTAP engine runs in `HardwareTest.OpenTap.Worker` (NDJSON IPC on stdout, logs on stderr). Public `IOpenTap*` are unchanged.
- Abort sends cooperative cancel; after `OpenTapWorkerKillTimeoutMilliseconds` (default 8000) the client calls `SafeIdle`, kills the process tree, writes an `opentap-worker` crash dossier (exit code / stderr, schema 1 additive), and respawns on the next call.
- In-process `OpenTapSession` remains the documented test-only host for the serial `OpenTapSerial` suite (`cancelExecuteWithToken: false` so Execute does not map onto `TapThread.Abort`). The worker passes the run CTS into `ExecuteAsync`.
- `HangForeverStep` is a test-only plugin step (not in sample/operator plans). `OpenTapWorkerKillTests` proves kill + `SafeIdle` + a second run.
- Architecture forbids `TapThread.Abort(` in `src/` and Avalonia references from the worker. ViewModels stay on `FakeOpenTapSession`.

## Related

- [phase-22-visa-broker.md](phase-22-visa-broker.md)
- OpenTAP `OpenTapSession.Abort` comments on `TapThread.Abort`
