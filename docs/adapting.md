# Adapting this template to your product

This repo is a working Avalonia + OpenTAP hardware-test shell. Keep the layering (`HardwareTest` UI → `IOpenTapSession` → plugins/plans → `HardwareTest.Core`) and replace the sample product pieces below.

For UI vs OpenTAP test suites, see [testing.md](testing.md). For sealed Linux publish layout, see [appliance-linux.md](appliance-linux.md). For the deeper OpenTAP platform roadmap (Avalonia-owned interactions, parameters, mixins, packages list), see [opentap-platform.md](opentap-platform.md) and [opentap-phases/](opentap-phases/).

## 1. Programs (TapPlans + catalog)

1. Author plans in **OpenTAP Editor** and ship locked `.TapPlan` files under [`plans/opentap/`](../plans/opentap/) (copied to `Programs/` on build).
2. Optional sidecar beside a plan: `{planId}.program.json` for display name, DUT family, and session requirements:

```json
{
  "displayName": "Power Board Suite",
  "dutFamily": "power",
  "requireSerial": true,
  "requireOperator": true,
  "requirePartNumber": false,
  "requireRevision": false
}
```

3. Built-in **sample** / **board-demo** entries stay as factories for CI-stable demos. Disk plans with the same id are not double-listed (`ProgramCatalog`).

   | Demo | Operator prompts | Station overrides (Engineer/Debug) |
   | --- | --- | --- |
   | **sample** (`SampleProgramFactory`) | `Confirm Sweep Area Clear` (confirm-only) → `Install Sweep Fixture` (typed: `fixtureId`, `fixtureTorqueNm`) | `Acquire VDC` / `Mean GTE` — `Channel`, `SampleCount`, `IntervalMs`, `Threshold`, `Enabled` (stable step Ids); **Identity Check** has Annotation mixin (`Note`, `IncludeInReport`) |
   | **board-demo** (`BoardDemoProgramFactory`) | `Seat Board Fixture` (confirm) → `Record Board Sticker` (typed: `boardLotId`) | Multi-rail Acquire/Mean with `Channel` / samples / thresholds; 3V3 rail uses stable Ids for override demos |

4. Run and Instruments both enumerate via `ProgramCatalog` — no need to hardcode program lists in ViewModels.

## 2. Plugins

1. Add an OpenTAP plugin project (see [`HardwareTest.OpenTap.Plugins.Basic`](../src/HardwareTest.OpenTap.Plugins.Basic/) and mixins in [`HardwareTest.OpenTap.Plugins.Mixins`](../src/HardwareTest.OpenTap.Plugins.Mixins/)).
2. The host always searches the Basic and Mixins plugin assembly directories.
3. Extra search paths:
   - `AppSettings.OpenTapPluginDirectories`
   - Env `HARDWARETEST_OPENTAP_PLUGIN_DIRS` (`;` or `Path.PathSeparator` separated)
4. On an appliance, drop third-party plugin DLLs under a writable/plugin folder and list that path in settings (see [appliance-linux.md](appliance-linux.md)).

## 3. Station bindings (Instruments)

1. Load programs → OpenTAP slots appear on Instruments.
2. Discover VISA (or mock) resources and set per-plan slot overrides (`PlanSlotOverrides` in settings).
3. Slots are discovered from any OpenTAP `Instrument` referenced by step properties. Resource strings use `ResourceName`, then `VisaAddress`, then `Address`. Instruments without a writable resource property will show on the page but cannot be rebound from the UI.

DUT stamping still looks for Basic `IdentityCheckStep` / `HardwareDut`. Custom DUT steps need a similar host hook or Identity-compatible step.

## 4. Plan contract (Run board)

- Prefer unique step paths (duplicate sibling names need path-qualified selection).
- Max useful nest depth for chrome is three levels (Stages → Sections → Nested).
- Include `SafeShutdownStep` when using Run Selected.
- Details and diagnostic tests: [testing.md](testing.md).

## 5. Operator interactions (no floating dialogs)

Use Avalonia-owned mid-run prompts only:

- **Confirm-only:** `OperatorPromptStep` or `StepRuntime.RequestOperatorAttention(message)`.
- **Typed input:** `OperatorInputStep` (string + optional number) or a custom step calling `StepRuntime.RequestInteraction` with `OperatorInteractionField`s.
- The Run board shows an in-panel host (title, message, fields). Continue / Cancel are toolbar actions — never OpenTAP `DialogStep`, WinForms/WPF message boxes, or a second window.

Field editors (`InteractionFieldViewModel`) are shared widgets; override and prompt collections stay separate.

## 6. Plan / step parameters vs operator prompts

Keep these separate:

- **Operator prompts (technician):** mid-run `OperatorInputStep` / `RequestInteraction` — values entered in the orange interaction host and returned to the step. Not saved as station overrides.
- **Station overrides (Engineer/Debug only):** Run board **Station overrides** panel for limits/channels/`Enabled`. **Apply & save** → `TrySetParameter` + `PlanParameterOverrides`. Re-applied on program load / before run. Does not rewrite the TapPlan.
- Prompt-schema properties on interaction steps (Message, field ids/labels) are not station overrides; only `Enabled` is overridable there.
- Prefer `EnumerateParameters` / `TrySetParameter` for new product code. `TrySetAcquireSettings` / `TrySetMeanGteThreshold` remain sample-only adapters around the bridge.

## 7. Reports (Typst)

Default embedded templates: `test-report.typ` + `lib/sample-chart.typ`.

Override without recompiling:

1. Set `AppSettings.DataDirectory` to your writable root.
2. Place files under `{DataDirectory}/reports/` (and `reports/lib/` for chart lib).
3. Optionally set `AppSettings.ReportTemplateName` (default `test-report.typ`).

Compile inputs: `run.json` (camelCase `TestRunRecord`), Typst inputs (`title`, `runId`, `planName`, `dutSerial`, `result`, …), and optional sample-driven charts via `sample-chart.typ`. `EmbedPlotsInReport` toggles chart notes.

## 8. What stays demo-specific

- Basic plugin steps (`AcquireVoltageStep`, `MeanGteStep`, `OperatorInputStep`, …).
- Engineer/Debug overlays `TrySetAcquireSettings` / `TrySetMeanGteThreshold` (sample step types only; prefer the parameter bridge). Prefer `TryGetStepConditionSummary` for read-only display of unknown steps.
- `LoadSampleProgramAsync` / `LoadBoardDemoProgramAsync` on `IOpenTapSession` — ignore once you only ship disk plans.

Plan-shape fixtures (`LoadPlanShapeAsync`) live on the concrete `OpenTapSession` / `FakeOpenTapSession` for tests, not on `IOpenTapSession`.

## 9. Rename checklist (optional)

When productizing the template name:

1. Rename solution/projects/namespaces from `HardwareTest` to your product id.
2. Update OpenTAP `[Display(..., Groups: ["HardwareTest"])]` on plugins.
3. Update CI paths, `dirs.proj`, publish output names, and Typst “Generated by …” strings.
4. Update env var prefix if desired (`HARDWARETEST_OPENTAP_PLUGIN_DIRS`).
5. Re-run ViewModels, OpenTAP host, and E2E smoke tests with `-r win-x64`.

## 10. Custom mixins

Use mixins for product-specific step settings without forking every step type.

1. Create a plugin class library (OpenTAP package reference matching Basic) with:
   - An embed type implementing `IMixin` and `[Display]` properties.
   - An `IMixinBuilder` marked `[MixinBuilder(typeof(ITestStep))]` that returns `MixinMemberData` with `EmbedPropertiesAttribute`.
2. Ship the DLL beside Basic or add its folder to `OpenTapPluginDirectories`.
3. Attach in **OpenTAP Editor** (right-click step → Add Mixin). Do not expect an Avalonia “Add Mixin” control.
4. On the Run board (Engineer/Debug), select the step → **Station overrides** shows grouped mixin fields → **Apply & save** persists `PlanParameterOverrides` (TapPlan unchanged).
5. Demo reference: `AnnotationMixin` / `AnnotationMixinBuilder`; sample Identity Check is pre-attached for CI/UI demos. Details: [phase-d-mixins.md](opentap-phases/phase-d-mixins.md).
