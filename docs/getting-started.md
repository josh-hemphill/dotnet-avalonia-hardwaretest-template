# Getting started: HardwareTest Authoring

Author a locked test plan in **HardwareTest.Authoring**, then run it in the operator shell. The operator shell does not edit plans.

This walkthrough opens the in-repo template workspace [`plans/opentap/`](../plans/opentap/), adds metrics with recipes, saves a TapPlan, and packs. Productization, plan-contract tables, and settings: [adapting.md](adapting.md). Architecture: [authoring-app.md](authoring-app.md). Layering rules: [README.md](../README.md).

OpenTAP TUI / Editor is an optional [escape hatch](#7-optional-opentap-tui-escape-hatch) when you need the stock step tree.

## 1. Run HardwareTest.Authoring on the template workspace

RID is required (same as the operator exe):

```bash
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- plans/opentap
```

The window opens that folder when it contains `authoring.json`. **Open workspace…** picks any other test-set directory.

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

1. **New program** seeds Identity + Cleanup + Mock DMM (in-repo demos). Product workspaces that declare InstrumentComponents.OpenTap still keep this template’s sample/board-demo on Basic.
2. On the **Program** tab, pick a recipe and **Add recipe**. The palette matches the test types below; **Dialog** and **Hang Forever** are not listed.
3. Edit **Channel key**, **Display role**, **Y unit**, limits / threshold, and **VisaAddress** on the first instrument. Mean GTE needs a threshold; band and series need both limits. **Save plan** refuses missing limits. **Formula…** edits a MATLAB-flavored subset; **Transfer function…** edits numerator / denominator / Ts. Preview uses canned samples unless a recording is selected.
4. **Preview** shows canned samples for the selected DisplayRole (not Execute). Select a `recordings/` export to eval formulas and transfer functions on real `elapsedMs` series.
5. **Save plan** compiles the metric IR to `{planId}.TapPlan` + `{planId}.program.json` (three-level groups, Presentation on function leaves, sidecar). **Save sidecar** writes only the program JSON.

Keep **VisaAddress** writable so the operator Instruments page can rebind.

Sidecar fields (`displayName`, DUT flags, `reportKinds`) live on the Program tab. Field reference: [adapting.md](adapting.md#author-a-locked-program). Copy [`plans/opentap/template.program.json`](../plans/opentap/template.program.json) only when you author a sidecar by hand.

## 3. Add each kind of test

Recipes appear under **Recipes** on the Program tab. Assign the instrument on every step that talks to hardware. Presentation is written on **Save plan** (see [§4](#4-presentation)).

### Structure — Test Group

**Test Group.** Setup / measure / Cleanup groups are written on Save. The group itself does not publish results. Keep nest depth at three levels. Give every leaf a unique name — duplicate sibling names force path-qualified selection on the Run board.

### Identity — Identity Check

When the sidecar has `requireSerial: true`, the plan needs an identity step. **New program** already adds one.

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

**Repeat Loop** wraps the last measure node. OpenTAP Sweep/Repeat steps also work in TUI. The Run hero shows innermost `iter i/N`. Edit bounds here or in Engineer **Station overrides** — not as operator prompts.

### Station health

**Report Station Health.** Publishes `cal.dc.offset` and `cal.age.hours`. Use unique ChannelKeys (no single mixin covering both). Mark the sidecar `programKind` as `stationHealth` for the health program itself; DUT programs opt in with `requireStationHealth` + `warn`|`block`. Do not skip this step via `Enabled`. Details: [adapting.md](adapting.md#author-a-locked-program).

### Cleanup — Safe Shutdown

**Safe Shutdown** (or library *Safe Shutdown* in TUI). Assign the same instrument. Required when `selectionIncludesCleanup` is true (the default). Set the sidecar false only when shutdown is suite-scoped and Run Selected is software-only. **New program** already adds Cleanup.

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
