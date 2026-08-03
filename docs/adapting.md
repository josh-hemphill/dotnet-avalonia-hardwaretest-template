# Adapting this template to your product

This repo is a working Avalonia + OpenTAP hardware-test shell. Keep the layering (`HardwareTest` UI → focused `IOpenTap*` session surfaces → plugins/plans → `HardwareTest.Core`) and replace the sample product pieces below.

For UI vs OpenTAP test suites, see [testing.md](testing.md). For sealed Linux publish layout, see [appliance-linux.md](appliance-linux.md). For the deeper OpenTAP platform roadmap (Avalonia-owned interactions, parameters, mixins, packages list, multi-DUT), see [opentap-platform.md](opentap-platform.md) and [opentap-phases/](opentap-phases/). Platform hardening (config, crash, storage, operator UX) is [platform-roadmap.md](platform-roadmap.md). Longer-horizon items live under [deferred/](deferred/).

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
  "requireRevision": false,
  "selectionIncludesCleanup": true
}
```

`selectionIncludesCleanup` defaults to **true** (omit or set true). Set **false** only when SafeShutdown is suite-scoped and Run Selected is software-only — then the selection mask excludes `SafeShutdownStep`. Full **Run** always executes the plan as authored. Disabled siblings outside the mask may show NotExecuted/Invalidated; that is expected and is not “cleanup skipped.”
3. Built-in **sample** / **board-demo** / **sweep-demo** entries stay as factories for CI-stable demos. Disk plans with the same id are not double-listed (`ProgramCatalog`).

   | Demo | Operator prompts | Station overrides / Presentation |
   | --- | --- | --- |
   | **sample** (`SampleProgramFactory`) | `Confirm Sweep Area Clear` (confirm-only) → `Install Sweep Fixture` (typed: `fixtureId`, `fixtureTorqueNm`) | Acquire/Mean settings + Annotation on Identity; Presentation: Acquire `VDC` timeseries, Mean `VDC.mean` scalar |
   | **board-demo** (`BoardDemoProgramFactory`) | `Seat Board Fixture` (confirm) → `Record Board Sticker` (typed: `boardLotId`) | Multi-rail Acquire/Mean; Presentation: `rail.3v3` / `rail.5v` / `bus.vdc` timeseries + mean scalar/passband (see [phase-i](opentap-phases/phase-i-presentation-contract.md)) |
   | **sweep-demo** (`SweepDemoProgramFactory`) | (none) | Repeat ×3; Presentation `sweep.vdc` timeseries + loop iteration stamps |

4. Run and Instruments both enumerate via `ProgramCatalog` — no need to hardcode program lists in ViewModels.

## 2. Plugins

1. Add an OpenTAP plugin project (see [`HardwareTest.OpenTap.Plugins.Basic`](../src/HardwareTest.OpenTap.Plugins.Basic/) and mixins in [`HardwareTest.OpenTap.Plugins.Mixins`](../src/HardwareTest.OpenTap.Plugins.Mixins/)).
2. The host always searches the Basic and Mixins plugin assembly directories.
3. Extra search paths:
   - `AppSettings.OpenTapPluginDirectories`
   - Env `HARDWARETEST_OPENTAP_PLUGIN_DIRS` (`;` or `Path.PathSeparator` separated)
4. On an appliance, drop third-party plugin DLLs under a writable/plugin folder and list that path in settings (see [appliance-linux.md](appliance-linux.md)).
5. Verify installed packages and plugin dirs in **Settings → OpenTAP packages & plugins** (offline list only; install via `tap package install` / bake — see [phase-e-packages-list.md](opentap-phases/phase-e-packages-list.md)).

## 3. Station bindings (Instruments)

1. Load programs → OpenTAP slots appear on Instruments.
2. Discover resources from either column, then set per-plan slot overrides (`PlanSlotOverrides` in settings):
   - **VISA** — IVI `GlobalResourceManager.Find` (or mock catalog when `UseMockVisa`), with parsed interface hints (USB/TCPIP/…). Toggling `UseMockVisa` in Settings takes effect immediately (no restart required) when no run is active and no VISA sessions are open; refused flips revert the checkbox to the current effective mode.
   - **OpenTAP** — `IDeviceDiscovery` plugins for `VisaAddress` (`IOpenTapHostCatalog.ListDiscoveredDeviceAddresses`).
   - **Query *IDN?** — opt-in; opens the selected address briefly to confirm manufacturer/model/serial. Not run on Discover.
3. Slots are discovered from any OpenTAP `Instrument` referenced by step properties. Resource strings prefer **`VisaAddress`**, then `ResourceName`, then `Address` ([`InstrumentResourceAccess`](../src/HardwareTest.OpenTap.Host/InstrumentResourceAccess.cs)). Instruments without a writable resource property will show on the page but cannot be rebound from the UI.
4. The Host does **not** call `Instrument.Open`/`Close` — OpenTAP opens instruments during plan execution. Avoid double-open from the shell.

### Third-party SCPI instrument

1. Author a TapPlan in OpenTAP Editor that references your SCPI plugin instrument (property named `VisaAddress` preferred).
2. Ship the plugin DLL via offline package install or `OpenTapPluginDirectories` (see [appliance-linux.md](appliance-linux.md)). Prefer plugins that implement `IDeviceDiscovery` so **Discover OpenTAP** lists their addresses.
3. On the bench, open **Instruments**, load the program, pick a discovered VISA or OpenTAP resource (or type one), save the slot override.
4. On **Run**, `ApplyStationAndDutAsync` writes the override onto the instrument before execute. Full ComponentSettings / bench-profile UI is deferred — see [deferred-bench-profile-ui.md](deferred/deferred-bench-profile-ui.md).

DUT stamping still looks for Basic `IdentityCheckStep` / `HardwareDut`. Custom DUT steps need a similar host hook or Identity-compatible step.

## 4. Plan contract (Run board)

- Prefer unique step paths (duplicate sibling names need path-qualified selection).
- Max useful nest depth for chrome is three levels (Stages → Sections → Nested).
- Include `SafeShutdownStep` when using Run Selected (selection keeps it enabled by default; opt out via `selectionIncludesCleanup: false` in `{planId}.program.json`). Disabled siblings may show NotExecuted/Invalidated — that is not cleanup being skipped.
- Repeat/Sweep loops show innermost `iter i/N` on the Run hero during execute; edit bounds in OpenTAP Editor or Phase C overrides — not in Avalonia.
- Details and diagnostic tests: [testing.md](testing.md).

### Operator session (DUT confirm / idle)

- Confirm DUT (and operator when `requireOperator` is true) once per session; sticky strip on Run shows last activity and time remaining to soft-warn / Stale.
- Idle uses **last operator activity** (`LastActivityAt`), not confirm time — reviewing Results / reports / navigating between pages refreshes activity.
- Soft-warn (default 80% of idle window) then hard Stale; resolutions are **Same DUT** / **Change Session** (in-panel only). Idle is checked on an interval, not only at Run.
- Canonical idle setting: **`OperatorSessionIdleMinutes`** (default 240). Hours env/CLI (`OperatorSessionIdleHours` / `HARDWARETEST_OPERATOR_SESSION_IDLE_HOURS` / `--session-idle-hours`) remain aliases; minutes wins when both are set.
- Optional station policy **`RequireDutConfirmEveryRun`**: after each terminal run, session goes Stale until Same DUT / Change Session.
- Technician required indicator and Same DUT validation follow program `requireOperator` (Stale prompt shows a technician field when required and none is stored).
- **Multi-DUT:** near-term [Phase K](opentap-phases/phase-k-multi-dut-parallel.md) adds multiple DUT sessions with one plan at a time (K.1). Today the shell is single-session.

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

Default embedded templates: `test-report.typ` (status; `status-report.typ` is an alias) + `certification-report.typ` + `lib/sample-chart.typ`.

Override without recompiling:

1. Set `AppSettings.DataDirectory` to your writable root.
2. Place files under `{DataDirectory}/reports/` (and `reports/lib/` for chart lib).
3. Optionally set `AppSettings.ReportTemplateName` (default `test-report.typ`). A file with that name under `{DataDirectory}/reports/` wins over the embedded template.

Compile inputs: `run.json` (camelCase `TestRunRecord`), Typst inputs (`title`, `runId`, `planName`, `dutSerial`, `result`, …), and optional sample-driven charts via `sample-chart.typ`. `EmbedPlotsInReport` toggles chart notes.

### MES / QA file export (optional)

Set `AppSettings.ExportOpenTapResults` (Settings → **Export OpenTAP results (CSV)**). During each run the host attaches a ResultListener that writes published OpenTAP tables beside the run folder:

`{DataDirectory}/runs/{runId}/opentap-results/{TableName}.csv`

(e.g. `Sample.csv`, `Identity.csv`, `Analyze.csv`). Typst `report.pdf` and `run.json` are unchanged. Default is off.

### DUT history (local)

After Pass/Fail, the shell compares channel means on the current run to the last 10 local runs with the same DUT serial + plan ([`DutHistoryService`](../src/HardwareTest.Core/Runs/DutHistoryService.cs)). Metrics group by Presentation `MetricKey` when set (else Channel). Watch/Alert thresholds default to 5%/10%, or per-metric via Presentation `HistoryWatchPercent` / `HistoryAlertPercent` / `HistoryEnabled`. Absent `HistoryEnabled` (legacy records) means **no comparison**, not default thresholds. History detail (metric table) lives on **Results**; the Run board banner is off by default (`ShowDutHistoryOnRun`).

### Presentation contract (Phase I) + UI (Phase J) + band-first authoring (Phase L)

Publish tables `Sample` (Channel, Index, Value) and `Scalar` (Name, Value, Unit, optional LimitLow/LimitHigh). Attach **Presentation** mixin (`ChannelKey`, `DisplayRole`, `YUnit`, optional history thresholds) in Editor or via demos. Results lines show `MetricKey [role] value unit`. Run maps `timeseries` → Focus trend when earned, `scalar`/`passband` → Band gauges; Results prefers gauges then charts. Full matrix: [phase-i-presentation-contract.md](opentap-phases/phase-i-presentation-contract.md), [phase-j-presentation-ui.md](opentap-phases/phase-j-presentation-ui.md), [phase-l-presentation-authoring.md](opentap-phases/phase-l-presentation-authoring.md). Shell Band/Focus: [phase-16-band-focus-presentation.md](platform-phases/phase-16-band-focus-presentation.md).

#### Band-first authoring cookbook

| Recipe | What to publish | Role | Limits | When to also publish timeseries |
| --- | --- | --- | --- | --- |
| Rail / mean in band | `rail.X.mean` (or equivalent) final value | `passband` | LimitLow / LimitHigh = spec | Acquire series optional for Focus/debug |
| Scalar threshold (GTE/LTE) | Mean / computed value | `scalar` or `passband` | One-sided or two-sided limits | Only if shape matters |
| Bump / pulse timing | Derived: `bump.rise.ms`, `bump.width.ms`, `bump.peak` | `scalar` / `passband` | Window bounds as limits | Raw series only for Focus |
| Hi → Low return | Derived: `return.high.at.ms`, `return.low.at.ms`, or excursion | `scalar` / `passband` | Timing + amplitude limits | Raw series only for Focus |
| Envelope / return bounds | Derived: `envelope.error` / `overshoot` / `undershoot` | `passband` | Spec envelope | Raw series for Focus |

Rules of thumb: (1) write pass criteria in words first; (2) publish **one Scalar per criterion** with limits; (3) keep `ChannelKey` stable; (4) add `timeseries` only when Focus trend is useful. Demo: **Timing / Envelope Demo (Band-first)** (`timing-demo`) plus Sample/Board.

### Reports (multi-PDF)

Programs declare `reportKinds` in the catalog / `{planId}.program.json` (default `["status"]`). Optional `defaultReportKind` chooses which PDF Results opens on double-click (default `status`). Sample and Board demos generate **status** (includes DUT history when available) and **certification** (pass/fail + measurements only). PDFs land as `runs/{runId}/status.pdf` and `certification.pdf`; `ReportPdfPath` points at status for back compat. Results: click a run for detail, double-click for the default report, or Open a specific artifact.

Loop samples stamp `IterationIndex` / `LoopPath` on `StoredSample` for report charts (last value per iteration); live Run plot stays chronological.

## 8. What stays demo-specific

- Basic plugin steps (`AcquireVoltageStep`, `MeanGteStep`, `OperatorInputStep`, …).
- Engineer/Debug overlays `TrySetAcquireSettings` / `TrySetMeanGteThreshold` (sample step types only; prefer the parameter bridge). Prefer `TryGetStepConditionSummary` for read-only display of unknown steps.
- `LoadSampleProgramAsync` / `LoadBoardDemoProgramAsync` / `LoadTimingDemoProgramAsync` on `IOpenTapSession` — ignore once you only ship disk plans.

Plan-shape fixtures (`LoadPlanShapeAsync`) live on the concrete `OpenTapSession` / `FakeOpenTapSession` for tests, not on `IOpenTapSession`.

## 9. Rename checklist (optional)

When productizing the template name:

1. Rename solution/projects/namespaces from `HardwareTest` to your product id.
2. Update OpenTAP `[Display(..., Groups: ["HardwareTest"])]` on plugins.
3. Update CI paths, `dirs.proj`, publish output names, and Typst “Generated by …” strings.
4. Update env var prefix if desired (`HARDWARETEST_*` including `HARDWARETEST_OPENTAP_PLUGIN_DIRS`).
5. Re-run ViewModels, OpenTAP host, and E2E smoke tests with `-r win-x64`.

## 10. Configuration reference

Precedence (low → high): **built-in defaults → `settings.json` → environment → command line**.

Env alone is enough for a sealed install. Missing or read-only `settings.json` is degraded, not fatal. Settings UI save writes the file only for keys **not** overridden by env/CLI. Diagnostics table + `--print-config` show provenance.

| Setting | Environment | CLI |
| --- | --- | --- |
| `DataDirectory` | `HARDWARETEST_DATA_DIRECTORY` | `--data-directory` |
| `DefaultVisaResource` | `HARDWARETEST_DEFAULT_VISA_RESOURCE` | `--default-visa-resource` |
| `UseMockVisa` | `HARDWARETEST_USE_MOCK_VISA` | `--mock-visa` |
| `LogMinimumLevel` | `HARDWARETEST_LOG_MINIMUM_LEVEL` | `--log-level` |
| `EnableOsEventSink` | `HARDWARETEST_ENABLE_OS_EVENT_SINK` | `--enable-os-event-sink` |
| `EnableSyslogOnUnix` | `HARDWARETEST_ENABLE_SYSLOG_ON_UNIX` | `--enable-syslog` |
| `SyslogHost` | `HARDWARETEST_SYSLOG_HOST` | `--syslog-host` |
| `SyslogPort` | `HARDWARETEST_SYSLOG_PORT` | `--syslog-port` |
| `PlotRefreshHz` | `HARDWARETEST_PLOT_REFRESH_HZ` | `--plot-refresh-hz` |
| `ThemePreference` | `HARDWARETEST_THEME_PREFERENCE` | `--theme` |
| `EmbedPlotsInReport` | `HARDWARETEST_EMBED_PLOTS_IN_REPORT` | `--embed-plots` |
| `ExportOpenTapResults` | `HARDWARETEST_EXPORT_OPENTAP_RESULTS` | `--export-opentap-results` |
| `ShowDutHistoryOnRun` | `HARDWARETEST_SHOW_DUT_HISTORY_ON_RUN` | `--show-dut-history-on-run` |
| `OperatorSessionIdleMinutes` | `HARDWARETEST_OPERATOR_SESSION_IDLE_MINUTES` | `--session-idle-minutes` |
| `OperatorSessionIdleHours` *(alias)* | `HARDWARETEST_OPERATOR_SESSION_IDLE_HOURS` | `--session-idle-hours` |
| `OperatorSessionIdleWarnPercent` | `HARDWARETEST_OPERATOR_SESSION_IDLE_WARN_PERCENT` | `--session-idle-warn-percent` |
| `RequireDutConfirmEveryRun` | `HARDWARETEST_REQUIRE_DUT_CONFIRM_EVERY_RUN` | `--require-dut-confirm-every-run` |
| `IsEngineerDebugMode` | `HARDWARETEST_ENGINEER_DEBUG` | `--engineer-debug` |
| `OpenTapPluginDirectories` | `HARDWARETEST_OPENTAP_PLUGIN_DIRS` *(legacy name; `;` / `Path.PathSeparator`)* | `--opentap-plugin-dirs` |
| `ReportTemplateName` | `HARDWARETEST_REPORT_TEMPLATE_NAME` | `--report-template` |
| `CrashEnabled` | `HARDWARETEST_CRASH_ENABLED` | `--crash-enabled` |
| `CrashDirectory` | `HARDWARETEST_CRASH_DIRECTORY` | `--crash-directory` |
| `CrashRetentionCount` | `HARDWARETEST_CRASH_RETENTION_COUNT` | `--crash-retention` |
| `RedactIdentifiersInDiagnostics` | `HARDWARETEST_REDACT_IDENTIFIERS` | `--redact-identifiers` |
| `ExportDirectory` | `HARDWARETEST_EXPORT_DIRECTORY` | `--export-directory` |
| `PreferRemovableExport` | `HARDWARETEST_PREFER_REMOVABLE_EXPORT` | `--prefer-removable-export` |
| `RunRetentionDays` | `HARDWARETEST_RUN_RETENTION_DAYS` | `--run-retention-days` |
| `RunRetentionMaxRuns` | `HARDWARETEST_RUN_RETENTION_MAX_RUNS` | `--run-retention-max-runs` |
| `DataFreeSpaceWarnBytes` | `HARDWARETEST_DATA_FREE_SPACE_WARN_BYTES` | `--data-free-space-warn-bytes` |
| `DataFreeSpaceCriticalBytes` | `HARDWARETEST_DATA_FREE_SPACE_CRITICAL_BYTES` | `--data-free-space-critical-bytes` |
| `AllowOsFolderBrowse` | `HARDWARETEST_ALLOW_OS_FOLDER_BROWSE` | `--allow-os-folder-browse` |

Also: `--settings <path>` (settings file path), `--print-config` (dump effective config + provenance to stdout and exit 0), `--version` / `-v` (print informational version and exit 0). Debug builds: `--simulate-crash {fatal|recoverable|command}`. Nested lists use `HARDWARETEST_<LIST>__{n}__<PROP>` (e.g. `HARDWARETEST_INSTRUMENTS__0__RESOURCE`).

Crash dossiers land under `{DataDirectory}/crashes/` (or `CrashDirectory`): `crash.json`, `log-tail.txt`, `config.json`, `session.json`. Home shows a dismissible recovery banner for unreviewed dossiers; Settings → **Open crashes folder** (hidden when `AllowOsFolderBrowse` is false and Engineer debug is off). See [phase-6-crash-reporting.md](platform-phases/phase-6-crash-reporting.md).

**Export / storage (Phase 10):** Results **Export to…** copies run PDFs + `run.json` (+ optional CSV) to removable media or `ExportDirectory`. Home crash **Export support bundle** uses the same targets (falls back to `{DataDirectory}/exports`). Retention prunes completed `runs/` folders by age/count; free-space warn/critical gates Run. See [phase-10-export-storage-chrome.md](platform-phases/phase-10-export-storage-chrome.md).

Bootstrap is two-stage: stage 1 resolves `DataDirectory` + `LogMinimumLevel` from env/CLI before logging; stage 2 loads `settings.json` then re-applies overlays. See [phase-3-configuration-model.md](platform-phases/phase-3-configuration-model.md).

### Schema versions

Every persisted JSON document carries an integer `schemaVersion`. Bumps are deliberate (see [`SchemaVersions`](../src/HardwareTest.Core/Serialization/SchemaVersions.cs)). Absent/`0` = legacy; greater than current = load read-only (never overwrite). Golden fixtures live under `tests/fixtures/schema/`.

| Document | Current | Changelog |
| --- | --- | --- |
| `AppSettings` (`settings.json`) | 1 | Initial stamped shape (Phase 5). |
| `UiState` (`ui-state.json`) | 1 | Initial stamped shape (Phase 5). |
| `TestRunRecord` (`runs/{id}/run.json`) | 1 | Initial stamped shape (Phase 5). `StoredSample.HistoryEnabled` is nullable so absent ≠ default. |
| `SuiteRunRecord` (`runs/suites/{id}/suite-run.json`) | 1 | Initial stamped shape (Phase 5). |
| `CrashReport` (`crashes/{id}/crash.json`) | 1 | Initial crash dossier (Phase 6). |

## 11. Custom mixins

Use mixins for product-specific step settings without forking every step type.

1. Create a plugin class library (OpenTAP package reference matching Basic) with:
   - An embed type implementing `IMixin` and `[Display]` properties.
   - An `IMixinBuilder` marked `[MixinBuilder(typeof(ITestStep))]` that returns `MixinMemberData` with `EmbedPropertiesAttribute`.
2. Ship the DLL beside Basic or add its folder to `OpenTapPluginDirectories`.
3. Attach in **OpenTAP Editor** (right-click step → Add Mixin). Do not expect an Avalonia “Add Mixin” control.
4. On the Run board (Engineer/Debug), select the step → **Station overrides** shows grouped mixin fields → **Apply & save** persists `PlanParameterOverrides` (TapPlan unchanged).
5. Demo reference: `AnnotationMixin` / `AnnotationMixinBuilder`; sample Identity Check is pre-attached for CI/UI demos. Details: [phase-d-mixins.md](opentap-phases/phase-d-mixins.md).
