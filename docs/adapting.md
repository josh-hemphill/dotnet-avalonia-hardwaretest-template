# Adapting this template to your product

This repo is a working Avalonia + OpenTAP hardware-test shell. Keep the layering (`HardwareTest` UI → focused `IOpenTap*` session surfaces → plugins/plans → `HardwareTest.Core`) and replace the sample product pieces below.

For UI vs OpenTAP test suites, see [testing.md](testing.md). For sealed Linux publish layout, see [appliance-linux.md](appliance-linux.md). For the deeper OpenTAP platform roadmap (Avalonia-owned interactions, parameters, mixins, packages list, multi-DUT), see [opentap-platform.md](opentap-platform.md) and [opentap-phases/](opentap-phases/). Platform hardening (config, crash, storage, operator UX) is [platform-roadmap.md](platform-roadmap.md). Longer-horizon items live under [deferred/](deferred/).

## 1. Author a locked program (cookbook)

A **locked program** is the bake-ready unit: `.TapPlan` + `{planId}.program.json` sidecar + Presentation/limits on function steps + plugin set (`.TapPackage` Dependencies) + Typst `reportKinds`. Author in **OpenTAP Editor / TUI**. This shell does not edit plans. `HardwareTest.PlanValidate --strict` (Host `PlanContractValidator`) must fail a bad package **before** appliance bake. Operator Run is not blocked by authoring warnings.

### Deliverables

| Piece | Where | Notes |
| --- | --- | --- |
| `.TapPlan` | [`plans/opentap/`](../plans/opentap/) (copied to `Programs/` on build) | Locked XML. Start from `sample.TapPlan`. |
| `{planId}.program.json` | Same folder | Session / DUT / Typst only. Copy [`template.program.json`](../plans/opentap/template.program.json). Keep `"$schema": "./program.schema.json"`. |
| Plugin set | TapPackage **Dependencies** | HardwareTest Basic + Mixins (+ product packs). Not listed in the sidecar. |
| Presentation / limits | Mixins on **function** leaves | Unique `ChannelKey`; scalar/passband need `LimitLow`/`LimitHigh`/`Threshold`. |
| `reportKinds` | Sidecar | `status` and/or `certification`. |

Instrument / component requirements belong in `.TapPackage` Dependencies, not the sidecar.

### Engineer loop

1. **Install authoring packs** into Editor/TUI (same versions the bench uses):

   ```bash
   dotnet build src/HardwareTest.OpenTap.Plugins.Basic -c Release -r linux-x64 -p:CreateOpenTapPackage=true -p:InstallCreatedOpenTapPackage=false
   dotnet build src/HardwareTest.OpenTap.Plugins.Mixins -c Release -r linux-x64 -p:CreateOpenTapPackage=true -p:InstallCreatedOpenTapPackage=false
   tap package install path/to/HardwareTest\ Basic*.TapPackage
   tap package install path/to/HardwareTest\ Mixins*.TapPackage
   ```

2. **Copy** `sample.TapPlan` + `sample.program.json` (or `template.program.json` renamed to `{planId}.program.json`). Keep `"$schema": "./program.schema.json"` for editor intellisense.

3. **Author** in TUI/Editor:
   - Three-level groups (`Setup` / function groups / `Cleanup`).
   - Unique leaf names; Identity when `requireSerial` is true.
   - Operator flow: `OperatorPromptStep` / `OperatorInputStep` — not `DialogStep`.
   - Presentation mixin on every function leaf (`ChannelKey` unique).
   - `SafeShutdownStep` in Cleanup.
   - Resource property named `VisaAddress` (or `ResourceName` / `Address`) so Instruments can rebind.

4. **Function steps** must publish Phase L `Sample` / `Scalar` and carry Presentation. Identity / Prompt / Input / SafeShutdown / HangForever / RepeatLoop / TestGroup are exempt.

5. **Validate pack-mode** (missing sidecar is an error; authoring warnings do not block operator Run):

   ```bash
   HardwareTest.PlanValidate plans/opentap --strict
   HardwareTest.PlanValidate plans/opentap --strict --format json
   ```

   Ad-hoc single-plan check (same Host validator; missing sidecar stays a **warning** because `--strict` is not set):

   ```bash
   HardwareTest --validate-plan path/to/plan.TapPlan
   ```

   Both entry points reuse `PlanContractValidator` in Host (Avalonia-free). Exit `1` on errors, `0` when only warnings remain. Missing `--validate-plan` path prints usage and exits `2` — it does not launch the UI. `HardwareTest.PlanValidate --opentap-plugin-dirs` trusts those **CLI** directories (authoring machine); `HARDWARETEST_OPENTAP_PLUGIN_DIRS` still requires appliance `PluginDirectoryTrust`. `HardwareTest --validate-plan` applies appliance trust (`{DataDirectory}/plugins` unless Engineer debug). `--format json|sarif` is for CI annotations.

6. **Pack** from [`plans/opentap/package.xml`](../plans/opentap/package.xml) (HardwareTest Template Program — sample plan + sidecar + schema; depends on Basic + Mixins, not Core). `tap package create` requires those authoring packs already installed (step 1):

   ```bash
   tap package create plans/opentap/package.xml
   ```

7. **Bake** the program pack and plugin packs onto the appliance. Inspect + mock Run (`UseMockVisa`) is still required before shipping.

`plans/opentap/fixtures/` are shape examples, not product plans. Top-level `plans/opentap/*.TapPlan` are the pack set (fixtures are a subdirectory and are not globbed). `selectionIncludesCleanup` defaults to **true**. Set **false** only when SafeShutdown is suite-scoped and Run Selected is software-only — then the selection mask excludes `SafeShutdownStep`. Full **Run** always executes the plan as authored. Disabled siblings outside the mask may show NotExecuted/Invalidated; that is expected and is not “cleanup skipped.”

```json
{
  "$schema": "./program.schema.json",
  "displayName": "Power Board Suite",
  "dutFamily": "power",
  "requireSerial": true,
  "requireOperator": true,
  "requirePartNumber": false,
  "requireRevision": false,
  "reportKinds": ["status", "certification"],
  "defaultReportKind": "status",
  "selectionIncludesCleanup": true
}
```

Built-in **sample** / **board-demo** / **sweep-demo** stay as factories for CI-stable demos. Disk plans with the same id are not double-listed (`ProgramCatalog`). Run and Instruments both enumerate via `ProgramCatalog` — no need to hardcode program lists in ViewModels.

| Demo | Operator prompts | Station overrides / Presentation |
| --- | --- | --- |
| **sample** (`SampleProgramFactory`) | `Confirm Sweep Area Clear` (confirm-only) → `Install Sweep Fixture` (typed: `fixtureId`, `fixtureTorqueNm`) | Acquire/Mean settings + Annotation on Identity; Presentation: Acquire `VDC` timeseries, Mean `VDC.mean` scalar |
| **board-demo** (`BoardDemoProgramFactory`) | `Seat Board Fixture` (confirm) → `Record Board Sticker` (typed: `boardLotId`) | Multi-rail Acquire/Mean; Presentation: `rail.3v3` / `rail.5v` / `bus.vdc` timeseries + mean scalar/passband (see [phase-i](opentap-phases/phase-i-presentation-contract.md)) |
| **sweep-demo** (`SweepDemoProgramFactory`) | (none) | Repeat ×3; Presentation `sweep.vdc` timeseries + loop iteration stamps |

## 2. Plugins

1. Add an OpenTAP plugin project (see [`HardwareTest.OpenTap.Plugins.Basic`](../src/HardwareTest.OpenTap.Plugins.Basic/) and mixins in [`HardwareTest.OpenTap.Plugins.Mixins`](../src/HardwareTest.OpenTap.Plugins.Mixins/)). The VISA broker adapter lives in [`HardwareTest.OpenTap.Plugins.Visa`](../src/HardwareTest.OpenTap.Plugins.Visa/) (bench only; not the Editor authoring pack).
2. The host always searches the Basic, Visa, and Mixins plugin assembly directories.
3. Extra search paths:
   - `AppSettings.OpenTapPluginDirectories`
   - Env `HARDWARETEST_OPENTAP_PLUGIN_DIRS` (`;` or `Path.PathSeparator` separated)
4. On an appliance, drop third-party plugin DLLs under a writable/plugin folder and list that path in settings (see [appliance-linux.md](appliance-linux.md)).
5. Verify installed packages and plugin dirs in **Settings → OpenTAP packages & plugins** (offline list only; install via `tap package install` / bake — see [phase-e-packages-list.md](opentap-phases/phase-e-packages-list.md)).

### Authoring packs (Editor / TUI)

Install the same **HardwareTest Basic** and **HardwareTest Mixins** versions the bench uses (commands in [§1 step 1](#engineer-loop)). `package.xml` lists only the plugin DLL (no `HardwareTest.Core`). The VISA adapter is not an authoring pack; product instrument types come from the visa/SCPI library later. Default builds keep `CreateOpenTapPackage=false` so CI does not run `tap package create`.

## 3. Station bindings (Instruments)

1. Load programs → OpenTAP slots appear on Instruments.
2. Discover resources from either column, then set per-plan slot overrides (`PlanSlotOverrides` in settings):
   - **VISA** — IVI `GlobalResourceManager.Find` (or mock catalog when `UseMockVisa`), with parsed interface hints (USB/TCPIP/…). Toggling `UseMockVisa` in Settings takes effect immediately (no restart required) when no run is active and no VISA sessions are open; refused flips revert the checkbox to the current effective mode.
   - **OpenTAP** — `IDeviceDiscovery` plugins for `VisaAddress` (`IOpenTapHostCatalog.ListDiscoveredDeviceAddresses`).
   - **Query *IDN?** — opt-in; opens the selected address briefly to confirm manufacturer/model/serial. Not run on Discover.
3. Slots are discovered from any OpenTAP `Instrument` referenced by step properties. Resource strings prefer **`VisaAddress`**, then `ResourceName`, then `Address` ([`InstrumentResourceAccess`](../src/HardwareTest.OpenTap.Host/InstrumentResourceAccess.cs)). Instruments without a writable resource property will show on the page but cannot be rebound from the UI.
4. The Host does **not** call `Instrument.Open`/`Close` — OpenTAP opens instruments during plan execution. Avoid double-open from the shell.

### Third-party SCPI instrument

1. Author a TapPlan in OpenTAP Editor or OpenTAP TUI that references your SCPI plugin instrument (property named `VisaAddress` preferred). Run `HardwareTest --validate-plan` (or `HardwareTest.PlanValidate`) before installing it on the bench.
2. Ship the plugin DLL via offline package install or `OpenTapPluginDirectories` (see [appliance-linux.md](appliance-linux.md)). Prefer plugins that implement `IDeviceDiscovery` so **Discover OpenTAP** lists their addresses.
3. On the bench, open **Instruments**, load the program, pick a discovered VISA or OpenTAP resource (or type one), save the slot override.
4. On **Run**, `ApplyStationAndDutAsync` writes the override onto the instrument before execute. Full ComponentSettings / bench-profile UI is deferred — see [deferred-bench-profile-ui.md](deferred/deferred-bench-profile-ui.md).

DUT stamping still looks for Basic `IdentityCheckStep` / `HardwareDut`. Custom DUT steps need a similar host hook or Identity-compatible step.

## 4. Plan contract (Run board)

`PlanContractValidator` (and the CLIs above) checks this contract. It does not replace a mock Run.

**TUI / Editor checklist** (same plugins as the bench: Basic + Mixins):

- Unique leaf paths (duplicate sibling names are fine when the full path is unique).
- Max useful nest depth for chrome is three levels (Stages → Sections → Nested); deeper still runs as leaves (warning, not a hard fail).
- Include `SafeShutdownStep` when using Run Selected (selection keeps it enabled by default; opt out via `selectionIncludesCleanup: false` in `{planId}.program.json`). Disabled siblings may show NotExecuted/Invalidated — that is not cleanup being skipped.
- At least one instrument with a writable `VisaAddress` / `ResourceName` / `Address` so Instruments can rebind.
- Use `OperatorPromptStep` / `OperatorInputStep`, never OpenTAP `DialogStep` or WinForms/WPF dialogs.
- Presentation: band-first (`scalar` / `passband` for pass criteria; `timeseries` only when shape matters).
- Sidecar `{planId}.program.json` present (warning if missing in ad-hoc validate; **error** under `--strict`) and schema-sane (invalid JSON is an error). Copy `plans/opentap/template.program.json` (`$schema` + `reportKinds`). Unknown sidecar properties warn; empty or unknown `reportKinds` / `defaultReportKind` error.

Repeat/Sweep loops show innermost `iter i/N` on the Run hero during execute; edit bounds in OpenTAP Editor / TUI or Phase C overrides — not in Avalonia.

Details and diagnostic tests: [testing.md](testing.md).

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

- **Confirm-only:** `OperatorPromptStep` or `IStepRuntime.RequestOperatorAttention(message)`.
- **Typed input:** `OperatorInputStep` (string + optional number) or a custom step calling `IStepRuntime.RequestInteraction` with `OperatorInteractionField`s.
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

Compile inputs: `run.json` (camelCase `TestRunRecord`), Typst inputs (`title`, `runId`, `planName`, `dutSerial`, `operatorName`, `attestationKind`, `attestationDetail`, `result`, …), and optional sample-driven charts via `sample-chart.typ`. `EmbedPlotsInReport` toggles chart notes. Certification export can require a detached `{kind}.attestation.json` sidecar (PIV on-card signature, or presence as a site-policy fallback) when `RequireAttestationBeforeExport` is on.

### MES / QA file export (optional)

Set `AppSettings.ExportOpenTapResults` (Settings → **Export OpenTAP results (CSV)**). During each run the host attaches a ResultListener that writes published OpenTAP tables beside the run folder:

`{DataDirectory}/runs/{runId}/opentap-results/{TableName}.csv`

(e.g. `Sample.csv`, `Identity.csv`, `Analyze.csv`). Typst `report.pdf` and `run.json` are unchanged. Default is off.

### DUT history (local)

After Pass/Fail, the shell compares channel means on the current run to the last 10 local runs with the same DUT serial + plan ([`DutHistoryService`](../src/HardwareTest.Core/Runs/DutHistoryService.cs)). Metrics group by Presentation `MetricKey` when set (else Channel). Watch/Alert thresholds default to 5%/10%, or per-metric via Presentation `HistoryWatchPercent` / `HistoryAlertPercent` / `HistoryEnabled`. Absent `HistoryEnabled` (legacy records) means **no comparison**, not default thresholds. History detail (metric table) lives on **Results**; when `ShowDutHistoryOnRun` is on, the one-line operator summary goes to the **shell notification strip** (Phase 17), not a growing Run hero row.

Opening a run also shows **Compare with previous**: the latest earlier run with the same DUT serial + plan ([`RunComparisonService`](../src/HardwareTest.Core/Runs/RunComparisonService.cs)). Missing metrics are listed as unavailable. Comparison never blocks Run. Export packages include `diagnostics.txt`; Settings **Copy diagnostics** includes catalog sidecar self-check.

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
| `ClockSkewWarnThresholdMinutes` | `HARDWARETEST_CLOCK_SKEW_WARN_THRESHOLD_MINUTES` | `--clock-skew-warn-threshold-minutes` |
| `NtpHost` | `HARDWARETEST_NTP_HOST` | `--ntp-host` |
| `UseMockOperatorCredential` | `HARDWARETEST_USE_MOCK_OPERATOR_CREDENTIAL` | `--mock-operator-credential` |
| `RequireCredentialForOperator` | `HARDWARETEST_REQUIRE_CREDENTIAL_FOR_OPERATOR` | `--require-credential-for-operator` |
| `RequireAttestationBeforeExport` | `HARDWARETEST_REQUIRE_ATTESTATION_BEFORE_EXPORT` | `--require-attestation-before-export` |
| `AllowPresenceInLieuOfSigning` | `HARDWARETEST_ALLOW_PRESENCE_IN_LIEU_OF_SIGNING` | `--allow-presence-in-lieu-of-signing` |

Also: `--settings <path>` (settings file path), `--print-config` (dump effective config + provenance to stdout and exit 0), `--validate-plan <path>` (validate a `.TapPlan` or a directory of plans and exit; `1` on errors, `0` if only warnings; bare/empty path exits `2` with usage and does not start the UI), `--version` / `-v` (print informational version and exit 0). Avalonia-free equivalent: `HardwareTest.PlanValidate <path> [...] [--strict] [--format text|json|sarif] [--opentap-plugin-dirs <dir>]` (explicit plugin dirs are trusted for that process; `--strict` fails a missing sidecar). Debug builds: `--simulate-crash {fatal|recoverable|command}`. Nested lists use `HARDWARETEST_<LIST>__{n}__<PROP>` (e.g. `HARDWARETEST_INSTRUMENTS__0__RESOURCE`).

Crash dossiers land under `{DataDirectory}/crashes/` (or `CrashDirectory`): `crash.json`, `log-tail.txt`, `config.json`, `session.json`. Unreviewed dossiers and recoverable faults publish to the **shell notification strip** (Phase 17) with Export / Open folder / Dismiss — not a competing Home hero card. Settings → **Open crashes folder** (hidden when `AllowOsFolderBrowse` is false and Engineer debug is off). See [phase-6-crash-reporting.md](platform-phases/phase-6-crash-reporting.md) and [phase-17-shell-notification-strip.md](platform-phases/phase-17-shell-notification-strip.md).

**Export / storage (Phase 10):** Results **Export to…** copies run PDFs + `run.json` + optional `*.attestation.json` (+ optional CSV) to removable media or `ExportDirectory`. Home crash **Export support bundle** uses the same targets (falls back to `{DataDirectory}/exports`). Retention prunes completed `runs/` folders by age/count using the injected clock; free-space warn/critical gates Run and surfaces on the **shell strip** (Critical is non-dismissible). See [phase-10-export-storage-chrome.md](platform-phases/phase-10-export-storage-chrome.md) and [phase-17-shell-notification-strip.md](platform-phases/phase-17-shell-notification-strip.md).

**Clock discipline (Phase 25):** Idle/stale, retention, and run-complete stamps use `IClock` (`SystemClock` → `TimeProvider`). Startup compares the clock to optional `NtpHost` (500ms timeout) or `{DataDirectory}/clock-last-good.json`. Skew above `ClockSkewWarnThresholdMinutes` (default 5) publishes a dismissible Warning on the shell strip with the measured delta and **does not block Run**. Safety Stop / worker kill must not wait on NTP. Appliance time sync: [appliance-linux.md](appliance-linux.md).

**Shell notifications (Phase 17):** MainWindow keeps a reserved-height strip above page content (idle caption **Ready**). Run severity, storage health, suite completion, DUT history one-liners, Home crash recovery, and clock-skew warnings publish into [`ShellNotificationViewModel`](../src/HardwareTest/Features/Shell/ShellNotificationViewModel.cs). Precedence: Critical > Error > Warning > Info across sources; session confirm and operator interaction stay on the Run board (height-capped). Sticky severity is **not** a collapsing Auto row on Run.

**Operator touch density (Phase 18):** Interactive operator controls use a **MinHeight ≥ 40** floor (filter chips, primary/danger/success buttons, step/stage/Results list rows); compact nav Pause/Stop are **48×48**. The Run page uses mutually exclusive **Steps / Details / Chart** tabs (preparation and operator prompts overlay the workspace). Hierarchical plans can keep an optional **Overview** sidebar (hidden on compact boards). Disabled Run / Run Selected show the blocking reason as an inline tip (not ToolTip-only). Double-tap remains an accelerator:

| Surface | Primary (touch) | Accelerator |
| --- | --- | --- |
| Step / stage | Select + **Details** | Double-tap row |
| Results report | Select + **Open report** | Double-click row |
| Live trend | **Chart** workspace | Shell **View chart** when out of band |

Constants: [`OperatorTouchDensity`](../src/HardwareTest/Features/Shell/OperatorTouchDensity.cs). Full kiosk bake remains [deferred](deferred/deferred-appliance-kiosk.md).

Bootstrap is two-stage: stage 1 resolves `DataDirectory` + `LogMinimumLevel` from env/CLI before logging; stage 2 loads `settings.json` then re-applies overlays. See [phase-3-configuration-model.md](platform-phases/phase-3-configuration-model.md).

### Schema versions

Every persisted JSON document carries an integer `schemaVersion`. Bumps are deliberate (see [`SchemaVersions`](../src/HardwareTest.Core/Serialization/SchemaVersions.cs)). Absent/`0` = legacy; greater than current = load read-only (never overwrite). Golden fixtures live under `tests/fixtures/schema/`.

| Document | Current | Changelog |
| --- | --- | --- |
| `AppSettings` (`settings.json`) | 1 | Initial stamped shape (Phase 5). |
| `UiState` (`ui-state.json`) | 1 | Initial stamped shape (Phase 5). |
| `TestRunRecord` (`runs/{id}/run.json`) | 2 | `Attestations` (optional chip/tap presence or PIV-signed sidecar stamps). Identity upgrade from 1. |
| `SuiteRunRecord` (`runs/suites/{id}/suite-run.json`) | 1 | Initial stamped shape (Phase 5). |
| `CrashReport` (`crashes/{id}/crash.json`) | 1 | Initial crash dossier (Phase 6). |

## 11. Custom mixins

Use mixins for product-specific step settings without forking every step type.

1. Create a plugin class library (OpenTAP package reference matching Basic) with:
   - An embed type implementing `IMixin` and `[Display]` properties.
   - An `IMixinBuilder` marked `[MixinBuilder(typeof(ITestStep))]` that returns `MixinMemberData` with `EmbedPropertiesAttribute`.
2. Ship the DLL beside Basic or add its folder to `OpenTapPluginDirectories`.
3. Attach in **OpenTAP Editor** or **OpenTAP TUI** (right-click / mixin menu → Add Mixin). Do not expect an Avalonia “Add Mixin” control.
4. On the Run board (Engineer/Debug), select the step → **Station overrides** shows grouped mixin fields → **Apply & save** persists `PlanParameterOverrides` (TapPlan unchanged).
5. Demo reference: `AnnotationMixin` / `AnnotationMixinBuilder`; sample Identity Check is pre-attached for CI/UI demos. Details: [phase-d-mixins.md](opentap-phases/phase-d-mixins.md).
