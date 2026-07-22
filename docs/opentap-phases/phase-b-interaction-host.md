# Phase B — Avalonia interaction host + input steps

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** [Phase A](phase-a-interaction-contract.md)  
**Unblocks:** Phase C (shared field editors)

## Goal

Replace ad-hoc prompt-only UX with an **in-panel** Run-board interaction host that supports confirm + typed inputs. Author `OperatorInputStep` in Basic plugins — never OpenTAP `DialogStep` / OS modals.

## Locked rules

- Interaction UI lives inside Run view (banner/panel), not a `Window`.
- Same field control types will be reused for pre-run parameters (Phase C).
- Headless E2E must drive Continue + field fill without real dialogs.

## Work items

1. **Run board UI** ([`RunTestView.axaml`](../../src/HardwareTest/Features/RunTest/RunTestView.axaml) / ViewModel):
   - When `IsAwaitingOperator` / pending request: show title, message, dynamic fields, Continue, Abort.
   - Bind field editors from request field kinds (TextBox / Numeric / CheckBox).
   - Continue builds `OperatorInteractionResponse` and calls session Resume.

2. **`OperatorInputStep`** in Plugins.Basic:
   - Configurable message + one or more field definitions (or a small fixed set for v1: e.g. string + optional number).
   - Calls Phase A `RequestInteraction`; reads response into step results / parameters.
   - Publish results for diagnostics (similar to OperatorPrompt).

3. **Keep `OperatorPromptStep`** as confirm-only.

4. **Docs:** adapting / opentap-platform — how to author input steps; forbid floating dialogs on appliance.

5. **Tests:**
   - ViewModels: Fake emits input request → VM fills → response returned.
   - E2E: plan with `OperatorInputStep` pauses → set values → Continue → run finishes.

## Exit criteria

- Confirm + at least one typed input works on Run board without a second window.
- E2E green for input path.
- Sample/board-demo prompts unchanged in behavior.

## Out of scope

- Full parameter enumeration of TapPlan members (Phase C).
- Mixins (Phase D).
- OpenTAP built-in Dialog step hosting.
