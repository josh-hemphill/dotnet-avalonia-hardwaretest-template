# Avalonia Hardware Test Template

Avalonia 12 desktop shell for **OpenTAP-sequenced hardware tests**, **IVI VISA discovery**, **ScottPlot live plots**, and **Typst PDF reports**. Self-contained (non-NativeAOT) publishes target sealed Linux appliance images.

## Layout

```
src/
  HardwareTest/                      # Avalonia exe — App / Features / Widgets
  HardwareTest.Core/                 # Avalonia-free: logging, settings, VISA, runs, reporting
  HardwareTest.OpenTap.Host/         # OpenTAP session façade (load / run / pause / abort)
  HardwareTest.OpenTap.Plugins.Basic/# Instruments, DUT, sample TestSteps
plans/opentap/                       # Locked .TapPlan programs
docs/appliance-linux.md              # Appliance layout + publish notes
docs/opentap-platform.md             # OpenTAP shell roadmap + phase checklist
docs/opentap-phases/                 # Incremental implementation plans (A–H)
tests/
  HardwareTest.Tests/                # Core + OpenTAP host unit tests
  HardwareTest.ViewModels.Tests/     # ViewModel unit tests (fakes)
  HardwareTest.E2E.Tests/            # Avalonia Headless UI flows
templates/reports/                   # Typst templates (embedded)
```

**Hard separation:** `HardwareTest.Core` has zero Avalonia references. OpenTAP Host is Avalonia-free. Features call services via explicit DI in `App/Composition.cs`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned via `global.json`)
- Optional: vendor VISA runtime for real instruments — mock instruments are the default

## Build & run

```bash
dotnet build dirs.proj -r win-x64
dotnet run --project src/HardwareTest -c Debug -r win-x64
```

> **RID required:** TypstInterop only restores its native library when a runtime identifier is set.

```bash
dotnet test tests/HardwareTest.Tests -r win-x64
dotnet test tests/HardwareTest.ViewModels.Tests -r win-x64
dotnet test tests/HardwareTest.E2E.Tests -r win-x64
```

See [docs/testing.md](docs/testing.md) for UI vs OpenTAP suite separation, plan-shape fixtures, and progress/summary recording.
See [docs/adapting.md](docs/adapting.md) to replace sample plans, plugins, station bindings, and reports for your product.
See [docs/opentap-platform.md](docs/opentap-platform.md) for the OpenTAP integration roadmap (interactions, parameters, mixins, packages) and [incremental phase plans](docs/opentap-phases/).
## Operator Session / DUT

- Confirm DUT serial once per session (sticky strip on Run).
- **Change DUT** clears and blocks Run until re-confirmed.
- Idle timeout (default 4h) or program-family mismatch marks session **Stale** — Same DUT / Change DUT.
- Do **not** re-prompt on every successful Run while Active.

## OpenTAP programs

Author structure in **OpenTAP Editor**; ship locked `.TapPlan` files under `plans/opentap/` (copied to `Programs/` on build). Avalonia supports constrained Engineer/Debug overlays (enable/disable, limits, resource rebind) without mutating the golden plan.

## Appliance publish

See [docs/appliance-linux.md](docs/appliance-linux.md). CI `publish-appliance` smokes self-contained `win-x64` with `PublishAot=false`.

```bash
dotnet publish src/HardwareTest -c Release -r win-x64 --self-contained -p:PublishAot=false
dotnet publish src/HardwareTest -c Release -r linux-x64 --self-contained -p:PublishAot=false
```

NativeAOT is **not** a product gate for the OpenTAP host.
