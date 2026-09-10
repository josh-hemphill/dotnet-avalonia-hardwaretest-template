# Adapting this template to your product

Keep the layering (`HardwareTest` UI → focused `IOpenTap*` session surfaces → plugins/plans → `HardwareTest.Core`) and replace the sample product pieces below.

Related: [getting-started.md](getting-started.md) (TUI walkthrough), [testing.md](testing.md) (suites), [appliance-linux.md](appliance-linux.md) (sealed publish), [README.md](../README.md) (layering rules).

## Author a locked program

This shell does not edit plans. Author in **OpenTAP Editor / TUI**. First-time TUI steps and each test type: [getting-started.md](getting-started.md). A locked program is `.TapPlan` + `{planId}.program.json` + Presentation on function leaves + TapPackage Dependencies + Typst `reportKinds`. `HardwareTest.PlanValidate --strict` must fail a bad pack **before bake**. Authoring warnings do not block operator Run.

Typed SCPI lives in **InstrumentComponents.OpenTap** ([user guide](https://josh-hemphill.github.io/instrument-components/csharp/opentap/)). HardwareTest Basic is operator/safety chrome and in-repo demos; Mixins is Presentation/Annotation.

### Deliverables

| Piece | Notes |
| --- | --- |
| `.TapPlan` | [`plans/opentap/`](../plans/opentap/) (copied to `Programs/`). Product plans: library instruments/steps. In-repo sample/board-demo stay Basic for CI. |
| `{planId}.program.json` | Session/DUT/Typst only. Copy [`template.program.json`](../plans/opentap/template.program.json); keep `"$schema": "./program.schema.json"`. |
| Dependencies | Product pack: OpenTAP + HardwareTest Basic + Mixins + **InstrumentComponents.OpenTap** + **Expressions** when used. This template `package.xml` keeps three live deps (OpenTAP, Basic, Mixins) so CI can load sample/board-demo. |
| Presentation | Mixin on function leaves: unique `ChannelKey`; `scalar`/`passband` + `LimitLow`/`LimitHigh`/`Threshold`. New mixin default `DisplayRole` is `scalar`. |

### Authoring checklist

1. **Install** the same pack versions the bench bakes (Basic, Mixins, InstrumentComponents.OpenTap). Commands: [Authoring packs](#authoring-packs).
2. **Save** `{planId}.TapPlan` and copy `template.program.json` → `{planId}.program.json`.
3. **One instrument resource per box** from *Instrument Components* (DMM, PSU, FGen, scope, switch, counter, power meter, spectrum analyzer). Extra capabilities are nested views on that resource, not a second slot. Keep **`VisaAddress`** writable so Instruments can rebind.
4. **Shape:** three-level groups (`Setup` / measure / `Cleanup`); unique leaf paths.
5. **Setup:** library *Identity Query* when `requireSerial` — DUT serial is the shell confirm, not a `HardwareDut` resource. Operator pauses: Basic `OperatorPromptStep` / `OperatorInputStep`, never `DialogStep`.
6. **Measure:** library function steps (they already publish `Sample` / `Scalar`). Attach **Presentation** (unique `ChannelKey`; band-first `scalar`/`passband` with limits; `timeseries` only for shape). Identity / Prompt / Input / Safe Shutdown / HangForever / RepeatLoop / TestGroup are exempt.
7. **Cleanup:** library *Safe Shutdown*. `selectionIncludesCleanup` defaults to **true** (Run Selected keeps it). Set **false** only when shutdown is suite-scoped and Run Selected is software-only.
8. **Validate / pack / bake.** Template pack (this repo, Basic sample only):

   ```bash
   HardwareTest.PlanValidate plans/opentap --strict
   cd plans/opentap && tap package create package.xml   # File Path is relative to this directory
   ```

   Product plans that use library steps need that pack on the search path, and declare it on the **product** `package.xml` (do not add a live `PackageDependency` on `InstrumentComponents.OpenTap` to this template):

   ```bash
   HardwareTest.PlanValidate path/to/product-plans --strict --opentap-plugin-dirs path/to/InstrumentComponents.OpenTap
   ```

   Ad-hoc (missing sidecar = warning): `HardwareTest --validate-plan path/to/plan.TapPlan`. Then bake packs onto the appliance and mock-run (`UseMockVisa`).

CLI notes: exit `1` on errors, `0` if only warnings; bare `--validate-plan` prints usage and exits `2` (no UI). `HardwareTest.PlanValidate --opentap-plugin-dirs` trusts those CLI dirs; `HARDWARETEST_OPENTAP_PLUGIN_DIRS` still needs appliance `PluginDirectoryTrust`. `--format json|sarif` is for CI. `tap package create` needs the declared authoring packs already installed.

`plans/opentap/fixtures/` are shape examples, not product plans. Top-level `*.TapPlan` are the pack set. Full **Run** always executes the authored plan. Disabled siblings outside a Run Selected mask may show NotExecuted/Invalidated — that is not “cleanup skipped.”

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

Unknown sidecar keys remain a contract warning (`SIDECAR_UNKNOWN_PROPERTY`). Known station-health keys (`programKind`, `requireStationHealth`, `stationHealthMaxAgeHours`, `stationHealthGate`, `stationHealthProfileId`) are parsed: unknown `programKind` / `stationHealthGate` is an error. Do not put `requireStationHealth` on a `stationHealth` program. DUT sidecar `requireStationHealth` + `warn`|`block` evaluates `{DataDirectory}/station-health/{profileId}.json` before Run (missing/failed/old is stale). `HARDWARETEST_STATION_HEALTH_GATE=off|warn|block` pins or disables the gate. Health programs themselves are never gated. Do not skip the health step via `Enabled`.

Built-in **sample** / **board-demo** / **sweep-demo** stay factories (Basic DMM) so CI does not need the library pack. Disk plans with the same id are not double-listed. Run and Instruments enumerate via `ProgramCatalog`.

| Demo | Operator prompts | Station overrides / Presentation |
| --- | --- | --- |
| **sample** | Confirm → typed fixture install | Acquire `VDC` timeseries, Mean `VDC.mean` scalar |
| **board-demo** | Seat fixture → board sticker | Multi-rail timeseries + mean scalar/passband |
| **sweep-demo** | (none) | Repeat ×3; `sweep.vdc` timeseries |
| **station-health** | (none) | `cal.dc.offset` + `cal.age.hours`; no DUT serial |

### OpenTAP Expressions (optional)

Do not reimplement evaluation in Avalonia. If the plan uses expression steps, install **Expressions** in Editor and on the bench and declare `<PackageDependency Package="Expressions" Version="^1.5.0" />` on the **program** pack (`^1.5.0` is OpenTAP’s example, not a CI pin). Sample/board-demo/fixtures stay numeric. Run Selected: expressions that read disabled siblings may see NotExecuted/Invalidated. Station overrides may edit expression **strings** when they are writable primitives.

## Plugins

1. Add an OpenTAP plugin project (see [`HardwareTest.OpenTap.Plugins.Basic`](../src/HardwareTest.OpenTap.Plugins.Basic/) and mixins in [`HardwareTest.OpenTap.Plugins.Mixins`](../src/HardwareTest.OpenTap.Plugins.Mixins/)). The VISA broker adapter lives in [`HardwareTest.OpenTap.Plugins.Visa`](../src/HardwareTest.OpenTap.Plugins.Visa/) (bench only; not the Editor authoring pack).
2. The host always searches the Basic, Visa, and Mixins plugin assembly directories.
3. Extra search paths: `AppSettings.OpenTapPluginDirectories` and `HARDWARETEST_OPENTAP_PLUGIN_DIRS` (`;` or `Path.PathSeparator` separated).
4. On an appliance, drop third-party plugin DLLs under a writable/plugin folder and list that path in settings (see [appliance-linux.md](appliance-linux.md)).
5. Verify installed packages and plugin dirs in **Settings → OpenTAP packages & plugins** (offline list only; install via `tap package install` / bake).

### Authoring packs

Install the same **HardwareTest Basic**, **HardwareTest Mixins**, and **InstrumentComponents.OpenTap** versions the bench uses:

```bash
dotnet build src/HardwareTest.OpenTap.Plugins.Basic -c Release -r linux-x64 -p:CreateOpenTapPackage=true -p:InstallCreatedOpenTapPackage=false
dotnet build src/HardwareTest.OpenTap.Plugins.Mixins -c Release -r linux-x64 -p:CreateOpenTapPackage=true -p:InstallCreatedOpenTapPackage=false
# Library pack (from the instrument-components repo; NuGet publish is not in this template):
dotnet build path/to/InstrumentComponents.OpenTap.csproj -c Release -p:CreateOpenTapPackage=true
tap package install path/to/HardwareTest\ Basic*.TapPackage
tap package install path/to/HardwareTest\ Mixins*.TapPackage
tap package install path/to/InstrumentComponents.OpenTap*.TapPackage
```

HardwareTest `package.xml` files list only the plugin DLL (no `HardwareTest.Core`). The VISA adapter is bench-only. Default HardwareTest builds keep `CreateOpenTapPackage=false` so CI does not run `tap package create`. The bench injects `IVisaBroker` as the library pack's SCPI session (`OpenTapScpiIo.Provider`); the pack does not open a vendor VISA resource manager. See the [OpenTAP pack guide](https://josh-hemphill.github.io/instrument-components/csharp/opentap/).

## Station bindings (Instruments)

1. Load programs → OpenTAP slots appear on Instruments.
2. Discover resources from either column, then set per-plan slot overrides (`PlanSlotOverrides` in settings):
   - **VISA** — IVI `GlobalResourceManager.Find` (or mock catalog when `UseMockVisa`), with parsed interface hints (USB/TCPIP/…). Toggling `UseMockVisa` in Settings takes effect immediately when no run is active and no VISA sessions are open; refused flips revert the checkbox.
   - **OpenTAP** — `IDeviceDiscovery` plugins for `VisaAddress` (`IOpenTapHostCatalog.ListDiscoveredDeviceAddresses`).
   - **Query *IDN?** — opt-in; opens the selected address briefly to confirm manufacturer/model/serial. Not run on Discover.
3. Slots come from any OpenTAP `Instrument` referenced by step properties. Resource strings prefer **`VisaAddress`**, then `ResourceName`, then `Address` ([`InstrumentResourceAccess`](../src/HardwareTest.OpenTap.Host/InstrumentResourceAccess.cs)). Instruments without a writable resource property show on the page but cannot be rebound from the UI.
4. The Host does **not** call `Instrument.Open`/`Close` — OpenTAP opens instruments during plan execution.

### Third-party SCPI instrument

1. Author a TapPlan that references your SCPI plugin instrument (`VisaAddress` preferred). Validate with `HardwareTest --validate-plan` or `HardwareTest.PlanValidate` before installing it on the bench.
2. Ship the plugin DLL via offline package install or `OpenTapPluginDirectories` (see [appliance-linux.md](appliance-linux.md)). Prefer plugins that implement `IDeviceDiscovery` so **Discover OpenTAP** lists their addresses.
3. On the bench, open **Instruments**, load the program, pick a discovered VISA or OpenTAP resource (or type one), save the slot override.
4. On **Run**, `ApplyStationAndDutAsync` writes the override onto the instrument before execute. The shell does not edit OpenTAP ComponentSettings / bench profiles. Product SCPI maps and typed steps come from **InstrumentComponents.OpenTap**; HardwareTest injects broker-backed SCPI I/O so that pack never calls IVI. Third-party instruments still work if they expose writable `VisaAddress`.

DUT serial is the operator session. Library *Identity Query* does not need `HardwareDut`. Basic `IdentityCheckStep` (in-repo demos) still stamps `HardwareDut` when present.

## Plan contract

`PlanContractValidator` (and the CLIs in [Author a locked program](#author-a-locked-program)) checks this contract. It does not replace a mock Run. Test coverage: [testing.md](testing.md#plan-contract).

| Check | Error unless noted |
| --- | --- |
| Unique leaf paths | Duplicate path |
| Nest depth > 3 | Warning (still runs) |
| Safe Shutdown present | Error when `selectionIncludesCleanup` is true (HardwareTest **or** Instrument Components type name) |
| Writable `VisaAddress` / `ResourceName` / `Address` | Error |
| `DialogStep` / OS dialog | Error — use Basic operator steps |
| Presentation on function leaves | Warning if missing; empty/duplicate `ChannelKey` is an error; band roles without limits warn |
| Sidecar | Missing: warning, or **error** under `--strict`. Invalid JSON / unknown `reportKinds`: error |
| `requireSerial` | Needs *Identity Query* or `IdentityCheckStep`. `HardwareDut` required only on Basic Identity Check |

Repeat/Sweep loops show innermost `iter i/N` on the Run hero; edit bounds in Editor/TUI or station overrides — not in Avalonia.

### Operator session

- Confirm DUT (and operator when `requireOperator` is true) once per session; sticky strip on Run shows last activity and time remaining to soft-warn / Stale.
- Idle uses **last operator activity** (`LastActivityAt`), not confirm time — reviewing Results / reports / navigating between pages refreshes activity.
- Soft-warn (default 80% of idle window) then hard Stale; resolutions are **Same DUT** / **Change Session** (in-panel only). Idle is checked on an interval, not only at Run.
- Canonical idle setting: **`OperatorSessionIdleMinutes`** (default 240). Hours env/CLI (`OperatorSessionIdleHours` / `HARDWARETEST_OPERATOR_SESSION_IDLE_HOURS` / `--session-idle-hours`) remain aliases; minutes wins when both are set.
- Optional station policy **`RequireDutConfirmEveryRun`**: after each terminal run, session goes Stale until Same DUT / Change Session.
- Technician required indicator and Same DUT validation follow program `requireOperator`.
- The shell is a single operator session (one DUT confirm at a time).

## Operator interactions and parameters

Use Avalonia-owned mid-run prompts only:

- **Confirm-only:** `OperatorPromptStep` or `IStepRuntime.RequestOperatorAttention(message)`.
- **Typed input:** `OperatorInputStep` (string + optional number) or a custom step calling `IStepRuntime.RequestInteraction` with `OperatorInteractionField`s.
- The Run board shows an in-panel host (title, message, fields). Continue / Cancel are toolbar actions — never OpenTAP `DialogStep`, WinForms/WPF message boxes, or a second window.

Keep technician prompts separate from engineer overrides:

- **Operator prompts:** mid-run `OperatorInputStep` / `RequestInteraction` — values entered in the orange interaction host and returned to the step. Not saved as station overrides.
- **Station overrides (Engineer/Debug only):** Run board **Station overrides** panel for limits/channels/`Enabled`. **Apply & save** → `TrySetParameter` + `PlanParameterOverrides`. Re-applied on program load / before run. Does not rewrite the TapPlan.
- Prompt-schema properties on interaction steps (Message, field ids/labels) are not station overrides; only `Enabled` is overridable there.
- Prefer `EnumerateParameters` / `TrySetParameter` for new product code. `TrySetAcquireSettings` / `TrySetMeanGteThreshold` remain sample-only adapters.
- OpenTAP **Expressions** strings are station-overridable when they are writable primitives. Do not add a formula UI in Avalonia.

Field editors (`InteractionFieldViewModel`) are shared widgets; override and prompt collections stay separate.

## Presentation and reports

Publish tables `Sample` (Channel, Index, Value, optional LimitLow/LimitHigh/ElapsedMs) and `Scalar` (Name, Value, Unit, optional LimitLow/LimitHigh). Attach **Presentation** (`ChannelKey`, `DisplayRole`, `YUnit`, optional history thresholds) in Editor/TUI. Results lines show `MetricKey [role] value unit`. Run maps `timeseries` → Focus trend when earned, `scalar`/`passband` → Band gauges; Results prefers gauges then charts.

| Recipe | What to publish | Role | Limits | When to also publish timeseries |
| --- | --- | --- | --- | --- |
| Rail / mean in band | `rail.X.mean` (or equivalent) final value | `passband` | LimitLow / LimitHigh = spec | Acquire series optional for Focus/debug |
| Scalar threshold (GTE/LTE) | Mean / computed value | `scalar` or `passband` | One-sided or two-sided limits | Only if shape matters |
| Bump / pulse timing | Derived: `bump.rise.ms`, `bump.width.ms`, `bump.peak` | `scalar` / `passband` | Window bounds as limits | Raw series only for Focus |
| Hi → Low return | Derived: `return.high.at.ms`, `return.low.at.ms`, or excursion | `scalar` / `passband` | Timing + amplitude limits | Raw series only for Focus |
| Envelope / return bounds | Derived: `envelope.error` / `overshoot` / `undershoot` | `passband` | Spec envelope | Raw series for Focus |
| Series stays in band | `Sample` + `LimitLow`/`LimitHigh` + `ElapsedMs`; Scalar `series.inband.pct` | acquire = `timeseries`; summary = `passband` | Every sample in band (`SeriesCompliance=allSamples`) | Event marks when bits/GPIB config change |
| Station health / daily cal | `cal.dc.offset` + `cal.age.hours` | `scalar` (unique ChannelKeys; no single mixin) | Age `LimitHigh` = max hours | Catalog `station-health`; DUT `requireStationHealth` warn\|block. Do not skip via Enabled |

Rules of thumb: write pass criteria in words first; publish **one Scalar per criterion** with limits; keep `ChannelKey` stable; add `timeseries` only when Focus trend is useful. Demo: **Timing / Envelope Demo (Band-first)** (`timing-demo`) plus Sample/Board.

### Typst PDFs

Default embedded templates: `test-report.typ` (status; `status-report.typ` is an alias) + `certification-report.typ` + `lib/sample-chart.typ`.

Override without recompiling:

1. Set `AppSettings.DataDirectory` to your writable root.
2. Place files under `{DataDirectory}/reports/` (and `reports/lib/` for chart lib).
3. Optionally set `AppSettings.ReportTemplateName` (default `test-report.typ`). A file with that name under `{DataDirectory}/reports/` wins over the embedded template.

Compile inputs: `run.json` (camelCase `TestRunRecord`), Typst inputs (`title`, `runId`, `planName`, `dutSerial`, `operatorName`, `attestationKind`, `attestationDetail`, `result`, …), and optional sample-driven charts via `sample-chart.typ`. `EmbedPlotsInReport` toggles chart notes. Certification export can require a detached `{kind}.attestation.json` sidecar (PIV on-card signature, or presence as a site-policy fallback) when `RequireAttestationBeforeExport` is on.

Programs declare `reportKinds` in `{planId}.program.json` (default `["status"]`). Optional `defaultReportKind` chooses which PDF Results opens on double-click (default `status`). Sample and Board demos generate **status** (includes DUT history when available) and **certification** (pass/fail + measurements only). PDFs land as `runs/{runId}/status.pdf` and `certification.pdf`; `ReportPdfPath` points at status for back compat. Results: click a run for detail, double-click for the default report, or Open a specific artifact.

Loop samples stamp `IterationIndex` / `LoopPath` on `StoredSample` for report charts (last value per iteration); live Run plot stays chronological.

### MES / QA file export

Set `AppSettings.ExportOpenTapResults` (Settings → **Export OpenTAP results (CSV)**). During each run the host writes published OpenTAP tables to `{DataDirectory}/runs/{runId}/opentap-results/{TableName}.csv`. Typst PDFs and `run.json` are unchanged. Default is off.

### DUT history and compare

After Pass/Fail, the shell compares channel means on the current run to the last 10 local runs with the same DUT serial + plan ([`DutHistoryService`](../src/HardwareTest.Core/Runs/DutHistoryService.cs)). Metrics group by Presentation `MetricKey` when set (else Channel). Watch/Alert thresholds default to 5%/10%, or per-metric via Presentation `HistoryWatchPercent` / `HistoryAlertPercent` / `HistoryEnabled`. Absent `HistoryEnabled` (legacy records) means **no comparison**, not default thresholds. History detail lives on **Results**; when `ShowDutHistoryOnRun` is on, the one-line operator summary goes to the **shell notification strip**.

Opening a run also shows **Compare with previous**: the latest earlier run with the same DUT serial + plan ([`RunComparisonService`](../src/HardwareTest.Core/Runs/RunComparisonService.cs)). Missing metrics are listed as unavailable. Comparison never blocks Run. Export packages include `diagnostics.txt`; Settings **Copy diagnostics** includes catalog sidecar self-check.

## What stays demo-specific

- Basic plugin steps (`AcquireVoltageStep`, `MeanGteStep`, `OperatorInputStep`, …).
- Engineer/Debug overlays `TrySetAcquireSettings` / `TrySetMeanGteThreshold` (sample step types only; prefer the parameter bridge). Prefer `TryGetStepConditionSummary` for read-only display of unknown steps.
- `LoadSampleProgramAsync` / `LoadBoardDemoProgramAsync` / `LoadTimingDemoProgramAsync` on `IOpenTapSession` — ignore once you only ship disk plans.

Plan-shape fixtures (`LoadPlanShapeAsync`) live on the concrete `OpenTapSession` / `FakeOpenTapSession` for tests, not on `IOpenTapSession`.

## Rename checklist

1. Rename solution/projects/namespaces from `HardwareTest` to your product id.
2. Update OpenTAP `[Display(..., Groups: ["HardwareTest"])]` on plugins.
3. Update CI paths, `dirs.proj`, publish output names, and Typst “Generated by …” strings.
4. Update env var prefix if desired (`HARDWARETEST_*` including `HARDWARETEST_OPENTAP_PLUGIN_DIRS`).
5. Re-run ViewModels, OpenTAP host, and E2E smoke tests with `-r win-x64`.

## Configuration reference

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

Also: `--settings <path>`, `--print-config` (dump effective config + provenance and exit 0), `--validate-plan <path>` (validate and exit; `1` on errors, `0` if only warnings; bare path exits `2` with usage and does not start the UI), `--version` / `-v`. Avalonia-free equivalent: `HardwareTest.PlanValidate <path> [...] [--strict] [--format text|json|sarif] [--opentap-plugin-dirs <dir>]` (explicit plugin dirs are trusted for that process; `--strict` fails a missing sidecar). Debug builds: `--simulate-crash {fatal|recoverable|command}`. Nested lists use `HARDWARETEST_<LIST>__{n}__<PROP>` (e.g. `HARDWARETEST_INSTRUMENTS__0__RESOURCE`).

Bootstrap is two-stage: stage 1 resolves `DataDirectory` + `LogMinimumLevel` from env/CLI before logging; stage 2 loads `settings.json` then re-applies overlays.

Crash dossiers land under `{DataDirectory}/crashes/` (or `CrashDirectory`): `crash.json`, `log-tail.txt`, `config.json`, `session.json`. Unreviewed dossiers and recoverable faults publish to the **shell notification strip** with Export / Open folder / Dismiss. Settings → **Open crashes folder** is hidden when `AllowOsFolderBrowse` is false and Engineer debug is off.

**Export / storage:** Results **Export to…** copies run PDFs + `run.json` + optional `*.attestation.json` (+ optional CSV) to removable media or `ExportDirectory`. Home crash **Export support bundle** uses the same targets (falls back to `{DataDirectory}/exports`). Retention prunes completed `runs/` folders by age/count using the injected clock; free-space warn/critical gates Run and surfaces on the shell strip (Critical is non-dismissible).

**Clock:** Idle/stale, retention, and run-complete stamps use `IClock` (`SystemClock` → `TimeProvider`). Startup compares the clock to optional `NtpHost` (500ms timeout) or `{DataDirectory}/clock-last-good.json`. Skew above `ClockSkewWarnThresholdMinutes` (default 5) publishes a dismissible Warning on the shell strip and **does not block Run**. Safety Stop / worker kill must not wait on NTP. Appliance time sync: [appliance-linux.md](appliance-linux.md).

**Shell notifications:** MainWindow keeps a reserved-height strip above page content (idle caption **Ready**). Run severity, storage health, suite completion, DUT history one-liners, Home crash recovery, and clock-skew warnings publish into [`ShellNotificationViewModel`](../src/HardwareTest/Features/Shell/ShellNotificationViewModel.cs). Precedence: Critical > Error > Warning > Info. Session confirm and operator interaction stay on the Run board.

**Operator touch:** Interactive operator controls use a **MinHeight ≥ 40** floor; compact nav Pause/Stop are **48×48**. The Run page uses mutually exclusive **Steps / Details / Chart** tabs. Hierarchical plans can keep an optional **Overview** sidebar (hidden on compact boards). Disabled Run / Run Selected show the blocking reason as an inline tip. Double-tap remains an accelerator (select + **Details** / **Open report** / **Chart**). Constants: [`OperatorTouchDensity`](../src/HardwareTest/Features/Shell/OperatorTouchDensity.cs). Full kiosk image bake is not in this repo; container rails are in [containers.md](containers.md).

### Schema versions

Every persisted JSON document carries an integer `schemaVersion`. Bumps are deliberate (see [`SchemaVersions`](../src/HardwareTest.Core/Serialization/SchemaVersions.cs)). Absent/`0` = legacy; greater than current = load read-only (never overwrite). Golden fixtures live under `tests/fixtures/schema/`.

| Document | Current | Changelog |
| --- | --- | --- |
| `AppSettings` (`settings.json`) | 1 | Initial stamped shape. |
| `UiState` (`ui-state.json`) | 1 | Initial stamped shape. |
| `TestRunRecord` (`runs/{id}/run.json`) | 3 | `Events`, `Sample.ElapsedMs`, `Sample.ResultSource`. Identity upgrades 1→2→3. |
| `SuiteRunRecord` (`runs/suites/{id}/suite-run.json`) | 1 | Initial stamped shape. |
| `CrashReport` (`crashes/{id}/crash.json`) | 1 | Initial crash dossier. |
| `StationHealthRecord` (`station-health/{profileId}.json`) | 1 | Station-scoped cal / health snapshot. |

## Custom mixins

Use mixins for product-specific step settings without forking every step type.

1. Create a plugin class library (OpenTAP package reference matching Basic) with:
   - An embed type implementing `IMixin` and `[Display]` properties.
   - An `IMixinBuilder` marked `[MixinBuilder(typeof(ITestStep))]` that returns `MixinMemberData` with `EmbedPropertiesAttribute`.
2. Ship the DLL beside Basic or add its folder to `OpenTapPluginDirectories`.
3. Attach in **OpenTAP Editor** or **OpenTAP TUI** (right-click / mixin menu → Add Mixin). The Avalonia shell does not add mixins.
4. On the Run board (Engineer/Debug), select the step → **Station overrides** shows grouped mixin fields → **Apply & save** persists `PlanParameterOverrides` (TapPlan unchanged).
5. Demo reference: `AnnotationMixin` / `AnnotationMixinBuilder`; sample Identity Check is pre-attached for CI/UI demos.
