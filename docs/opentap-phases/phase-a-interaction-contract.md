# Phase A — Operator interaction contract

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** nothing  
**Unblocks:** Phase B

## Goal

Introduce typed `OperatorInteractionRequest` / `OperatorInteractionResponse` and bridge them through `StepRuntime` without changing Run-board layout yet. Existing Continue prompts keep working.

## Locked rules

- Avalonia will own UI later; this phase is Host + plugin API only.
- No floating windows.
- Blocking wait on the plan thread via the existing pause gate pattern.

## Work items

1. **Types in Host** (e.g. `OperatorInteraction.cs`):
   - `OperatorInteractionFieldKind`: `String`, `Number`, `Boolean` (extend later if needed).
   - `OperatorInteractionField`: id, label, kind, optional default, optional required/validation hint.
   - `OperatorInteractionRequest`: id, title, message, fields (empty = confirm-only).
   - `OperatorInteractionResponse`: request id, cancelled flag, `Dictionary<string, string>` (or typed) values.

2. **`StepRuntime` generalization** ([`StepRuntime.cs`](../../src/HardwareTest.OpenTap.Plugins.Basic/StepRuntime.cs) at the time; [Phase 24](../platform-phases/phase-24-session-decomposition.md) replaced the statics with [`IStepRuntime`](../../src/HardwareTest.OpenTap.Plugins.Basic/IStepRuntime.cs)):
   - Prefer `Func<OperatorInteractionRequest, OperatorInteractionResponse>? RequestInteraction` (or Host-assigned callback that blocks until response).
   - Keep `RequestOperatorAttention(string)` as a thin wrapper that builds a confirm-only request (compat for existing steps).
   - Document thread rules: called from OpenTAP plan thread; must not touch Avalonia controls directly.

3. **`OpenTapSession`**:
   - On interaction: set awaiting state, raise progress with request payload (or dedicated event), Pause.
   - `Resume(OperatorInteractionResponse?)` / Continue maps response then unpauses.
   - Abort cancels pending interaction.

4. **`OperatorPromptStep`**: still works via string wrapper or confirm-only request.

5. **FakeOpenTapSession**: auto-complete or queueable responses for ViewModel tests.

6. **Tests**: host serial — prompt still completes with Resume; Fake can inject a response.

## Exit criteria

- [x] Sample / board-demo operator prompts still pass E2E and host tests.
- [x] Contract documented in [opentap-platform.md](../opentap-platform.md).
- [x] No new Avalonia Window.

## Out of scope

- Rich interaction panel UI / `OperatorInputStep` — see [Phase B](phase-b-interaction-host.md) (Done).
- Parameter panel (Phase C).
