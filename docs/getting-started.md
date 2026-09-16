# Getting started: HardwareTest Authoring

Author a locked test plan in **HardwareTest.Authoring**, then run it in the operator shell. The operator shell does not edit plans.

This walkthrough opens the in-repo template workspace [`plans/opentap/`](../plans/opentap/), adds metrics with recipes, saves a TapPlan, and packs. Productization, plan-contract tables, and settings: [adapting.md](adapting.md). Architecture: [authoring-app.md](authoring-app.md). Layering rules: [README.md](../README.md).

OpenTAP TUI / Editor is an optional [escape hatch](#6-optional-opentap-tui-escape-hatch) when you need the stock step tree.

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
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --pack plans/opentap --out dist/
dotnet run --project src/HardwareTest.Authoring -c Debug -r win-x64 -- --help
```

`--opentap-home DIR` overrides the isolated home for `--bootstrap`, `--compat`, and `--pack`. `--format text|json|sarif` applies to `--validate`. `--compat` and `--pack` fail closed when TUI compatibility `BlocksPack` (unknown types, dropped mixins, contract errors, or authoring types the TUI home cannot load).

## 2. Create a program and add recipes

1. **New program** seeds Identity + Cleanup + Mock DMM (in-repo demos). Product workspaces that declare InstrumentComponents.OpenTap still keep this template’s sample/board-demo on Basic.
2. On the **Program** tab, pick a recipe and **Add recipe**. The palette matches the test types below; **Dialog** and **Hang Forever** are not listed.
3. Edit **Channel key**, **Display role**, **Y unit**, limits / threshold, and **VisaAddress** on the first instrument. Mean GTE needs a threshold; band and series need both limits. **Save plan** refuses missing limits.
4. **Preview** shows canned samples for the selected DisplayRole (not Execute).
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

## 5. Validate, pack, then run in the operator shell

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

## 6. Optional: OpenTAP TUI escape hatch

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

In TUI: **New** test plan (or open [`plans/opentap/sample.TapPlan`](../plans/opentap/sample.TapPlan) and Save As). Add three **Test Group** steps (`HardwareTest` group): `Setup`, a measure group, and `Cleanup`. Add **one instrument resource per box**. Product plans: pick a typed *Instrument Components* instrument. In-repo demos use **Mock DMM**. Keep **`VisaAddress`** writable. Save as `{planId}.TapPlan` under `plans/opentap/` (copied to `Programs/` on build). Attach Presentation by hand (**Add Mixin** → **Presentation**). Then `--compat` / `--pack` as in [§5](#5-validate-pack-then-run-in-the-operator-shell).

`--compat` compares the authoring catalog and a Load/Save round-trip against a TUI-equipped home. CI: `deno run -A tools/ci/main.ts test:authoring-compat`.

## Next

- Productize plugins, reports, and settings: [adapting.md](adapting.md)
- Tests for a new plan or plugin: [testing.md](testing.md)
- Bake onto a sealed bench: [appliance-linux.md](appliance-linux.md)
- Authoring architecture: [authoring-app.md](authoring-app.md)
