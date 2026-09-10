# Getting started: OpenTAP TUI and HardwareTest packages

Author a locked test plan in **OpenTAP TUI** (or Editor), then run it in this shell. The shell does not edit plans.

This walkthrough gets a first program onto the Run board and shows how to add each test type we ship today. Productization, plan-contract tables, and settings: [adapting.md](adapting.md). Layering rules: [README.md](../README.md).

## 1. Install OpenTAP and TUI

You need the same OpenTAP major version this repo pins (`^9.32.2` in [`plans/opentap/package.xml`](../plans/opentap/package.xml)).

```bash
tap package install TUI
tap tui
```

Official notes: [OpenTAP editors](https://doc.opentap.io/User%20Guide/Editors/Readme.html) and the [TUI package](https://github.com/StefanHolst/opentap-tui). Developer System (`tap editor`) works the same for the steps below.

## 2. Install our authoring packages

TUI only lists steps and mixins from packages installed into that OpenTAP tree. Build and install the same versions the bench will bake:

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

## 3. Create the plan skeleton

1. In TUI, **New** test plan (or open [`plans/opentap/sample.TapPlan`](../plans/opentap/sample.TapPlan) and Save As).
2. Add three **Test Group** steps (`HardwareTest` group): `Setup`, a measure group (name the suite), and `Cleanup`. Keep nest depth at three levels.
3. Give every leaf a unique name. Duplicate sibling names force path-qualified selection on the Run board.
4. Add **one instrument resource per box**. Product plans: pick a typed *Instrument Components* instrument (DMM, PSU, FGen, scope, switch, counter, power meter, spectrum analyzer). Extra capabilities are nested views on that resource, not a second slot. Keep **`VisaAddress`** writable so Instruments can rebind.
5. In-repo demos use **Mock DMM** (`HardwareTest`) so CI does not need the library pack.

Save as `{planId}.TapPlan` under `plans/opentap/` (copied to `Programs/` on build).

## 4. Add each kind of test

Steps appear under the **HardwareTest** groups below (or the matching *Instrument Components* type). Assign the instrument on every step that talks to hardware. Attach **Presentation** on function leaves (see [§5](#5-attach-presentation)).

### Structure — Test Group

**Add step → HardwareTest → Test Group.** Use groups for Setup / measure / Cleanup only. The group itself does not publish results.

### Identity

When the sidecar has `requireSerial: true`, the plan needs an identity step.

- **Product:** *Identity Query* (Instrument Components). DUT serial is the shell confirm — do not add a `HardwareDut` resource.
- **In-repo demos:** **Identity Check** (`HardwareTest` / `Identity`). Assign Mock DMM and a **Hardware DUT** so the demo can stamp serial.

### Operator confirm — Operator Prompt

**Add step → HardwareTest / Operator → Operator Prompt.** Set **Message**. The Run board pauses in-panel; the technician presses **Continue**. Use this for fixture seating, clear-the-area, and other confirm-only pauses.

Never add OpenTAP **Dialog** steps. The validator rejects them.

### Operator typed input — Operator Input

**Add step → HardwareTest / Operator → Operator Input.** Set **Title**, **Message**, string field id/label (and optional number field). Values return to the step and run results; they are **not** station overrides.

### Measure waveform — Acquire Voltage (or library measure)

**Add step → HardwareTest / Measure → Acquire Voltage** (demos) or a library measure step (TUI lists Display names such as *DMM Measure Voltage AC* or *Scope Capture Trace*).

1. Assign the instrument. Set channel / sample count / interval as needed.
2. Attach Presentation with `DisplayRole` = `timeseries` and a unique `ChannelKey` (for example `VDC`) only when operators need the shape.
3. Optional series band: set **Limit low** / **Limit high** and **Series compliance** `allSamples` or `dwell`.

Library measure steps already publish `Sample` / `Scalar`. Prefer those on product plans.

### Threshold / band — Mean GTE or Publish Band Scalar

Pass/fail belongs on a **scalar** or **passband**, not on a chart.

- **Mean GTE** (`HardwareTest` / `Analyze`): pass if the mean meets **Threshold**. Attach Presentation `ChannelKey` = `VDC.mean`, `DisplayRole` = `scalar`, set **Y unit**.
- **Publish Band Scalar** (`HardwareTest` / `Analyze`): one derived metric (`bump.rise.ms`, `envelope.error`, …) with optional **Limit low** / **Limit high**. Use `passband` when both bounds matter.
- **Library:** scalar measure steps take inclusive `LimitLow` / `LimitHigh`.

Write the criterion in words first, then publish **one Scalar per criterion**. Recipe table: [adapting.md](adapting.md#presentation-and-reports).

### Series in band + events — Bit Sweep / timed sample

When every sample must stay in band, or you need config-change marks:

- **Bit Sweep Acquire** (`HardwareTest` / `Measure`): publishes `Sample` + `Event` on one elapsed clock. Set limits and `SeriesCompliance=allSamples` (or `dwell` + **Dwell limit ms**). Keep Presentation on this step as `timeseries`. Turn **Publish summaries** on only when you want `series.inband.pct` from the same step; otherwise add **Publish Series Compliance** as a sibling analyze step (`passband` on `series.inband.pct`).
- **Publish Timed Sample** (`HardwareTest` / `Measure`): one `Sample` with optional `ElapsedMs` and an **Event** label (for example `bit3`).

### Repeat / sweep

**Add step → HardwareTest / Flow → Repeat Loop.** Set **Count** and nest measure children. OpenTAP Sweep/Repeat steps also work. The Run hero shows innermost `iter i/N`. Edit bounds here or in Engineer **Station overrides** — not as operator prompts.

### Station health

**Add step → HardwareTest / Station → Report Station Health.** Publishes `cal.dc.offset` and `cal.age.hours`. Use unique ChannelKeys (no single mixin covering both). Mark the sidecar `programKind` as `stationHealth` for the health program itself; DUT programs opt in with `requireStationHealth` + `warn`|`block`. Do not skip this step via `Enabled`. Details: [adapting.md](adapting.md#author-a-locked-program).

### Cleanup — Safe Shutdown

**Add step → HardwareTest / Safety → Safe Shutdown** (or library *Safe Shutdown*). Assign the same instrument. Required when `selectionIncludesCleanup` is true (the default). Set the sidecar false only when shutdown is suite-scoped and Run Selected is software-only.

### Do not add

| Step | Why |
| --- | --- |
| OpenTAP **Dialog** / OS message boxes | Appliance rule — use Operator Prompt / Input |
| **Hang Forever** (`HardwareTest` / `Test`) | Worker-kill fixture only; not for operator plans |

## 5. Attach Presentation

Select a function leaf → **Add Mixin** → **Presentation** (`HardwareTest`). Set:

| Field | Typical value |
| --- | --- |
| **Channel key** | Stable id (`rail.3v3.mean`, `VDC`). Unique in the plan. |
| **Display role** | `scalar` or `passband` for verdicts; `timeseries` only for shape |
| **Y unit** | `V`, `ms`, … |
| History fields | Optional DUT-history watch/alert percents |

Identity, Prompt, Input, Safe Shutdown, Hang Forever, Repeat Loop, and Test Group are exempt. Empty or duplicate `ChannelKey` is a contract error; band roles without limits warn.

Optional: **Annotation** mixin for a bench note (Engineer station override). The shell does not add mixins — attach them in TUI/Editor.

## 6. Add the program sidecar

Copy [`plans/opentap/template.program.json`](../plans/opentap/template.program.json) to `{planId}.program.json` next to the TapPlan. Edit `displayName`, DUT flags, and `reportKinds` there. Sidecar is session/DUT/Typst only — instrument requirements belong in TapPackage Dependencies. Field reference: [adapting.md](adapting.md#author-a-locked-program).

## 7. Validate, then run in the shell

```bash
HardwareTest.PlanValidate plans/opentap --strict
# Product plans that use the library pack:
HardwareTest.PlanValidate path/to/product-plans --strict --opentap-plugin-dirs path/to/InstrumentComponents.OpenTap
```

`--strict` fails a missing sidecar. Warnings do not block operator Run. Check table: [adapting.md](adapting.md#plan-contract).

Then:

1. `dotnet run --project src/HardwareTest -c Debug -r win-x64` (RID required).
2. Confirm DUT on Run (and technician when `requireOperator` is true).
3. Bind instruments on **Instruments** if slots are unbound (Engineer / debug mode shows that page).
4. **Run**. Operator Prompt/Input appear in-panel. Band gauges show scalar/passband; Chart appears when a timeseries earns Focus.
5. Open **Results** for the run, DUT history, and Typst PDFs (`status` / `certification`).

Mock instruments: keep `UseMockVisa` on until a vendor VISA runtime is installed.

## Next

- Productize plugins, reports, and settings: [adapting.md](adapting.md)
- Tests for a new plan or plugin: [testing.md](testing.md)
- Bake onto a sealed bench: [appliance-linux.md](appliance-linux.md)
