# Getting started: HardwareTest Authoring

Author a locked test plan in **HardwareTest.Authoring**, then run it in the operator shell. The operator shell does not edit plans.

This walkthrough opens the in-repo template workspace [`plans/opentap/`](../plans/opentap/), adds metrics with recipes, saves a TapPlan, and packs. Productization, plan-contract tables, and settings: [adapting.md](adapting.md). Architecture: [authoring-app.md](authoring-app.md). Layering rules: [README.md](../README.md).

OpenTAP TUI / Editor is an optional [escape hatch](#7-optional-opentap-tui-escape-hatch) when you need the stock step tree.

## 1. Run HardwareTest.Authoring on the template workspace

RID is required (same as the operator exe):

```bash
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- plans/opentap
```

The window opens that folder when it contains `authoring.json`. **Open workspace…** picks any other test-set directory. The toolbar **Settings** button opens a separate window for theme (System/Light/Dark), last workspace, an optional OpenTAP home override, and whether raw TUI XML is shown — stored in `%AppData%/HardwareTest/authoring-preferences.json` (Linux `~/.config/HardwareTest/authoring-preferences.json`), not operator `settings.json`. Last workspace is offered on the Programs rail; it is never auto-opened.

**Bootstrap** installs Editor packs (HardwareTest Basic + Mixins; InstrumentComponents.OpenTap when the workspace manifest lists it) into `{workspace}/.authoring/opentap/`. The GUI Bootstrap button is offline. Headless flags exit before Avalonia:

```bash
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --bootstrap plans/opentap --offline
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --validate plans/opentap --strict
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --compat plans/opentap
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --eval-formulas plans/opentap
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --pack plans/opentap --out dist/
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --help
```

`--opentap-home DIR` overrides the isolated home for `--bootstrap`, `--compat`, and `--pack`. `--format text|json|sarif` applies to `--validate`. `--compat` and `--pack` fail closed when TUI compatibility `BlocksPack` (unknown types, dropped mixins, contract errors, or authoring types the TUI home cannot load).

## 2. Create a program and add recipes

1. **New test plan** opens the shared initializer with explicit Empty, Voltage task and Demo voltage task choices. **First voltage test…** offers optional task guidance. Empty plans contain no instruments; only an explicit Demo choice selects a Mock DMM. Choose a physical instrument and its address for product plans, and review instrument identity and safe shutdown before creation. **Remove program** (or Delete on the programs list) drops the selected plan from the session and deletes its `.TapPlan` + `.program.json` when those files exist.
2. On the **Program** tab, search **Add to sequence** by Measure, Check, Operator action or Flow, choose **Before selected**, **After selected** or **End of section**, review the prerequisites, and **Add recipe**. Rename, Duplicate and Move up/down apply to the selected step; Undo/Redo restore complete program edits. Duplicate allocates fresh IDs and channels while preserving external references. Moves that break dependencies, loop scope or opaque steps are rejected with an explanation. **Remove selected** (or Delete on the sequence list) drops the highlighted Setup, measure, Repeat, or Raw row. **Remove loop, keep steps** unwraps Repeat children. **Disable safe shutdown** turns Cleanup off. The sequence list is Setup / Measure / Cleanup — not a tree. Repeat children are indented under the Repeat row. **Dialog** and **Hang Forever** are not listed.
3. The **Inspector** edits only the selected sequence row (channel key, display role, unit, limits, formula chips, transfer-function method). **Hardware** holds program sidecar membership (DUT fields, reports) and the instrument table with actual type, address, package availability, affected steps and cleanup coverage. **Definitions** administers workspace catalogs and hardware templates. Mean GTE needs a threshold; band and series need both limits before compilation. Missing criteria and incomplete numeric text can still be saved as authoring drafts. Preview uses canned samples unless a recording is selected.
4. **Preview** shows canned samples for the selected DisplayRole (not Execute). Select a `recordings/` export to eval formulas and transfer functions on real `elapsedMs` series.
5. **Save plan** writes the durable source in `authoring-drafts/{planId}.authoring.json`, then compiles deployable content to `{planId}.TapPlan` + `{planId}.program.json`. Incomplete content remains saved with an explanation of what prevents compilation. **Save sidecar** persists the authoring source and exports only the program settings; changed sequence content still requires compilation before checked packaging.

To change a program binding, select its slot in **Hardware**, click **Load selected binding**, choose a registered adapter and its address/configuration, then **Review binding change**. The dialog names the affected steps. Applying preserves logical slot and step identities; **Undo**/**Redo** restores the program edit. Removal explicitly chooses a compatible remaining slot and reviews every known reference. Imported opaque bindings stay protected.

**Definitions** creates report kinds, program kinds and required fields without changing any program membership. Choose membership in **Hardware**. Workspace removal reviews each affected program, including its default-report fallback or reset to `dut`, and **Undo catalog**/**Redo catalog** restores the transaction. Use **Save All** to persist workspace changes.

Hardware templates have stable identities in `authoring.json`. **Include definition in selected program** copies the visible type/address/configuration only into that program. Updating or removing a template leaves existing program bindings independent; update those through Hardware review. The operator Instruments page can still rebind its bench runtime resource.

**Undo** and **Redo** apply to the selected program's committed edits, including sequence, instrument and sidecar changes. Each program keeps its own history and selected step when you switch programs. Saving keeps the history: Undo can make a saved program dirty again, while returning to its saved content clears the dirty marker. **Save sidecar** advances only the settings baseline; sequence edits still require **Save plan** or **Save all**.

**Undo catalog** and **Redo catalog** restore a workspace catalog operation together with its affected programs. These controls become unavailable if intervening program edits would be overwritten; undo those edits first. **Remove program** deletes files and cannot be undone. Histories last for the open workspace session; saved authoring documents and recovery checkpoints survive reopening.

Editing creates debounced local checkpoints under `.authoring/recovery/`. A checkpoint does not clear the unsaved marker. On reopening, review the recovery notice and explicitly restore or discard newer content. Recovery failures remain visible while your draft stays open. Saved source documents retain stable row identities, formula intent, and incomplete input; future-schema sources are preserved read-only. Local recovery, builds, and package homes stay out of source control; commit `authoring-drafts/` with the workspace.

If compiled plans change outside the app, review the conflict and choose whether to import those changes or retain your source before exporting again. Saving a draft alone does not make stale compiled artifacts ready for packaging. Workspace manifests must use the current schema version 2. Older manifests are rejected without changes; future manifests remain read-only and untouched.

Sidecar fields (`displayName`, DUT flags, `reportKinds`) live on **Hardware**. Field reference: [adapting.md](adapting.md#author-a-locked-program). Copy [`plans/opentap/template.program.json`](../plans/opentap/template.program.json) only when you author a sidecar by hand.

## 3. Add each kind of test

Recipes appear under **Add to sequence** on the Program tab. Assign the instrument on every step that talks to hardware. Presentation is written on **Save plan** (see [§4](#4-presentation)).

### Structure — Test Group

**Test Group.** Setup / measure / Cleanup groups are generated on Save and are absent from the insertion palette. The group itself does not publish results. Keep nest depth at three levels. Give every leaf a unique name — duplicate sibling names force path-qualified selection on the Run board.

### Identity — Identity Check

When the sidecar has `requireSerial: true`, the plan needs an identity step. Select **Check instrument identity** with the chosen instrument during initialization, or add it in the normal editor. Guided voltage tasks select the identity check by default; empty plans remain incomplete until their required hardware and identity are configured.

- **In-repo demos:** **Identity Check**. Assign Mock DMM and a **Hardware DUT** so the demo can stamp serial.
- **Product:** *Identity Query* (Instrument Components). DUT serial is the shell confirm — do not add a `HardwareDut` resource. Use TUI if that library step is not in the recipe palette.

### Operator confirm — Operator Prompt

**Operator Prompt.** Set the message. The Run board pauses in-panel; the technician presses **Continue**. Use this for fixture seating, clear-the-area, and other confirm-only pauses.

Never add OpenTAP **Dialog** steps. The validator rejects them.

### Operator typed input — Operator Input

**Operator Input.** Title, message, string field id/label (and optional number field). Values return to the step and run results; they are **not** station overrides.

### Measure waveform — Acquire Voltage (or library measure)

**Acquire Voltage** (demos) publishes a timeseries (`ChannelKey` `VDC`). Product plans prefer a library measure step (Display names such as *DMM Measure Voltage AC* or *Scope Capture Trace*) via TUI when the palette does not list them.

1. Assign the instrument. Set channel / sample count / interval as needed.
2. Use `DisplayRole` = `timeseries` only when operators need the shape.
3. Optional series band: set **Limit low** / **Limit high**.

Library measure steps already publish `Sample` / `Scalar`. Prefer those on product plans.

### Threshold / band — Mean GTE or Publish Band Scalar

Pass/fail belongs on a **scalar** or **passband**, not on a chart.

- **Mean GTE**: pass if the mean meets **Threshold**. Presentation `ChannelKey` = `VDC.mean`, `DisplayRole` = `scalar`. Set **Y unit**.
- **Publish Band Scalar**: one derived metric (`bump.rise.ms`, `envelope.error`, …) with **Limit low** / **Limit high**. Use `passband` when both bounds matter.
- **Library:** scalar measure steps take inclusive `LimitLow` / `LimitHigh`.

Write the criterion in words first, then publish **one Scalar per criterion**. Recipe table: [adapting.md](adapting.md#presentation-and-reports).

### Formula — MATLAB-flavored subset

**Formula…** is not MATLAB Runtime. One language, parsed in Authoring. Unknown names (`fft`, `plot`, `eval`, continuous `tf`) fail parse. Nested `filter` / `filtfilt` fail closed (they never become OpenTAP Expressions).

Allowed operators: `+ - * / ^`, `.* ./ .^`, parentheses. Functions: `abs`, `sqrt`, `min`, `max`, `mean`, `sum`, `std`, `diff`, `length`, `median`, `filter`, `filtfilt`, plus HardwareTest `rise_time` / `inband_pct`. Coefficient vectors for IIR sugar: `[0.5 0.5]` or `[0.5, 0.5]`. Preview and `--eval-formulas` evaluate that subset on a series. **Save plan** / pack only lower two shapes: `mean(x)` plus a scalar threshold → **Mean GTE**, and top-level `filter` / `filtfilt` → **Apply Transfer Function**. Any other parsed formula fails `FORMULA_NO_LOWER` (OpenTAP Expressions are not emitted).

### Transfer function — discrete SISO IIR

**Transfer function…** (or top-level `filter(b, a, x)` / `filtfilt(b, a, x)` in a formula) compiles to **Apply Transfer Function**, a native Basic analyze step. Coefficients come from the editor or from MATLAB-exported `models/*.tf.json` — not `.m` files. There is no MATLAB Engine or Runtime in Authoring or on the bench.

`filtfilt` is a sibling analyze of the acquire (whole series, zero-phase). Causal `filter` is the same placement. Both need a uniform `ElapsedMs` grid; **Acquire Voltage** always publishes `IntervalMs * index` so the first operator `run.json` already has a clock. Timestamp-only recordings fail closed.

Import JSON (`schemaVersion` 1): `tsSeconds`, `numerator`, `denominator`, `method` (`filter` or `filtfilt`, lowercase), `timeBase` = `elapsedMs`, `initialConditions` = `zero`, `inputChannelKey`, `outputChannelKey`. Extra properties, `tsSeconds <= 0`, and a leading denominator of `0` fail import. Export snippet: [adapting.md](adapting.md#discrete-transfer-functions).

### Series in band + events — Bit Sweep / timed sample

When every sample must stay in band, or you need config-change marks:

- **Publish Series Compliance**: in-band percent passband. Pair it with **Acquire Voltage** (or Bit Sweep in TUI). Limits must match the voltage band, not a dummy percent such as `100`.
- **Bit Sweep Acquire** / **Publish Timed Sample** (TUI when not in the palette): publishes `Sample` + `Event`. Keep Presentation on the acquire step as `timeseries`.

### Repeat / sweep

**Repeat Loop** wraps the selected eligible measurement or loop; its prerequisite text names the target. OpenTAP Sweep/Repeat steps also work in TUI. The Run hero shows innermost `iter i/N`. Edit bounds here or in Engineer **Station overrides** — not as operator prompts.

### Station health

**Report Station Health.** Publishes `cal.dc.offset` and `cal.age.hours`. Use unique ChannelKeys (no single mixin covering both). Mark the sidecar `programKind` as `stationHealth` for the health program itself; DUT programs opt in with `requireStationHealth` + `warn`|`block`. Do not skip this step via `Enabled`. Details: [adapting.md](adapting.md#author-a-locked-program).

### Cleanup — Safe Shutdown

**Safe Shutdown** (or library *Safe Shutdown* in TUI). Assign the same instrument. Required when `selectionIncludesCleanup` is true (the default). Set the sidecar false only when shutdown is suite-scoped and Run Selected is software-only. Initialization offers **Safe shutdown selected resources** and shows its instrument coverage; empty plans have no resources to shut down.

### Do not add

| Step | Why |
| --- | --- |
| OpenTAP **Dialog** / OS message boxes | Appliance rule — use Operator Prompt / Input |
| **Hang Forever** (`HardwareTest` / `Test`) | Worker-kill fixture only; not for operator plans |

## 4. Presentation

**Save plan** attaches **Presentation** (`HardwareTest`) on function leaves:

| Field | Typical value |
| --- | --- |
| **Channel key** | Stable id (`rail.3v3.mean`, `VDC`). Unique in the plan. |
| **Display role** | `scalar` or `passband` for verdicts; `timeseries` only for shape |
| **Y unit** | `V`, `ms`, … |
| History fields | Optional DUT-history watch/alert percents |

Identity, Prompt, Input, Safe Shutdown, Hang Forever, Repeat Loop, and Test Group are exempt. Empty or duplicate `ChannelKey` is a contract error; band roles without limits warn.

Optional: **Annotation** mixin for a bench note (Engineer station override). The operator shell does not add mixins. Authoring writes Presentation on Save; TUI/Editor authors still attach mixins by hand.

## 5. Check formulas and transfer functions against recordings

Copy an operator Results export into `{workspace}/recordings/{planId}/{runId}/run.json`. The Program tab lists datasets for the current `planId`. Selecting one drives Preview from real samples (not Execute). `--eval-formulas` walks that workspace `recordings/` tree (not `tests/fixtures/authoring/`, which are unit-test goldens) and fails on parse / eval / missing limits / irregular `elapsedMs`:

```bash
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --eval-formulas plans/opentap
```

Transfer-function eval uses the same Direct Form II transposed filter as the bench step. Identification recordings must include finite `elapsedMs` on every sample (do not use timestamp-only `run-v1.json`). DUT serial is optional.

## 6. Validate, pack, then run in the operator shell

```bash
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --validate plans/opentap --strict
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --compat plans/opentap
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --pack plans/opentap --out dist/
```

`--validate` reuses `PlanContractCli` (same exit codes as `HardwareTest.PlanValidate`). `--strict` fails a missing sidecar. Warnings do not block operator Run. Check table: [adapting.md](adapting.md#plan-contract).

Appliance CI can still call `HardwareTest.PlanValidate` directly:

```bash
HardwareTest.PlanValidate plans/opentap --strict
# Product plans that use the library pack:
HardwareTest.PlanValidate path/to/product-plans --strict --opentap-plugin-dirs path/to/InstrumentComponents.OpenTap
```

Then:

1. `dotnet run --project src/HardwareTest -c Debug -r win-x64` (RID required).
2. Confirm DUT on Run (and technician when `requireOperator` is true).
3. Bind instruments on **Instruments** if slots are unbound (Engineer / debug mode shows that page).
4. **Run**. Operator Prompt/Input appear in-panel. Band gauges show scalar/passband; Chart appears when a timeseries earns Focus.
5. Open **Results** for the run, DUT history, and Typst PDFs (`status` / `certification`).

Mock instruments: keep `UseMockVisa` on until a vendor VISA runtime is installed. Pack output is `dist/` artifacts for bake, not a live load into the operator process. Ship tab **Pack…** writes that `dist/`. Headless `--pack` also runs the TUI compatibility checker.

## 7. Optional: OpenTAP TUI escape hatch

Use TUI when you need the stock editor (sweeps, ComponentSettings, or a library step Authoring decompiles as Raw). Prefer the isolated home from **Bootstrap** so TUI and Authoring share one package set.

You need the same OpenTAP major version this repo pins (`^9.32.2` in [`plans/opentap/package.xml`](../plans/opentap/package.xml)). After Bootstrap, install TUI into that home (needs network unless TUI is already present) and launch it from there:

```bash
home=plans/opentap/.authoring/opentap
"$home/tap" package install TUI
PATH="$home:$PATH" tap tui
```

Official notes: [OpenTAP editors](https://doc.opentap.io/User%20Guide/Editors/Readme.html) and the [TUI package](https://github.com/StefanHolst/opentap-tui). Developer System (`tap editor`) works the same for the steps below.

TUI only lists steps and mixins from packages installed into that OpenTAP tree. Bootstrap already installed Basic + Mixins into `{workspace}/.authoring/opentap/`. To install into a machine-global `tap` instead:

```bash
dotnet build src/HardwareTest.OpenTap.Plugins.Basic -c Release -r linux-x64 -p:CreateOpenTapPackage=true -p:InstallCreatedOpenTapPackage=false
dotnet build src/HardwareTest.OpenTap.Plugins.Mixins -c Release -r linux-x64 -p:CreateOpenTapPackage=true -p:InstallCreatedOpenTapPackage=false
tap package install path/to/HardwareTest\ Basic*.TapPackage
tap package install path/to/HardwareTest\ Mixins*.TapPackage
```

For product plans, also install **InstrumentComponents.OpenTap** (typed SCPI instruments and function steps). Build that pack from the [instrument-components](https://josh-hemphill.github.io/instrument-components/csharp/opentap/) repo — this template does not ship it. Then:

```bash
tap package install path/to/InstrumentComponents.OpenTap*.TapPackage
```

Optional: **Expressions** when the plan uses expression steps (`^1.5.0` is OpenTAP’s example, not a CI pin).

Restart TUI after installing packs so the step list refreshes. On the bench, confirm **Settings → OpenTAP packages & plugins**. Pack build details: [adapting.md](adapting.md#authoring-packs).

In TUI: **New** test plan (or open [`plans/opentap/sample.TapPlan`](../plans/opentap/sample.TapPlan) and Save As). Add three **Test Group** steps (`HardwareTest` group): `Setup`, a measure group, and `Cleanup`. Add **one instrument resource per box**. Product plans: pick a typed *Instrument Components* instrument. In-repo demos use **Mock DMM**. Keep **`VisaAddress`** writable. Save as `{planId}.TapPlan` under `plans/opentap/` (copied to `Programs/` on build). Attach Presentation by hand (**Add Mixin** → **Presentation**). Then `--compat` / `--pack` as in [§6](#6-validate-pack-then-run-in-the-operator-shell).

`--compat` compares the authoring catalog and a Load/Save round-trip against a TUI-equipped home. CI: `deno run -A tools/ci/main.ts test:authoring-compat`.

## Next

- Productize plugins, reports, and settings: [adapting.md](adapting.md)
- Tests for a new plan or plugin: [testing.md](testing.md)
- Bake onto a sealed bench: [appliance-linux.md](appliance-linux.md)
- Authoring architecture: [authoring-app.md](authoring-app.md)

## Guided first voltage test

Create an Empty, Product hardware scaffold or explicit Demo voltage workspace from the welcome screen, then choose **First voltage test…**. Physical workspaces declare Instrument Components; Demo voltage uses explicit Mock hardware. Guidance uses the normal plan initializer, document, editor and save operations.

The six stages are name/device, instrument, measurement, pass criterion, preview, and save/check. Instrument identity and safe shutdown are shown in the preview. Choose **Empty plan** for a direct route to the normal editor. Demo explicitly selects a Mock DMM; physical instrument selection requires the declared package and its installed payload.

**Leave guidance** retains entered form values, including incomplete numeric text, for **Resume guidance** in the current workspace session. Reusable instrument selection retains its original address and configuration if catalog entries move, change or disappear; changed or removed choices are labelled for review. No source is published until the final Save action. After that action, the normal document owns edits, history, stable IDs and recovery. **Leave saved guidance** hides the help without altering the document. Reopening a saved plan and choosing Resume uses that plan's saved content.

**Skip optional guidance** is a workstation preference (`skipGuidance` in authoring-preferences.json). Future-schema settings remain read-only. A subsequent First voltage test command opens the ordinary initializer; Resume explicitly enables guidance again. If future-schema settings are read-only and skip guidance, Resume enables it for the current workspace session while preserving the exact settings bytes.

Use the existing editor preview with example data or a recording, **Save and check draft** to save/compile, and **Validate saved plans** for the shared validation operation. Missing bindings, incomplete criteria and packages point to Issues and Environment. Build shows deployment requirements. The completion message requires saved, compiled source with matching artifact hashes and no current editing blockers. It does not execute or deploy to a bench.
