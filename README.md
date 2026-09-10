# Avalonia Hardware Test Template

Avalonia 12 desktop shell for **OpenTAP-sequenced hardware tests**, **IVI VISA discovery**, **ScottPlot live plots**, and **Typst PDF reports**. Self-contained (non-NativeAOT) publishes target sealed Linux appliance images.

**License:** [MIT](LICENSE)

## Layout

```
src/
  HardwareTest/                      # Avalonia exe — App / Features / Widgets
  HardwareTest.Core/                 # Avalonia-free: logging, settings, VISA, runs, reporting
  HardwareTest.OpenTap.Host/         # OpenTAP session façade (load / run / pause / abort)
  HardwareTest.OpenTap.Plugins.Basic/# Operator/safety/measure steps (Editor pack)
  HardwareTest.OpenTap.Plugins.Visa/ # VISA DMM adapter over IVisaBroker (bench)
  HardwareTest.OpenTap.Plugins.Mixins/# Presentation + Annotation mixins (Editor pack)
plans/opentap/                       # Locked .TapPlan programs + template program TapPackage
docs/adapting.md                     # Productize plans, plugins, station bindings, reports
docs/testing.md                      # UI vs OpenTAP suite separation
docs/appliance-linux.md              # Appliance layout + publish notes
docs/containers.md                   # Local CI tasks, Podman, appliance image rails
tools/ci/                            # Deno CI tasks shared by Actions + local runs
tests/
  HardwareTest.Architecture.Tests/   # Layering smoke (Avalonia/OpenTAP boundaries)
  HardwareTest.Session.Contracts/    # Shared IOpenTapSession contract (real + fake)
  HardwareTest.Tests/                # Core + OpenTAP host unit tests
  HardwareTest.ViewModels.Tests/     # ViewModel unit tests (fakes)
  HardwareTest.E2E.Tests/            # Avalonia Headless UI flows
templates/reports/                   # Typst templates (embedded)
```

**Hard separation:** `HardwareTest.Core` is Avalonia-free and OpenTAP-free. OpenTAP Host and OpenTAP Worker are Avalonia-free. Features call services via explicit DI in `App/Composition.cs` and take focused `IOpenTap*` surfaces — not the aggregating `IOpenTapSession`. Plugins must not reference Avalonia/ScottPlot or call `Ivi.Visa`; Core owns `IVisaBroker`. Operator flow stays in-panel on `MainWindow` (no WinForms/WPF dialogs). Feature files stay under 600 lines. Pause/interaction is per `OpenTapRunContext`, not static on `StepRuntime`. Idle/retention/run-complete use `IClock`; Safety Stop must not wait on NTP. Do not call `TapThread.Abort` in the UI process.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned via `global.json`)
- [Deno](https://deno.land/) 2.x for the shared CI task runner (`tools/ci/`)
- Optional: vendor VISA runtime for real instruments — mock instruments are the default
- Optional: [Podman](https://podman.io/) for Linux container CI / appliance rails — [docs/containers.md](docs/containers.md)

## Build & run

```bash
dotnet build dirs.proj -r win-x64
dotnet run --project src/HardwareTest -c Debug -r win-x64
```

> **RID required:** TypstInterop only restores its native library when a runtime identifier is set.

```bash
# Full CI-shaped matrix (preferred):
deno task --cwd tools/ci all -- --rid win-x64

# Or individual suites:
dotnet test tests/HardwareTest.Architecture.Tests -r win-x64
dotnet test tests/HardwareTest.Tests -r win-x64
dotnet test tests/HardwareTest.ViewModels.Tests -r win-x64
dotnet test tests/HardwareTest.E2E.Tests -r win-x64
```

See [docs/testing.md](docs/testing.md) for UI vs OpenTAP suite separation, plan-shape fixtures, and progress/summary recording.
See [docs/containers.md](docs/containers.md) for Deno tasks, Podman CI image, and appliance stub rails.
See [docs/adapting.md](docs/adapting.md) to replace sample plans, plugins, station bindings, and reports for your product.
## Operator Session / DUT

- Confirm DUT serial once per session (sticky strip on Run shows last activity + idle countdown).
- **Change DUT** clears and blocks Run until re-confirmed.
- Idle uses **last activity** (default 240 minutes); soft-warn then Stale — Same DUT / Change Session.
- Optional station policy: confirm DUT every run (`RequireDutConfirmEveryRun`).
- Technician required UI follows program `requireOperator`.

## OpenTAP programs

Author structure in **OpenTAP Editor** or the free **OpenTAP TUI**; ship locked `.TapPlan` files under `plans/opentap/` (copied to `Programs/` on build). Product plans use **InstrumentComponents.OpenTap** typed instruments/steps plus HardwareTest Mixins Presentation; validate with `HardwareTest.PlanValidate --strict` before bake. Cookbook: [docs/adapting.md](docs/adapting.md#1-author-a-locked-program-cookbook).

## Appliance publish

See [docs/appliance-linux.md](docs/appliance-linux.md). CI `publish-win` / `test-linux` smoke self-contained publishes via Deno tasks.

```bash
deno run -A tools/ci/main.ts publish --rid win-x64
deno run -A tools/ci/main.ts publish --rid linux-x64
```

NativeAOT is **not** a product gate for the OpenTAP host.
