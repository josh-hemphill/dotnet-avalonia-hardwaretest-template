# Phase 6 — Crash reporting & unhandled failure capture

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 3](phase-3-configuration-model.md) (effective config in the dossier), [Phase 4](phase-4-build-info.md) (a crash report without a version is nearly useless)
**Unblocks:** handing the app to operators you cannot stand behind
**Status:** Done

## Goal

When the app dies, three things must happen in this order: **the hardware is made safe**, the run record stops lying about being in progress, and enough evidence is captured on disk that the failure can be diagnosed without reproducing it.

## Current state

[`Program.cs`](../../src/HardwareTest/App/Program.cs) wraps `StartWithClassicDesktopLifetime` in a `try/catch` that logs `Log.Fatal` and rethrows. That is the only handler in the repo. Not hooked: `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, `Dispatcher.UIThread.UnhandledException`, and ReactiveUI's exception handler (`RxState` / `WithExceptionHandler` — ReactiveUI 23 replaced `RxApp.DefaultExceptionHandler`).

## Locked rules

- **Safety outranks diagnostics.** If a plan is executing, attempt abort/safe-stop *before* writing anything. A dossier is worth nothing next to an unattended DUT under load.
- **The crash handler may not crash.** Synchronous, allocation-light, reentrancy-guarded, bounded in size and time, every step individually wrapped. A failure inside it degrades to "write less", never to "throw".
- **Local and offline.** No network, no third-party SDK, no telemetry. Keep the dossier format stable and versioned so an uploader is purely additive later.
- **No dialog on crash.** The [appliance rule](../opentap-platform.md#interaction-contract-avalonia-owned) holds even here. Recovery is an in-panel banner on next launch, never an OS modal.
- **Redaction is policy, not an afterthought.** DUT serials and operator names are identifying data.

## Work items

### 1. Capture surfaces

Install all four in `Program.Main` before Avalonia starts, in a `CrashHandler.Install(...)`:

| Surface | Fatal? | Note |
| --- | --- | --- |
| `AppDomain.CurrentDomain.UnhandledException` | Yes | Last chance; process is going down regardless |
| `Dispatcher.UIThread.UnhandledException` | Recoverable | Avalonia lets you mark handled — log, dossier, keep running |
| `TaskScheduler.UnobservedTaskException` | No | Observe and log; these are usually leaks, not crashes |
| `RxState` / `WithExceptionHandler` | Recoverable | Replace the rethrowing ReactiveUI default with a handler that dossiers and surfaces a status message |

Distinguish **fatal** (dossier, then terminate) from **recoverable** (dossier, keep running, banner). Recoverable events must be rate-limited — a command failing in a loop must not fill the disk with dossiers.

### 2. Safe-stop interlock

On a fatal event, before any I/O: if a plan is running, call the existing abort path on `IOpenTapSession` with a hard timeout (~2s). Record whether the stop was confirmed, timed out, or was not attempted, and put that field near the top of the dossier — it is the first thing anyone will need to know.

### 3. Dossier layout

Under `{DataDirectory}/crashes/{utcTimestamp}-{shortId}/`:

| File | Contents |
| --- | --- |
| `crash.json` | Schema version, app version + commit ([Phase 4](phase-4-build-info.md)), runtime/OS/RID, culture, uptime, faulting thread, full exception chain with inner exceptions and stacks, `IsFatal`, safe-stop outcome, active `RunId` / `PlanId` if any |
| `log-tail.txt` | Last N KB of structured log leading up to the fault |
| `config.json` | Effective settings + provenance from [Phase 3](phase-3-configuration-model.md), redacted |
| `session.json` | Operator session state, redacted: DUT present yes/no, plan id, engineer-mode flag |

Cap total dossier size and cap retained dossiers (keep newest ~20, prune oldest) so a crash loop cannot consume the disk — a real risk given there is no free-space handling anywhere in the app today.

### 4. In-memory ring buffer log sink

The log tail cannot come from the rolling file: the file sink is `shared: true` and may not have flushed the interesting lines. Add a bounded ring-buffer Serilog sink (a few thousand entries) in [`LoggingBootstrap`](../../src/HardwareTest.Core/Logging/LoggingBootstrap.cs) that the crash handler drains synchronously. It doubles as the backing store for an in-app log viewer later.

### 5. Dangling run reconciliation

On startup, scan `runs/` for records still in a running state and reconcile them to aborted with reason `ProcessInterrupted`, linking the crash dossier id when timestamps correlate. Without this, Results shows a run that never ends and DUT history compares against a truncated record. This is worth doing even independently of crash capture — a power cut produces the same artifact.

### 6. Recovery UX

On next launch, if unreviewed dossiers exist, show a dismissible in-panel banner on Home: what happened, when, which version. Actions: **Open folder**, **Export support bundle** (zip of the dossier), **Dismiss**. Mark reviewed by writing a marker file inside the dossier, not by deleting it.

### 7. Redaction policy

One `CrashRedaction` policy shared with [Phase 3](phase-3-configuration-model.md)'s diagnostics dump. Default: replace DUT serial and operator name with a stable salted hash so recurrence can still be correlated across dossiers without storing identity. Filesystem paths keep the leaf and elide the user directory. Add `RedactIdentifiersInDiagnostics` (default on) so a lab that wants raw values can opt out.

### 8. Configuration

`HARDWARETEST_CRASH_DIRECTORY`, `HARDWARETEST_CRASH_ENABLED`, retention count — through the Phase 3 binder, so a container or a bench script can redirect dossiers without editing JSON.

## Tests

- Dossier writer produces valid `crash.json` from a synthetic nested exception, with no external dependencies.
- Reentrancy: a handler invoked while already handling writes one dossier, not two, and does not deadlock.
- Retention prunes to the configured count.
- Redaction: DUT serial does not appear in any dossier file when the policy is on.
- Reconciliation converts a running-state `run.json` to aborted on startup.
- The ring sink returns entries in order and stays bounded under sustained logging.
- An internal `--simulate-crash {fatal|recoverable|command}` switch (Debug builds only) exercises the whole pipeline end to end, including the safe-stop interlock against a fake session.

## Exit criteria

- [x] An exception thrown from a `ReactiveCommand` no longer terminates the process.
- [x] A fatal crash during a run attempts a safe stop and records the outcome.
- [x] After a forced crash, the next launch shows a recovery banner and the interrupted run reads as aborted, not running.
- [x] **Export support bundle** produces one zip that identifies build, config, and fault without any identifying data.
- [x] A crash loop cannot exhaust the disk.

## Out of scope

- Remote upload, crash aggregation, third-party SDKs — the format is designed to make these additive.
- Native minidumps. Revisit only if a Skia or VISA driver fault appears in the field; managed stacks cover the realistic failures.
- Watchdog / automatic restart supervision — appliance concern, belongs with the deferred OS integration.
