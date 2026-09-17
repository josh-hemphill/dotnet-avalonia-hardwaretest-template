# Engineer authoring application

Status: **proposal**. The operator shell still does not edit plans. Until this stack lands, author in OpenTAP TUI / Editor: [getting-started.md](getting-started.md).

This document is the execution plan for a **separate workstation app** in this repo. It is not an `IShellApplication` baked into the operator appliance.

## Goal

An engineer can open a test-set planning directory, author **only the metrics they want to measure and the algorithms that run over those measurements** (closed recipes, a MATLAB-flavored formula that compiles to OpenTAP Expressions / existing analyze steps, **or** a discrete SISO transfer function imported as numerator/denominator coefficients). Those metrics appear on the operator Run / Results board (Presentation mixins, gauges, charts, prompts). Formulas and transfer functions can be checked against **operator run exports** (`run.json`) checked into the same test-set repo. The app bootstraps an isolated OpenTAP tree with HardwareTest Basic, Mixins, **InstrumentComponents.OpenTap**, and any other declared packs so TUI/Editor and our UI share the same step catalog. `HardwareTest.PlanValidate --strict` plus a TUI compatibility check gate a **pack/ship** step that compiles that directory into TapPackages (and optional plugin / shell-app artifacts) a product test-set repo can install on the bench. The operator `HardwareTest` exe stays a locked runner. There is no MATLAB Runtime on the bench and no MATLAB Engine in the authoring process. Transfer functions ship as **coefficients + a native .NET IIR step**, not `.m` files.

## Why a separate app

Today the path is: install OpenTAP + TUI, manually build/install our TapPackages, author a three-level TapPlan, attach Presentation by hand, copy a sidecar, validate, `tap package create`. That leaks OpenTAP plumbing (groups, mixins, package.xml, Dialog vs Operator Prompt) onto every new engineer.

Constraints this repo already encodes:

- The operator shell does not edit plans or add mixins ([adapting.md](adapting.md#author-a-locked-program)).
- Operator shell apps are bake-time `IShellApplication` class libraries, not launch-time plugin scans ([adapting.md](adapting.md#operator-shell-applications)).
- Product plans use **InstrumentComponents.OpenTap** typed instruments/steps; in-repo sample/board-demo stay Basic so CI does not need that pack.
- The VISA adapter is bench-only; Editor/TUI authoring packs are Basic + Mixins (+ library pack).

A guest page on the operator shell would put a plan editor on the appliance and fight those rules. A second Avalonia exe on the engineer workstation can still **reuse** the same presentation mapping and widgets so the preview matches the bench.

Vue is the usual preference for greenfield UI; this app is Avalonia so operator `MetricGaugeView` / plot / timing-strip chrome can be shared instead of reimplemented.

## Two-repo shape

```
dotnet-avalonia-hardwaretest-template/     # this repo
  src/HardwareTest/                        # operator appliance (locked plans)
  src/HardwareTest.Authoring.Core/         # Avalonia-free workspace, compile, pack, TUI compat
  src/HardwareTest.Authoring/              # engineer Avalonia exe + headless CLI flags
  src/HardwareTest.OpenTap.Plugins.*/      # Editor/bench packs
  plans/opentap/                           # in-repo demos only

{product}-testset-{station}/               # separate repo per test set (not in this template)
  authoring.json                           # workspace manifest
  plans/*.TapPlan + *.program.json
  recordings/{planId}/**/run.json          # optional operator export goldens
  models/*.tf.json                         # optional MATLAB-exported discrete TFs
  package.xml                              # generated or checked in
  plugins/                                 # optional test-set OpenTAP plugins
  shell-apps/                              # optional IShellApplication projects for that station
```

The authoring app **loads any workspace directory**. This template’s `plans/opentap/` is the built-in example workspace. Product TapPlans and station-specific UI plugins live beside each other in the test-set repo; this app does not clone or host those repos.

Ship output is artifacts for a bake pipeline, not a live load into the operator process:

| Artifact | Goes to |
| --- | --- |
| Program `.TapPackage` (TapPlans + sidecars + `package.xml`) | Bench `tap package install` / `Programs/` |
| Plugin `.TapPackage`s (Basic, Mixins, InstrumentComponents, extra plugins) | Bench OpenTAP tree |
| Optional shell-app binaries | Product `AddShellApplications` bake — still compile-time, never `plugins/` |

## User loop

1. **Open workspace** — `authoring.json` + `plans/`.
2. **Bootstrap** — isolated OpenTAP home; install OpenTAP, TUI, HardwareTest Basic, Mixins, InstrumentComponents.OpenTap (when configured), Expressions when the manifest says so.
3. **Author metrics / algorithms** — not raw OpenTAP step trees. Chrome (Setup / measure / Cleanup groups, Safe Shutdown, unique leaf names, Presentation mixin, sidecar) is generated. Algorithms are a closed recipe, a MATLAB-flavored formula (subset) that lowers to OpenTAP Expressions / Basic analyze steps, **or** a discrete SISO transfer function (num/den/`Ts`) that lowers to `ApplyTransferFunctionStep`.
4. **Preview operator UI** — same DisplayRole → gauge / chart / timing mapping as Run / Results. When the workspace has run recordings, preview, formula eval, and TF apply use those samples instead of synthesized values.
5. **Validate** — existing `PlanContractValidator` (`--strict` before pack).
6. **TUI compat** — round-trip TapPlan XML through a TUI-equipped PluginManager; catalog-diff step types.
7. **Pack** — write/update `package.xml`, `tap package create`, copy declared plugin packs, write `dist/ship-manifest.json`.

Escape hatch: **Open in TUI** launches `tap tui` against the same isolated home so an engineer can drop to the stock editor without a second package set.

## Non-goals (this stack)

- Editing plans inside the operator `HardwareTest` process or on the sealed appliance.
- Loading shell apps from OpenTAP `plugins/` at operator launch.
- Reimplementing OpenTAP Editor (sweep UI, ComponentSettings bench profiles, feed browser).
- Driving the TUI process as a terminal UI (we compare engines + catalogs).
- Putting InstrumentComponents.OpenTap into this template’s live `plans/opentap/package.xml` (CI demos stay Basic).
- Shipping the VISA broker adapter in the authoring OpenTAP home (Editor pack set only).
- MATLAB Runtime / Compiler SDK / Engine on the appliance or in Authoring. No `.m` toolbox, Simulink, MIMO, or arbitrary MATLAB. Engineers may **export coefficient JSON** from MATLAB (documented snippet); Authoring never launches MATLAB.
- Reimplementing OpenTAP Expressions evaluation in Avalonia ([adapting.md](adapting.md#opentap-expressions-optional)). Preview/CI walk our subset AST in Authoring.Core; the packed plan still executes in OpenTAP.
- Lowering transfer functions through OpenTAP Expressions (IIR is a native step).
- Continuous-time `tf(num,den)` without `Ts`. Discretize in MATLAB (`c2d`) before import.

## Architecture

```
HardwareTest.Authoring (Avalonia WinExe)
  → HardwareTest.Authoring.Core     (Avalonia-free)
       → HardwareTest.OpenTap.Host  (PlanContractValidator, ProgramSidecar, mixin attach)
       → HardwareTest.OpenTap.Plugins.Basic / Mixins
       → OpenTAP TestPlan.Load / Save / PluginManager
  → shared presentation widgets (extracted from operator Features/Widgets)

HardwareTest (operator)  ──x──  does not reference Authoring*
```

Headless flags on the same exe (`--pack`, `--bootstrap`, `--compat`) exit before Avalonia, matching `HardwareTest --validate-plan`. Keep `HardwareTest.PlanValidate` as the pack-gate CLI for appliance CI; Authoring CLI is for workspace bootstrap/pack/compat.

### Isolated OpenTAP home

Not the operator publish tree and not a machine-global `tap` install the engineer already uses for other products.

Default: `{workspace}/.authoring/opentap/` (gitignored in product repos) or `--opentap-home`. Bootstrap uses `tap package install` of **file** TapPackages we just built, plus TUI from the OpenTAP feed when network is allowed. CI can pass `--tui-package` / `--instrument-components-package` paths so bootstrap stays offline.

## Workspace contract

`authoring.json` is schemaVersion 1, `additionalProperties: false`. Persist with `AuthoringJsonContext` in Authoring.Core — do not register these types on Core’s `AppJsonContext` (architecture tests only walk Core roots).

Product-workspace example (this template’s golden `authoring.json` **omits** InstrumentComponents.OpenTap so sample/board-demo stay Basic):

```json
{
  "$schema": "./authoring.schema.json",
  "schemaVersion": 1,
  "displayName": "Power Board Test Set",
  "plansDirectory": "plans",
  "package": {
    "name": "PowerBoard Programs",
    "version": "0.1.0",
    "os": "Windows,Linux,MacOS"
  },
  "dependencies": [
    { "package": "OpenTAP", "version": "^9.32.2" },
    { "package": "HardwareTest Basic", "version": "^0.1.0" },
    { "package": "HardwareTest Mixins", "version": "^0.1.0" },
    { "package": "InstrumentComponents.OpenTap", "version": "^0.1.0" }
  ],
  "optionalDependencies": [
    { "package": "Expressions", "version": "^1.5.0", "when": "planUsesExpressions" }
  ],
  "instrumentComponentsPackage": null,
  "pluginProjects": [],
  "shellAppProjects": [],
  "includeTui": true,
  "recordingsDirectory": "recordings"
}
```

`instrumentComponentsPackage` is a path or leave null and resolve `HARDWARETEST_INSTRUMENT_COMPONENTS_PACKAGE`. This template workspace omits that dependency so sample/board-demo still validate without the library pack. Optional `recordingsDirectory` (default `recordings`) is unused until Area 10; Area 1 still owns the key so schemaVersion stays 1.

Directory rules (same as today, plus the manifest):

- `{plansDirectory}/{planId}.TapPlan` + `{planId}.program.json`
- Sidecar remains session/DUT/Typst only ([program.schema.json](../plans/opentap/program.schema.json)); instrument requirements stay TapPackage Dependencies.
- `package.xml` may be generated on pack; if checked in, pack must not drift from the manifest.
- Enumerate `*.TapPlan` with `SearchOption.TopDirectoryOnly` (same as `PlanContractValidator.ExpandTarget`). `plans/opentap/fixtures/` stays out of the workspace and out of the program pack.

## Metric-first IR

Engineers do not start from Test Group / mixin menus. They edit a draft that **compiles** to a TapPlan (and **decompiles** TUI-authored plans so round-trips stay honest).

Area 1 freezes the **file** workspace. Area 3 adds drafts beside it — do not replace `TapPlanPaths` with `Programs`.

```csharp
// Area 1 — files on disk
public sealed record AuthoringWorkspace(
    string Root,
    AuthoringManifest Manifest,
    IReadOnlyList<string> TapPlanPaths);

// Area 3 — compiled/decompiled programs for those paths
public sealed record DraftWorkspace(
    AuthoringWorkspace Files,
    IReadOnlyList<ProgramDraft> Programs);

public sealed record ProgramDraft(
    string PlanId,
    ProgramSidecar Sidecar,
    IReadOnlyList<InstrumentRef> Instruments,
    IReadOnlyList<SetupAction> Setup,
    IReadOnlyList<MeasureNode> Measure,
    CleanupPolicy Cleanup);

public abstract record MeasureNode;
public sealed record MetricNode(MetricDraft Metric) : MeasureNode;
public sealed record RepeatNode(int Count, IReadOnlyList<MeasureNode> Children) : MeasureNode;
public sealed record RawStepNode(string TypeName, string XmlFragment) : MeasureNode;

public sealed record InstrumentRef(
    string SlotName,
    string TypeId,          // "InstrumentComponents.OpenTap.DmmInstrument" or Basic MockDmm
    string VisaAddress);    // writable; Instruments page rebinds on the bench

public abstract record SetupAction;
public sealed record IdentitySetup(string InstrumentSlot) : SetupAction;
public sealed record OperatorPromptSetup(string Name, string Message) : SetupAction;
public sealed record OperatorInputSetup(
    string Name, string Title, string Message,
    string? StringFieldId, string? NumberFieldId) : SetupAction;

public sealed record MetricDraft(
    string Name,
    string ChannelKey,
    string DisplayRole,     // scalar | passband | timeseries | timing
    string YUnit,
    LimitSpec? Limits,
    HistorySpec? History,
    MetricSource Source);

public abstract record MetricSource;
public sealed record MeasureSource(
    string InstrumentSlot,
    string FunctionId,      // catalog id, e.g. "IC.Dmm.MeasureVoltageDc" / "Basic.AcquireVoltage"
    IReadOnlyDictionary<string, string> Settings) : MetricSource;

public sealed record AlgorithmSource(
    string AlgorithmId,     // MeanGte, PublishBandScalar, SeriesInBand, …
    IReadOnlyList<string> InputChannelKeys,
    IReadOnlyDictionary<string, string> Settings) : MetricSource;

/// MATLAB-flavored subset; not MATLAB. Compiles to Expressions or a closed analyze step.
public sealed record ExpressionAlgorithm(
    IReadOnlyList<string> InputChannelKeys,
    string Source) : MetricSource;

/// Discrete SISO LTI. Coefficients from MATLAB export JSON or filter(b,a,x) sugar.
/// Does not compile to Expressions. Execute path is ApplyTransferFunctionStep.
public sealed record TransferFunctionAlgorithm(
    string InputChannelKey,
    IReadOnlyList<double> Numerator,
    IReadOnlyList<double> Denominator,
    double TsSeconds,
    string Method) : MetricSource;   // "filter" | "filtfilt"

public sealed record LimitSpec(double? Low, double? High, double? Threshold);
public sealed record HistorySpec(bool Enabled, double? WatchPercent, double? AlertPercent);
public sealed record CleanupPolicy(bool IncludeSafeShutdown, string InstrumentSlot);
```

Compile rules (must match [getting-started.md](getting-started.md) / plan contract):

- Emit three-level groups: `Setup` / named measure suite / `Cleanup`.
- One instrument resource per box; extra capabilities are nested views, not a second slot.
- Function leaves get Presentation with unique `ChannelKey`. `Save` writes `LimitSpec` onto the step (`LimitLow` / `LimitHigh` / `Threshold`) and `HistorySpec` onto the mixin (`HistoryEnabled` / `HistoryWatchPercent` / `HistoryAlertPercent`). Extend attach beyond today’s `AttachPresentation(channelKey, displayRole, yUnit)` — that helper does not set limits or history, and skipping them inverts `MISSING_LIMITS`.
- Identity / Prompt / Input / Safe Shutdown / Repeat / Test Group stay Presentation-exempt.
- Never emit `DialogStep`. Product identity is library Identity Query when InstrumentComponents is present; Basic Identity Check + `HardwareDut` only for in-repo demos.
- `timeseries` is opt-in for shape; pass criteria live on `scalar` / `passband` with limits.
- Algorithms that need a prior series reference `InputChannelKeys` (sibling measure steps), not hidden global state.
- `ExpressionAlgorithm.Source` is the MATLAB-flavored subset (Area 3 parser). `Save` lowers it to a closed analyze step when it matches a recipe (`mean(x)` + threshold → `MeanGteStep`); otherwise an OpenTAP Expressions step and the program pack gains the Expressions optional dependency. Fail closed if the formula cannot lower. Do not emit a MATLAB plugin or keep `.m` files in the TapPackage.
- `TransferFunctionAlgorithm` is frozen in Area 3 as a record only. Area 11 owns parse/import, IIR, `ApplyTransferFunctionStep`, and Save. Area 3 `FormulaParser` still **rejects** `filter(...)` and `filtfilt(...)` (unknown functions) so Area 7 formula tests stay stable. Do not lower TFs through Expressions.

Decompile: walk `OpenTapStepTree`, read Presentation via `OpenTapPresentation.TryReadMixin`, map known Basic / InstrumentComponents type names through `OpenTapStepKinds`. Unknown or unmodeled steps become `RawStepNode` (preserve type name + inner XML) so TUI-only plugins and Repeat children are not stripped. `RepeatLoopStep` decompiles to `RepeatNode`.

## TUI compatibility

TUI is an OpenTAP package over the same `TestPlan` XML. We do **not** scrape the terminal UI.

```csharp
public sealed record TuiCompatReport(
    IReadOnlyList<CatalogDelta> Catalog,
    IReadOnlyList<RoundTripFinding> RoundTrips);

public sealed record CatalogDelta(
    string TypeName,
    string DisplayName,
    CatalogSide MissingOn);   // AuthoringHome | TuiHome

public sealed record RoundTripFinding(
    string PlanPath,
    string Code,              // TYPE_UNKNOWN, MIXIN_DROPPED, XML_DRIFT, CONTRACT_FAIL
    string Message);

public interface ITuiCompatChecker
{
    TuiCompatReport Compare(AuthoringWorkspace workspace, OpenTapHome authoringHome, OpenTapHome tuiHome);
}

public static class TuiCompatReportExtensions
{
    // Pack/CI fail closed when any of these is true.
    public static bool BlocksPack(this TuiCompatReport report);
}
```

Checks:

1. **Catalog** — `PluginManager` step, instrument, and mixin builder types in the authoring home vs a home that also has the TUI package. Authoring must not offer types TUI cannot load (`CatalogSide.TuiHome` is a pack failure). Types present only in the TUI home are ignored when they live under `OpenTap.TUI*` (the TUI app assembly). Other `TuiHome`-only HardwareTest / InstrumentComponents / BasicSteps types are a catalog warning, not a pack failure.
2. **Round-trip** — Authoring `Save` → `TestPlan.Load` in the TUI home → `Save` → load again in Authoring → structural diff (step types, mixin member names, ChannelKeys, sidecar). Ignore volatile XML noise (formatting) via a normalized tree, not raw string compare. Mixin member names must stay XML-safe (no encoded colons — see existing Mixins fix).
3. **Contract** — `PlanContractValidator.Validate` in both homes with `--strict` semantics for pack.
4. **Optional Open in TUI** — spawn `tap tui` with the isolated `OpenTapHome` as cwd and `tap` on `PATH` pointing at that home (manual; not CI). This repo has no `OPENTAP_PATH` setting.

CI: `deno` task `test:authoring-compat`. Offline when TUI `.TapPackage` is cached; otherwise advisory like Linux E2E. Template plans (Basic only) run in required CI; InstrumentComponents round-trip is required only when that pack path is provided.

## Pack / ship

```text
HardwareTest.Authoring --pack <workspace> --out dist/
  1. Load authoring.json
  2. Bootstrap isolated home if packs missing
  3. PlanContractValidator.Validate(plans, strict: true, trust plugin dirs)
  4. TuiCompatChecker.Compare — fail pack when BlocksPack:
     round-trip TYPE_UNKNOWN / MIXIN_DROPPED / CONTRACT_FAIL, or catalog MissingOn TuiHome
     (authoring type TUI cannot load). XML_DRIFT is a warning.
  5. Render package.xml from manifest + discovered TapPlan/sidecar files
  6. tap package create (cwd = plans dir; File Path relative)
  7. Build/copy pluginProjects TapPackages
  8. dotnet publish shellAppProjects into dist/shell-apps/{id}/ (document bake hook; do not scan at operator launch)
  9. Write dist/ship-manifest.json (package files, versions, OpenTAP ^ pin)
```

`ship-manifest.json` is the handoff a test-set repo CI and the appliance bake both read. It does not install onto a live bench by itself.

## Operator preview

Reuse, do not fork, DisplayRole strings (`timeseries` → Focus chart, `scalar`/`passband` → gauges, `timing` → timing strip). Area 5 moves only `TryMapRole` + role constants (align with Mixins `PresentationDisplayRoles`). Do **not** move `PresentationTileViewModel`, `BuildFromStoredSamples`, or `IsRunGaugeSample` — those take Host `MeasurementSampleEvent` / ReactiveUI VMs. Area 7 preview synthesizes canned `StoredSample`s when no recording is bound. Area 10 binds operator `run.json` datasets and evals `ExpressionAlgorithm` over those series. Area 11 applies `TransferFunctionAlgorithm` on the same series via `TransferFunctionFilter` (ElapsedMs grid). No `TestPlan.Execute` / `TapThread` in the UI process. Optional later: mock-run via `OpenTapWorkerClient`.

## Layering rules (architecture tests)

- `HardwareTest.Authoring.Core` is Avalonia-free (and may reference Host + OpenTAP).
- Operator `HardwareTest` must not reference `HardwareTest.Authoring*`.
- Authoring must not reference `HardwareTest.OpenTap.Worker` in v1 (no execute in the UI process).
- Authoring is not an `IShellApplication`; it has its own window.
- Authoring OpenTAP home does not install the VISA adapter pack.
- Feature files stay under 600 lines; InternalsVisibleTo for Authoring tests as with Host.
- Plugins must not reference Avalonia/ScottPlot or `HardwareTest.Core`. `TransferFunctionFilter` lives in Basic (OpenTAP-only) so Authoring preview and the bench step share one IIR without putting Core in the Editor pack.

Promote `OpenTapMixinAttach` (and any compile helpers) from `internal` to a documented public authoring surface on Host, or move them into Authoring.Core to avoid leaking demo factories. Prefer a small public `IPlanCompiler` in Authoring.Core that calls today’s attach helpers.

---

## Stack graph

```
latest
  ← Area 1  workspace + Authoring.Core skeleton
       ← Area 2  isolated OpenTAP bootstrap
            ← Area 3  metric IR compile / decompile (+ formula AST; TF record only)
                 ← Area 4  pack / ship (Core API)
                      ← Area 5  extract shared presentation map
                           ← Area 6  Authoring Avalonia shell
                                ← Area 7  metric-first UI + MATLAB-flavored formula editor
                                     ← Area 10 run recordings consume / eval / visualize
                                          ← Area 11 discrete TF import + native IIR step
                 ← Area 8  TUI compat checker + CI   (can start after Area 3; pack failure uses it in Area 4)
  docs rewrite is Area 9 on top of 7+8+10+11
```

Area 8 may proceed in parallel with Areas 5–7 once Area 3’s IR and Area 2’s isolated home exist (little shared UI). Area 10 needs Area 3’s `ExpressionAlgorithm` + Area 5’s `TryMapRole` + Area 7’s preview pane; it can start after Area 7 is pushed. Area 11 needs Area 3’s `TransferFunctionAlgorithm` record, Area 7’s metric editor, and Area 10’s series-by-metric binder. Area 4’s pack API accepts an optional `ITuiCompatChecker`; if Area 8 is not merged yet, pack validates contract only. Area 6 owns `src/HardwareTest.Authoring/` (exe + headless flags). Area 4 is Core-only (`WorkspacePacker`).

### Conflict map

| Surface | Areas | Overlap |
| --- | --- | --- |
| `authoring.json` / schema | 1, 4, 9, 10 | Area 1 owns the schema (including unused `recordingsDirectory`); later areas only add optional keys |
| `AuthoringWorkspace` (files) | 1, 2, 4, 8, 10 | Area 1 freezes it; others consume |
| `DraftWorkspace` / `ProgramDraft` / `MeasureNode` / `ExpressionAlgorithm` / `TransferFunctionAlgorithm` | 3, 7, 8, 10, 11 | Area 3 freezes IR records; 11 owns TF parse/Save/IIR |
| Isolated OpenTAP home (`OpenTapHome`) | 2, 4, 8 | Area 2 owns bootstrap; 4/8 consume the home path |
| `package.xml` generation | 4 | only; Expressions dep when a formula did not lower to a Basic step. TF uses Basic — no extra pack dep |
| `src/HardwareTest.Authoring/` exe | 6 | only; Area 4 tests pack via Core API |
| `PresentationRoleMap.TryMapRole` | 5, 7, 10, 11 | Area 5 moves the map; 7/10/11 bind preview |
| `FormulaParser` | 3, 11 | Area 3: subset without `filter` / `filtfilt`. Area 11 adds `filter(b,a,x)` / `filtfilt(b,a,x)` → `TransferFunctionAlgorithm` |
| `HardwareTest.OpenTap.Plugins.Basic` IIR/step | 11 | only |
| Operator `HardwareTest.csproj` | 5 | usings only — no Authoring reference |
| `dirs.proj` / `HardwareTest.slnx` | 1, 6 | Area 1 adds Core + tests; Area 6 adds the exe |
| `tools/ci` TASKS | 6, 8, 10 | Area 6 may add `pack:template`; Area 8 `test:authoring-compat`; Area 10 `test:authoring-recordings`. Area 11 adds host tests, not a new TASK unless goldens need one. Never two TASKS rewrites in the same PR. |
| `docs/getting-started.md` | 9 | only; earlier areas may add a one-line pointer |

---

### Area 1: Workspace contract + Authoring.Core skeleton

- Goal: Load/save a planning directory against a versioned `authoring.json`; fail closed on unknown schemaVersion; architecture gates exist so later UI cannot leak into Core.
- Depends on: nothing (base `latest`)
- Out of scope: OpenTAP install, compile, UI, pack, TUI
- Likely files / crates: `src/HardwareTest.Authoring.Core/`, `plans/opentap/authoring.json` + `authoring.schema.json`, `tests/HardwareTest.Authoring.Tests/`, `dirs.proj`, `HardwareTest.slnx`, `ArchitectureRulesTests.cs`
- Public surface:

```csharp
public sealed class AuthoringManifest { /* schemaVersion, displayName, plansDirectory, package, dependencies, … */ }

public static class AuthoringWorkspaceLoader
{
    public static AuthoringWorkspace Load(string root);
    public static void SaveManifest(string root, AuthoringManifest manifest);
}

public sealed record AuthoringWorkspace(string Root, AuthoringManifest Manifest, IReadOnlyList<string> TapPlanPaths);
```

- Pseudo-code:
  - Discover `authoring.json` at `root` (or `--manifest`).
  - Source-gen JSON (`AuthoringJsonContext`), camelCase, `schemaVersion` required; `> current` → read-only + error on Save; missing file → error (do not invent a product pack).
  - Resolve `plansDirectory` relative to root; enumerate `*.TapPlan` with `SearchOption.TopDirectoryOnly` (fixtures stay out).
  - Golden: this repo’s `plans/opentap/` with a template `authoring.json` that lists OpenTAP + Basic + Mixins only (no InstrumentComponents). `recordingsDirectory` may be omitted (default `recordings`); Area 1 does not require the folder to exist.
- Tests: load template workspace; reject unknown properties / future schemaVersion write; architecture: Core has no Avalonia; operator exe has no Authoring reference (may be vacuous until Area 6 — still add the rule).
- Risks: `dirs.proj` glob already includes `src/**/*.csproj`, so a new project is built in CI immediately — keep Core compiling without tap CLI.
- Conflicts with: Area 4 (manifest fields for pack). Do not add pack fields beyond the schema above.

### Area 2: Isolated OpenTAP bootstrap

- Goal: Given a workspace, produce an OpenTAP home with the declared TapPackages installed (Basic, Mixins, optional InstrumentComponents, optional TUI) without touching the operator publish tree.
- Depends on: Area 1
- Out of scope: compiling metrics, UI, `tap package create` of the **program** pack
- Likely files: `Authoring.Core/OpenTapHomeBootstrapper.cs`, plugin csproj pack targets already used in [adapting.md](adapting.md#authoring-packs)
- Public surface:

```csharp
public sealed record OpenTapHome(string Root);

public interface IOpenTapHomeBootstrapper
{
    OpenTapHome Bootstrap(AuthoringWorkspace workspace, BootstrapOptions options);
}

public sealed class BootstrapOptions
{
    public string? HomeDirectory { get; init; }
    public string? InstrumentComponentsPackagePath { get; init; }
    public string? TuiPackagePath { get; init; }
    public bool Offline { get; init; }
}
```

- Pseudo-code:
  - Build Basic + Mixins with `CreateOpenTapPackage=true` (same commands as docs) into a cache dir, or consume already-built artifacts.
  - `tap package install <file>` into `HomeDirectory` (create if needed). Offline: require file paths; fail with a named error if InstrumentComponents is in `dependencies` but no path/env.
  - Record installed names/versions via existing `OpenTapPackageCatalog` scanning `package.xml`.
  - Do not register `IVisaBroker` / Visa plugin in this home. Authoring PluginManager search dirs must not include the bench Visa assembly even though Host currently project-references it — tests assert Visa types are absent from the authoring catalog, not only from `tap package` listings.
- Tests: temp home; after bootstrap, catalog contains HardwareTest Basic + Mixins; Visa adapter absent; missing IC dependency errors when declared; template workspace succeeds without IC.
- Risks: `tap` not on PATH in CI — wrap via OpenTAP’s bundled CLI next to `OpenTap.dll`, or skip network TUI install when `Offline`.
- Conflicts with: Area 8 (same home). Area 2 owns create/install.

### Area 3: Metric IR compile / decompile

- Goal: Round-trip `ProgramDraft` ↔ `.TapPlan` + sidecar using Host factories/mixin attach; compiled plans pass `PlanContractValidator` for the sample metric recipes; parse/lower `ExpressionAlgorithm`.
- Depends on: Area 1 (sidecar/workspace paths). Can run without Area 2 if PluginManager search dirs include in-tree plugin outputs (today’s host tests).
- Out of scope: UI, pack, TUI catalog, InstrumentComponents-only functions in required CI, MATLAB Runtime, formula editor chrome, IIR execute (`ApplyTransferFunctionStep` is Area 11)
- Likely files: `Authoring.Core/PlanCompiler.cs`, `MetricDraft.cs`, `FormulaAst.cs`, `FormulaParser.cs`, `FormulaLowerer.cs`, promote `OpenTapMixinAttach`; tests using Basic `AcquireVoltage` / `MeanGte` / `PublishBandScalar`
- Public surface:

```csharp
public interface IPlanCompiler
{
    void Save(ProgramDraft draft, string tapPlanPath);   // writes TapPlan + sidecar
    ProgramDraft Load(string tapPlanPath);               // decompile + sidecar
    DraftWorkspace LoadAll(AuthoringWorkspace workspace);
}

public static class PresentationAttach
{
    // Limits go on the step; history goes on the mixin. Do not call the 3-arg demo helper alone.
    public static void Apply(ITestStep step, MetricDraft metric);
}

public sealed record FormulaAst(/* identifiers, ops, calls */);

public static class FormulaParser
{
    public static FormulaAst Parse(string source);          // fail closed on unknown syntax
}

public static class FormulaLowerer
{
    public static MetricSource Lower(ExpressionAlgorithm expr); // AlgorithmSource or keep Expression for Expressions step
    // Area 11 adds: Lower(FormulaAst) may return TransferFunctionAlgorithm for FilterCall.
}

public static class FormulaEvaluator
{
    // Preview/CI only. Same AST the lowerer saw. No Avalonia, no MATLAB.
    public static double Evaluate(FormulaAst ast, IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> series);
}
```

- Pseudo-code:
  - `Save`: search plugins (Authoring home or in-tree Basic+Mixins, **not** Visa); build groups; create instruments; emit setup; walk `Measure` (`MetricNode` / `RepeatNode` / `RawStepNode`); for `ExpressionAlgorithm`, parse + lower (closed step or Expressions); `PresentationAttach.Apply` (ChannelKey, DisplayRole, YUnit, HistorySpec on mixin; LimitSpec on LimitLow/LimitHigh/Threshold); `plan.Save`; write sidecar.
  - `Load`: `TestPlan.Load`; classify with `OpenTapStepKinds`; unknown → `RawStepNode`; Repeat → `RepeatNode`; Expressions steps whose original subset source is in Settings round-trip as `ExpressionAlgorithm`.
  - Recipe coverage: timeseries acquire + scalar mean **with limits**; passband with limits; operator prompt/input; safe shutdown; `requireSerial` identity; one Repeat wrapping a metric; one formula `mean(VDC)` that lowers to `MeanGteStep`.
  - `TransferFunctionAlgorithm` on a draft: `Save` returns a named error (`TF_STEP_UNAVAILABLE`) until Area 11. Do not drop the node or emit Expressions. `filter(...)` and `filtfilt(...)` parse fail as unknown functions (same as `fft`).
- Tests: compile sample-equivalent draft → validator OK (no `MISSING_LIMITS` on scalar/passband); decompile `plans/opentap/sample.TapPlan` → ChannelKeys `VDC` / `VDC.mean` plus Repeat/Raw if present; compile → decompile preserves ChannelKey/DisplayRole/**limits/history**; Dialog never emitted; duplicate ChannelKey fails before save; Visa step types absent; `mean(VDC)` lowers to MeanGte; unknown functions `fft(VDC)`, `filter(VDC)`, and `filtfilt(VDC)` fail parse; `FormulaEvaluator` on a two-point series matches `mean`; Save of `TransferFunctionAlgorithm` fails `TF_STEP_UNAVAILABLE`.
- Risks: OpenTAP mixin XML vs flattened EmbedProperties — use the same attach path as `SampleProgramFactory`.
- Conflicts with: Area 7 (UI binds these records). Freeze names here.

### Area 4: Pack / ship (Core API)

- Goal: `WorkspacePacker.Pack` validates, writes `package.xml`, creates the program TapPackage, copies declared plugin packs, writes `dist/ship-manifest.json`. No exe in this area.
- Depends on: Areas 1–3 (validate compiled plans). Optional `ITuiCompatChecker` until Area 8.
- Out of scope: Avalonia, `src/HardwareTest.Authoring/`, appliance bake, publishing to a feed, Deno TASKS catalog
- Likely files: `Authoring.Core/WorkspacePacker.cs`; tests call the API. Architecture: PlanValidate stays Avalonia-free and remains the operator pack-gate.
- Public surface:

```csharp
public sealed class PackOptions
{
    public OpenTapHome? Home { get; init; }
    public ITuiCompatChecker? Compat { get; init; }  // null until Area 8 — contract-only pack
    public bool Offline { get; init; }
}

public sealed record ShipManifest(string PackageName, string Version, IReadOnlyList<string> Files);

public static class WorkspacePacker
{
    public static ShipManifest Pack(AuthoringWorkspace workspace, string outputDirectory, PackOptions options);
}
```

- Pseudo-code:
  - Strict `PlanContractValidator` on `workspace.TapPlanPaths` (top-level only).
  - If `Compat` is set, fail when `report.BlocksPack()` (see TUI section).
  - Render `package.xml`. **Template pack** (manifest `package.name` == `HardwareTest Template Program`) Files stay the current set: `sample.TapPlan`, `sample.program.json`, `template.program.json`, `program.schema.json` — not every top-level TapPlan (`board-demo` stays a CI factory, not this pack). Product workspaces default to all `TapPlanPaths` + matching sidecars. Do not add a `package.files` schema key in Area 1.
  - Dependencies from manifest (still **no** live IC dep on the template pack).
  - Invoke `tap package create` with cwd = plans directory using Area 2 `OpenTapHome`.
  - Copy extra plugin TapPackages listed in manifest.
  - `shellAppProjects`: `dotnet publish -o dist/shell-apps/{id}` only; ship-manifest notes they are bake-time.
- Tests: pack template workspace → TapPackage exists; listed files match architecture test; deps OpenTAP + Basic + Mixins only; does not include `board-demo.TapPlan`; Core test has no Avalonia reference.
- Risks: `tap package create` requires packs already installed — use Area 2 home.
- Conflicts with: Area 6 (exe `--pack` calls this API). Do not add `HardwareTest.Authoring.csproj` here.

### Area 5: Extract shared presentation map

- Goal: Operator and Authoring share one Avalonia-free DisplayRole → tile-kind function. Operator Run/Results behavior unchanged.
- Depends on: **Recommended base: Area 4** so Core/slnx churn from Areas 1–4 is done. No Authoring UI.
- Out of scope: Authoring UI, new roles, moving ViewModels
- Likely files: move `TryMapRole` + role constants next to Mixins `PresentationDisplayRoles` (or Host); operator `PresentationRoleMap` becomes a thin wrapper for `IsRunGaugeSample` / `BuildFromStoredSamples` which stay in the operator assembly.
- Public surface: `TryMapRole(string? displayRole)` + `timeseries` / `scalar` / `passband` / `timing` constants. Namespace may change — update operator wrappers in this PR.
- Pseudo-code: move map; keep `PresentationTileViewModel` and sample-ingest helpers in operator Features; no behavior change.
- Tests: existing ViewModel presentation tests; architecture: plugins still have no Avalonia/ScottPlot.
- Risks: do not pull OpenTAP or ReactiveUI into Core.
- Conflicts with: Area 7. Finish the map move before preview UI.

### Area 6: Authoring Avalonia shell

- Goal: Create `src/HardwareTest.Authoring/` WinExe that opens a workspace, lists programs, shows a **read-only** decompile tree + **sidecar-only** editor, contract findings, Bootstrap, Validate, Pack. No metric editor yet.
- Depends on: Areas 1–4
- Out of scope: metric-first editor, operator preview widgets, execute, rewriting TapPlan XML
- Likely files: `src/HardwareTest.Authoring/` (App, MainWindow, WorkspaceViewModel, `Program.cs` headless parse), `HardwareTest.Authoring.csproj` Avalonia 12 same as operator; optional Deno `pack:template` that runs the exe `--pack` (update CI TASKS in this PR if added)
- Public surface: `dotnet run --project src/HardwareTest.Authoring -r win-x64 -- <workspace>`; flags `--pack` / `--bootstrap` / `--validate` / `--compat` exit 0/1/2 like PlanValidate (usage 2) and **do not start Avalonia**. `--eval-formulas` is parsed here as a stub (exit 2 / “not implemented”) until Area 10.
- Pseudo-code:
  - Composition separate from operator `App/Composition.cs`.
  - Pages: Workspace | Program (read-only tree from `IPlanCompiler.Load` + sidecar form bound to `ProgramSidecar`) | Findings | Ship.
  - Save in this area writes `{id}.program.json` only. TapPlan Save waits for Area 7.
  - Single window; no WinForms dialogs; folder pick may use Avalonia storage provider (engineer workstation).
- Tests: ViewModel: load template workspace → sample program listed; sidecar save round-trips; validate surfaces `PlanContractFinding`s; `--pack` hits `WorkspacePacker` without loading Avalonia (process-inspect or a dedicated headless test); architecture: operator does not reference Authoring; Authoring does not reference Worker; file size cap.
- Risks: OpenTAP PluginManager process-global — do not run Authoring E2E in parallel with host tests (`BuildInParallel=false` already). Prefer ViewModels + Core tests in v1; Authoring E2E advisory.
- Conflicts with: Area 7 (same ViewModels — 6 stays read-only tree / sidecar-only Save).

### Area 7: Metric-first UI + MATLAB-flavored formula editor

- Goal: Engineer adds a metric (channel key, role, unit, limits, measure vs algorithm) without touching mixin menus; **formula editor** for `ExpressionAlgorithm` using MATLAB-flavored subset syntax; compiler writes Presentation; preview pane shows gauge/chart/timing using the shared map and canned samples (recordings come in Area 10).
- Depends on: Areas 3, 5, 6
- Out of scope: full OpenTAP property grid, live instrument execute, MATLAB Engine/Runtime, `.m` import, loading `run.json` (Area 10), transfer-function coefficient UI / `filter(b,a,x)` / `filtfilt(b,a,x)` (Area 11)
- Likely files: `MetricEditorViewModel`, `FormulaEditorViewModel`, preview using `MetricGaugeView` / plot (shared widget project or project-reference operator widgets **only if** extracted; do not reference `HardwareTest` exe)
- Public surface: `DraftWorkspace` editing; `IPlanCompiler.Save` on apply; recipe picker matching getting-started test types; formula box bound to `ExpressionAlgorithm.Source` with parse errors inline
- Pseudo-code:
  - Empty workspace: wizard “what do you want to measure?” → `Measure` list of `MetricNode`s.
  - Adding a scalar metric auto-attaches Presentation `DisplayRole=scalar` and requires `LimitSpec`.
  - Algorithm picker: closed recipes **or** “Formula…” which creates `ExpressionAlgorithm` with `InputChannelKeys` chosen from existing measure ChannelKeys. No “Transfer function…” row yet (Area 11).
  - On each keystroke (debounced): `FormulaParser.Parse`; show diagnostics; do not Save invalid AST.
  - Repeat uses `RepeatNode`; unknown TUI steps stay `RawStepNode` (advanced inspector, not stripped).
  - Preview without recordings: synthesize `StoredSample`s from draft limits/role; if the metric is a formula, `FormulaEvaluator` over the canned series for the output scalar; `TryMapRole` → gauge vs chart vs timing (not Execute).
- Tests: adding `VDC.mean` scalar + MeanGte algorithm → saved plan validator OK + decompile ChannelKey; formula `mean(VDC)` parse-ok, `fft(VDC)` parse-fail; preview tile kind is Scalar; Dialog not in palette; Instruments slot VisaAddress remains writable.
- Risks: widget extract from operator exe may need a `HardwareTest.Widgets` project — do that here only if Area 5 left views behind. Formula preview numbers may drift from OpenTAP Expressions on the bench — Area 3 goldens lock the subset; Area 10 adds a recording golden.
- Conflicts with: operator widget namespaces. Prefer `HardwareTest.Widgets` referenced by both exes. Area 10 replaces canned preview when a dataset is selected.

### Area 8: TUI compatibility checker + CI

- Goal: Automated catalog + round-trip report; Deno task; template workspace required-green without IC; fail pack on unknown types / dropped mixins.
- Depends on: Areas 2–3 (home + IR). Pack (Area 4) should call this when present.
- Out of scope: launching TUI curses UI in CI
- Likely files: `Authoring.Core/TuiCompatChecker.cs`, `tools/ci/main.ts` task `test:authoring-compat`, workflow step
- Public surface: `ITuiCompatChecker.Compare`; `TuiCompatReport.BlocksPack()`; Area 6 `--compat` flag (Area 8 does not create a second exe)
- Pseudo-code:
  - Compare `OpenTapHome` from Area 2 (authoring packs) vs the same home after TUI install, or a second home.
  - Normalized tree diff after Load/Save.
  - `BlocksPack` is true on round-trip TYPE_UNKNOWN / MIXIN_DROPPED / CONTRACT_FAIL **or** catalog `MissingOn: TuiHome` (authoring type TUI cannot load). Ignore `OpenTap.TUI*`. XML_DRIFT warns only.
- Tests: sample plan round-trip findings empty; a plan with a fake unknown type name fails `TYPE_UNKNOWN`; mixin ChannelKey survives save in TUI home; authoring-only type fails pack via `BlocksPack`.
- Risks: TUI package feed in CI — cache a TapPackage under `tests/fixtures/opentap/packages/` if license allows, else advisory.
- Conflicts with: `TASKS` catalog assertion in `.github/workflows/ci.yml` (must update expected list in the same PR).

### Area 9: Docs rewrite

- Goal: Getting-started primary path is the authoring app; TUI is escape hatch + compatibility; adapting.md pack section points at `--pack`; formula subset, recordings checkout, and TF coefficient import documented.
- Depends on: Areas 6–8, 10, and 11 (commands must exist)
- Out of scope: appliance kiosk bake
- Likely files: `docs/getting-started.md`, `docs/adapting.md`, `README.md`, `docs/testing.md`
- Public surface: documented commands only
- Pseudo-code: replace “Install OpenTAP and TUI” as step 1 with “Run HardwareTest.Authoring on the template workspace”; keep TUI install as optional § escape hatch; document formula subset, `recordings/` goldens, and MATLAB `tf` → coefficient JSON → import.
- Tests: none beyond link targets; architecture messages that cite adapting.md still match.
- Conflicts with: none if last.

### Area 10: Run recordings — consume, eval, visualize

- Goal: Load operator run exports into the test-set workspace, bind them to a plan by `planId`, evaluate `ExpressionAlgorithm`s against real series, and preview with the same DisplayRole map as the operator board. No `TestPlan.Execute`.
- Depends on: Areas 3, 5, 7 (IR + map + preview pane). Load `run.json` via Core (`TestRunRecord` / schema gate) — prefer a Core project reference, not Host, so dataset ingest stays OpenTAP-free.
- Out of scope: MATLAB, scraping Typst PDFs, writing back to the appliance `DataDirectory`, PII-preserving MES feeds, live Worker execute
- Likely files: `Authoring.Core/RunDatasetCatalog.cs`, `Authoring.Core/RunDatasetBinder.cs`, Area 6/7 ViewModels for dataset picker; optional Deno `test:authoring-recordings`
- Public surface:

```csharp
public sealed record RunDataset(
    string Path,
    TestRunRecord Run);   // schema gate via Core; read-only if future schemaVersion

public static class RunDatasetCatalog
{
    public static IReadOnlyList<RunDataset> List(AuthoringWorkspace workspace);
    public static RunDataset Load(string runJsonPath);
}

public static class RunDatasetBinder
{
    // planId must match ProgramDraft.PlanId (filename). Extra MetricKeys in the run are kept; missing InputChannelKeys fail eval.
    public static IReadOnlyDictionary<string, IReadOnlyList<StoredSample>> SeriesByMetric(TestRunRecord run);
}

public static class FormulaDatasetEval
{
    public static IReadOnlyList<StoredSample> EvaluateProgram(ProgramDraft draft, TestRunRecord run);
}
```

- Pseudo-code:
  - Discover `{recordingsDirectory}/**/run.json` (and a single `run.json` dropped in that folder). Also accept a Results **Export to…** folder that contains `run.json` at the root.
  - Load through Core JSON context + schema gate (legacy v0/v1 upgrade, future version read-only).
  - UI: dataset list filtered to current `planId`; show DUT serial only if present (do not require it for eval). Selecting a dataset drives the Area 7 preview tiles from real samples; formulas re-eval via `FormulaEvaluator`. `TransferFunctionAlgorithm` nodes are skipped with a visible “needs Area 11 filter” until that area ships (do not silently treat them as identity).
  - Headless: `HardwareTest.Authoring --eval-formulas <workspace>` (Area 6 flag, implemented here) walks goldens, fails on parse/eval/`MISSING_LIMITS` against recorded limits. TF eval is Area 11 (`--eval-formulas` then includes IIR).
  - Canonical ingest is **`run.json`** (`StoredSample` already has MetricKey, DisplayRole, ElapsedMs, limits). Optional OpenTAP CSVs under `opentap-results/` are **not** required for eval; if present, a later importer may join `Sample`/`Scalar` tables. Do not treat Typst PDFs or host `*.progress.json` cassettes as the product dataset format (cassettes stay a test-only UI VCR).
- Tests: load `tests/fixtures/schema/run-v1.json` (or a dedicated authoring golden copied from sample-pass) → series key `VDC`; `mean(VDC)` eval matches; missing channel fails; future schemaVersion does not overwrite; DUT serial is not required to list the dataset; template workspace with empty `recordings/` is OK.
- Risks: `run.json` today has `planId` / `planName` / `AppCommitSha` but **no test-set git SHA or TapPackage version** — binding is planId-only until a later run-record field. DUT serials in git are PII — goldens should use fixtures (`DUT-RECORD`) not production exports. CSV-only exports without `run.json` are out of scope for v1.
- Conflicts with: Area 7 preview source (canned vs dataset). Area 9 docs. TASKS catalog — add `test:authoring-recordings` only in this PR.

### Area 11: Discrete transfer functions — import, native IIR, preview

- Goal: Engineers design/identify a SISO LTI model in MATLAB against `run.json` series, export **coefficients**, import them into Authoring, preview/eval on the same recording, and compile to a native OpenTAP analyze step. No MATLAB Runtime, no Expressions lowering, no `.m` in the TapPackage.
- Depends on: Areas 3 (frozen `TransferFunctionAlgorithm`), 7 (metric editor pane), 10 (`SeriesByMetric` + recordings)
- Out of scope: MATLAB Engine, c2d in C#, MIMO, Simulink, MATLAB Coder plugins, parsing `.m` files, non-uniform resample, live mid-acquire `filtfilt`
- Likely files: `src/HardwareTest.OpenTap.Plugins.Basic/TransferFunctionFilter.cs`, `TransferFunctionGrid.cs`, `ApplyTransferFunctionStep.cs`; `Authoring.Core/TfModelJson.cs`, `TfModelImporter.cs`, `TransferFunctionTimeBase.cs`; Host `OpenTapStepKinds` (decompile `ApplyTransferFunctionStep`); formula parser extension; Area 7 `TransferFunctionEditorViewModel`; goldens under `tests/fixtures/authoring/tf/`
- Public surface:

```csharp
// Basic — no Core types, no Math.NET, no Avalonia.
public static class TransferFunctionFilter
{
    // Direct Form II transposed. a[0] must be 1 after normalize.
    public static double[] Filter(IReadOnlyList<double> b, IReadOnlyList<double> a, IReadOnlyList<double> x);
    public static double[] FiltFilt(IReadOnlyList<double> b, IReadOnlyList<double> a, IReadOnlyList<double> x);
}

public static class TransferFunctionGrid
{
    public const double DefaultTsRelativeEpsilon = 1e-6;
    public const double DefaultTsEpsilonFloorSeconds = 1e-9;

    // Fail closed: empty, missing/NaN elapsed (including 1:1 NaN from TimeBase), irregular dt,
    // |medianDt/1000 - tsSeconds| > epsilon. Never drop rows to “fix” the grid.
    // Null epsilon → max(DefaultTsEpsilonFloorSeconds, DefaultTsRelativeEpsilon * tsSeconds).
    // Preview and ApplyTransferFunctionStep must call with the same default.
    public static void RequireUniform(
        IReadOnlyList<double> elapsedMs, double tsSeconds, double? epsilon = null);
}

public sealed class ApplyTransferFunctionStep : RuntimeAwareTestStep
{
    public string InputChannel { get; set; } = string.Empty;
    public double[] Numerator { get; set; } = [1];
    public double[] Denominator { get; set; } = [1];
    public double TsSeconds { get; set; }
    public string Method { get; set; } = "filter"; // filter | filtfilt
    // Reads sibling Sample rows (Channel, Value, ElapsedMs). Calls RequireUniform then Filter/FiltFilt.
    // Missing ElapsedMs / irregular dt / Ts mismatch → Verdict.Fail (same rules as preview). Never index-order fallback.
}

// Authoring.Core — may use StoredSample. Basic must not.
public sealed record TfImport(
    TransferFunctionAlgorithm Algorithm,
    string OutputChannelKey);

public static class TfModelImporter
{
    public static TfImport Load(string path);
    public static void Save(string path, TfImport model);
}

public static class TransferFunctionTimeBase
{
    // Same length as series. StoredSample.ElapsedMs is double?; null → NaN.
    // Never omit rows (that is index-order). RequireUniform then fails on NaN.
    public static IReadOnlyList<double> ElapsedMs(IReadOnlyList<StoredSample> series);
    public static IReadOnlyList<double> Values(IReadOnlyList<StoredSample> series);
}
```

- Pseudo-code:
  - **Import:** file picker or drop `models/*.tf.json` (`TfModelJson`: schemaVersion 1, `additionalProperties: false`, input/output keys, tsSeconds, numerator, denominator, method, timeBase=`elapsedMs`, initialConditions=`zero`). Fail closed: `TsSeconds <= 0`, empty numerator or denominator, `InitialConditions != zero`, `TimeBase != elapsedMs` (`index` / `timestamp` included), unknown method (`Filter` ≠ `filter`; method is lowercase `filter` | `filtfilt`), schemaVersion > 1 (**fail Load/apply** — do not treat v2 coefficients as v1). Normalize by dividing all `b`/`a` by `a[0]`. If `a[0] == 0` (or den empty after normalize) fail `TF_DEN_LEADING_ZERO` — do not emit NaN IIR. `Load` returns `TfImport` so `OutputChannelKey` becomes `MetricDraft.ChannelKey`.
  - **Parser boundary:** Area 11 **extends** Area 3 `FormulaParser` / `FormulaLowerer` (same types). New AST node `FilterCall(b, a, channel, method)`. Area 3 still treats `filter` / `filtfilt` as unknown; Area 11 registers the call. `Lower(FilterCall)` → `TransferFunctionAlgorithm`, never Expressions. Nested `filter` / `filtfilt` (e.g. `mean(filter(...))`, `filter(...) + 1`) **fail parse or Lower** — do not fall through to Expressions. `TsSeconds` from bound series median dt, or a required editor field if no recording. Coefficient vectors are numeric literals only. Channel argument is an identifier from `InputChannelKeys`, not a nested expression.
  - **Save:** emit `ApplyTransferFunctionStep` after the measure leaf that publishes `InputChannelKey`. Shape-only `AcquireVoltageStep` today publishes `Channel, Index, Value` and omits `ElapsedMs` unless `LimitLow` / `LimitHigh` / series compliance are set. Save **always publishes the Sample `ElapsedMs` column** on that sibling (`IntervalMs * index`) without adding limits or enabling compliance — turning limits on just to get a clock would invert shape-only timeseries / `MISSING_LIMITS`. If the step type cannot publish `ElapsedMs`, fail `TF_MISSING_ELAPSED`. Presentation on the TF step uses `MetricDraft.ChannelKey`. `filtfilt` is a sibling analyze (whole series), never nested inside acquire. Still never `DialogStep`. `OpenTapStepKinds` recognizes `ApplyTransferFunctionStep` for decompile (function leaf, not Presentation-exempt).
  - **Decompile:** `ApplyTransferFunctionStep` → `TransferFunctionAlgorithm` (not `RawStepNode`).
  - **Preview/eval:** Authoring.Core pulls Value/ElapsedMs from `StoredSample`, then `TransferFunctionGrid.RequireUniform` + `TransferFunctionFilter` (same as the step). Publish synthetic samples with the output ChannelKey + same ElapsedMs. `--eval-formulas` includes TF goldens.
  - **Bench Run:** `ApplyTransferFunctionStep` reads sibling Sample `ElapsedMs` and applies the **same** `RequireUniform` then Filter/FiltFilt. Fail verdict on grid errors — no index-order fallback.
  - **MATLAB snippet** (docs only, not a shipped toolbox): load `run.json` samples → `t`, `u` → `sys = tf(b, a, Ts)` or `tfest` → `c2d` if needed → write `TfModelJson`. Golden: MATLAB `filter(b,a,u)` vector checked in beside the JSON; .NET `Filter` max abs error below a named epsilon (e.g. 1e-9 relative 1e-6).
  - **Step contract:** function leaf, not Presentation-exempt; unique ChannelKey; limits on a derived scalar if the verdict is not “shape only.” Optional timeseries Presentation on the filtered series for Focus.
- Tests: DF-II impulse response of a checked-in biquad matches golden; `FiltFilt` matches checked-in MATLAB `filtfilt` vector; missing `ElapsedMs` fails **both** `RequireUniform` and `ApplyTransferFunctionStep` (no index-order); `TimeBase.ElapsedMs` on a series with a null `ElapsedMs` returns the same length with NaN at that index (does not drop the row) and `RequireUniform` fails; irregular dt fails; `TsSeconds` mismatch fails (preview and step share the same default epsilon); import JSON without Ts / empty num / `InitialConditions=zi` / `timeBase=index` / `timeBase=timestamp` / `schemaVersion=2` / `a[0]=0` fails; `TfImport.OutputChannelKey` maps to MetricDraft; `filter([0.5 0.5],[1],VDC)` Lower → `TransferFunctionAlgorithm` and Save emits `ApplyTransferFunctionStep` with ElapsedMs on the acquire sibling **and no new limits**; `filtfilt([0.5 0.5],[1],VDC)` Lower → `Method=filtfilt`; nested `mean(filter([1],[1],VDC))` and `filter([1],[1],VDC)+1` fail parse or Lower (never Expressions); decompile restores num/den via `OpenTapStepKinds`; Expressions is not a dependency of a TF-only plan; Basic `package.xml` still has no Core.dll; `fft` still fails parse; `filtfilt` step is a sibling analyze not inside acquire; architecture: `TransferFunctionFilter` / `TransferFunctionGrid` have no Avalonia/Core/Ivi.Visa; `TransferFunctionTimeBase` is Authoring.Core-only (uses `StoredSample`) and is absent from Basic.
- Risks: MATLAB `filter` vs Direct Form II numerical differences — lock form and goldens, do not chase every toolbox default. Zero-state transients: document ignore-first-N or match MATLAB `zi=0`. `filtfilt` Gustafsson vs SciPy defaults — check in MATLAB R202x vectors, do not generate goldens from SciPy in CI unless labeled. Uniform `Ts` may not match acquire `IntervalMs` if the host used ingest time — Area 10 already prefers `ElapsedMs`; TF fails closed without it.
- Conflicts with: Area 3 parser (`filter` / `filtfilt` become legal). Area 10 `EvaluateProgram` (include TF). Basic plugin sources. Area 9 docs. Do not add Math.NET or a second DSP project in this area.

---

## Implementation notes

### Function catalog (Area 3/7)

Start with a **closed table** in Authoring.Core (not reflection over every OpenTAP plugin Display):

| FunctionId / AlgorithmId | Pack | Step type |
| --- | --- | --- |
| `Basic.AcquireVoltage` | Basic | `AcquireVoltageStep` |
| `Basic.MeanGte` | Basic | `MeanGteStep` |
| `Basic.PublishBandScalar` | Basic | `PublishBandScalarStep` |
| `Basic.BitSweepAcquire` | Basic | `BitSweepAcquireStep` |
| `Basic.PublishTimedSample` | Basic | `PublishTimedSampleStep` |
| `Basic.PublishSeriesCompliance` | Basic | `PublishSeriesComplianceStep` |
| `Basic.RepeatLoop` | Basic | `RepeatLoopStep` |
| `Basic.ReportStationHealth` | Basic | `ReportStationHealthStep` |
| `Basic.SafeShutdown` | Basic | `SafeShutdownStep` |
| `Basic.OperatorPrompt` | Basic | `OperatorPromptStep` |
| `Basic.OperatorInput` | Basic | `OperatorInputStep` |
| `Basic.IdentityCheck` | Basic | `IdentityCheckStep` (in-repo demos only) |
| `IC.IdentityQuery` | InstrumentComponents | `IdentityQueryStep` (name match via `OpenTapStepKinds`) |
| `IC.SafeShutdown` | InstrumentComponents | `SafeShutdownStep` |
| `IC.Dmm.MeasureVoltage*` | InstrumentComponents | Display-name catalog at bootstrap |
| `Expr.MatlabSubset` | Expressions or Basic | `ExpressionAlgorithm` → lowered step |
| `Basic.ApplyTransferFunction` | Basic | `ApplyTransferFunctionStep` (Area 11) |

When InstrumentComponents is absent, hide `IC.*` in the UI and keep demos on Basic. Unknown plugin steps remain `RawStepNode` after decompile.

### MATLAB-flavored formula subset (Areas 3 / 7)

Not MATLAB. One language, parsed in Authoring.Core.

Allowed: `+ - * / ^` and `.* ./ .^`, parentheses, comparisons `> >= < <= ==`, `&& ||`, indexing `x(1)`, `x(end)`, `x(a:b)`, and numeric row vectors `[0.5 0.5]` / `[1]` (Area 11 coefficient literals; Area 3 may still reject them until `filter` is registered). Identifiers are `InputChannelKeys` (vectors if the bound series has more than one sample).

Functions: `abs`, `sqrt`, `min`, `max`, `mean`, `sum`, `std`, `diff`, `length`, `median`. HardwareTest extras that lower to **our** steps when they match a recipe: `rise_time(x, lo, hi)`, `inband_pct(x, lo, hi)`. Area 11 adds `filter(b, a, x)` / `filtfilt(b, a, x)` with numeric coefficient vectors → `TransferFunctionAlgorithm` (not Expressions).

Forbidden: scripts, function files, toolboxes, `plot`, I/O, `eval`, classes, cell arrays, complex except where a function already returns it, `fft`, Control System Toolbox objects, and anything else not in the table. Unknown names fail parse (do not emit Expressions hoping the bench knows them). Continuous-time `tf(...)` is not a formula; import discrete coefficient JSON instead.

Lowering: `mean(x)` + scalar `LimitSpec` → `MeanGteStep` / band scalar when that is an exact recipe; top-level `filter` / `filtfilt` → `TransferFunctionAlgorithm` / `ApplyTransferFunctionStep` (Area 11, never Expressions); nested `filter` / `filtfilt` fail closed (do not emit Expressions around IIR). Otherwise OpenTAP Expressions + optional pack dependency. Preview/CI use `FormulaEvaluator` on the same AST (FilterCall does not go through `FormulaEvaluator` — it goes through `TransferFunctionFilter`). Goldens lock numeric results so preview and bench recipes cannot silently drift.

### Headless CLI shape

Area 6 owns these flags; they call Authoring.Core and **do not start Avalonia**.

```text
HardwareTest.Authoring <workspace>
HardwareTest.Authoring --bootstrap <workspace> [--opentap-home DIR] [--offline]
HardwareTest.Authoring --validate <workspace> [--strict] [--format text|json|sarif]
HardwareTest.Authoring --compat <workspace>
HardwareTest.Authoring --pack <workspace> --out dist/
HardwareTest.Authoring --eval-formulas <workspace>
HardwareTest.Authoring --help
```

`--validate` delegates to `PlanContractCli` (same exit codes as PlanValidate). Do not start Avalonia if any of these flags are present.

### Test-set repo template (documented, not a submodule)

Product repos are expected to look like:

```text
authoring.json
plans/{id}.TapPlan
plans/{id}.program.json
recordings/{planId}/{runId}/run.json   # optional operator export goldens (Area 10)
models/{outputChannelKey}.tf.json      # optional MATLAB-exported discrete TFs (Area 11)
plugins/                 # optional extra OpenTAP plugin csproj(s)
shell-apps/              # optional IShellApplication csproj(s)
.authoring/              # gitignored OpenTAP home
dist/                    # pack output (CI artifact)
```

This template does not create those product repos. Pack’s `ship-manifest.json` is the contract they consume. Recordings are **not** packed into the program TapPackage (bench does not need them).

## Run datasets — analysis (Area 10)

The operator already writes everything a formula editor needs to replay **metrics**, not SCPI.

| Artifact | Where | What it is | Use in Authoring |
| --- | --- | --- | --- |
| `run.json` | `{DataDirectory}/runs/{runId}/` and Results **Export to…** | `TestRunRecord` schema v3: `planId`, samples with `MetricKey` / `DisplayRole` / `ElapsedMs` / limits / events | **Canonical dataset.** Load with Core schema gate. |
| `opentap-results/{Table}.csv` | Same run folder when `ExportOpenTapResults` is on | OpenTAP `Sample` / `Scalar` / `Event` tables (MES/QA) | Optional later join. Not required if `run.json` is present. Default export is off on the bench. |
| Typst PDFs | `runs/{runId}/status.pdf` etc. | Derived reports | Do not parse. |
| Host cassettes | `tests/fixtures/opentap/recordings/*.progress.json` | UI/host VCR (`OpenTapRunRecording`) | Keep as **this template’s** test harness, not the product dataset format. |
| Support bundle | Home crash export | logs + config | Out of scope. |

**Tying collections to a test-set repo.** Today a run stamps `planId` (TapPlan file stem), `planName`, operator `AppVersion` / `AppCommitSha`, DUT serial — not the test-set git SHA or program TapPackage version. Area 10 binds `recordings/{planId}/**/run.json` (or a dropped export folder) to `ProgramDraft.PlanId`. That is enough to eval formulas whose `InputChannelKeys` exist as `EffectiveMetricKey`s. It is **not** enough to prove the recording was taken against this exact plan revision. A later `TestRunRecord` field (`programPackageVersion` / `testsetCommit`) would close that; do not block Area 10 on a schema bump in the operator.

**What “test and visualize against it” means here.** Select a golden `run.json` → group samples by metric → `FormulaEvaluator` for each `ExpressionAlgorithm` → (Area 11) `TransferFunctionFilter` for each `TransferFunctionAlgorithm` on the `ElapsedMs` grid → operator tiles via `TryMapRole`. Pass/fail against `LimitSpec` / recorded limits. This is **offline eval**, not a second OpenTAP execute, so Authoring still does not reference Worker. Engineers can copy a Results export into `recordings/` and iterate formulas/TFs without the bench.

**Gaps to be honest about.**

- CSV-only MES drops without `run.json` cannot feed v1 (no Presentation fields unless we re-join tables).
- Production DUT serials in git are PII; goldens should be fixtures or redacted copies. Do not make Authoring the system of record for operator data.
- `run.json` samples are the **published** Sample/Scalar stream after Presentation, not raw ADC buffers. Algorithms that need a richer table than we publish must change the plan’s publish contract first (same as today).
- Formula preview uses our AST; the packed plan uses OpenTAP. Area 3 goldens + Area 10 `--eval-formulas` catch drift for the subset; they do not prove Expressions semantics for formulas we could not lower to a Basic step.
- Transfer functions need a **uniform `ElapsedMs` grid** matching `TsSeconds`. Legacy samples with only wall-clock `Timestamp` cannot drive a TF (fail closed).

### Discrete transfer functions (Area 11)

Interchange is coefficient JSON, not MATLAB source. MATLAB remains the design tool; Authoring/bench share `TransferFunctionFilter` in Basic.

Documented MATLAB export (engineers paste; we do not ship a toolbox):

```matlab
% samples: run.json rows whose metricKey (fallback: channel) matches the input series.
% JSON fields are metricKey / channel / value / elapsedMs — not EffectiveMetricKey.
t_ms = [samples.elapsedMs]';
u    = [samples.value]';
Ts   = median(diff(t_ms)) / 1000;   % must equal TfModelJson.tsSeconds or RequireUniform fails
sys  = tf(b, a, Ts);                % already discrete; c2d(...) if sys is continuous
[num, den] = tfdata(sys, 'v');
model = struct( ...
    'schemaVersion', 1, ...
    'inputChannelKey', 'VDC', ...
    'outputChannelKey', 'VDC.filt', ...
    'tsSeconds', Ts, ...
    'numerator', num, ...
    'denominator', den, ...
    'method', 'filter', ...
    'timeBase', 'elapsedMs', ...
    'initialConditions', 'zero');
fid = fopen('models/VDC.filt.tf.json', 'w'); fwrite(fid, jsonencode(model)); fclose(fid);
y = filter(num, den, u);            % check in y as golden beside the JSON
```

`filtfilt` is post-acquire analyze only (whole buffer, non-causal). Causal `filter` may run after the sibling acquire in the same measure group.

## Leftover follow-ups (not in this stack)

- Mock-run from Authoring via Worker for live preview.
- MATLAB Engine / Runtime / `.m` toolbox / Simulink / MIMO (explicit non-goal). MATLAB Coder → extra plugin for nonlinear models.
- c2d / bilinear in C# (discretize in MATLAB before export).
- Stamp `programPackageVersion` / test-set commit on `TestRunRecord` so recordings bind to a plan revision, not only `planId`.
- Import `opentap-results/*.csv` when `run.json` is missing.
- Resample irregular `ElapsedMs` instead of fail-closed.
- Feed browser / `tap package install` from Keysight repo inside the UI.
- Generating operator `Composition.cs` bake fragments for shell apps.
- macOS authoring RID beyond existing lockfile RIDs.
- Unify Mixins `PresentationDisplayRoles` with operator role constants if Area 5 leaves a wrapper.
- Authoring PluginManager must not inherit Host’s Visa project-reference search path (assert in Areas 2–3).
- Optional `sourceRunPath` on `TfModelJson` so import auto-binds the same `run.json` used in MATLAB.
- `filtfilt` inside `RepeatNode`: per-iteration buffer vs whole-series (v1: sibling analyze of the preceding acquire only).
- Fail closed when the series is shorter than MATLAB `filtfilt` allows (pad/trim policy).
