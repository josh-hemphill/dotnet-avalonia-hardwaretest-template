# Deferred — Instrument pack binding (visa/SCPI library)

**Parent:** [opentap-platform.md](../opentap-platform.md) · [adapting.md](../adapting.md)
**Status:** Deferred (library pack does not exist yet)
**Related:** [phase-22-visa-broker.md](../platform-phases/phase-22-visa-broker.md) (Done — `IVisaBroker`) · [deferred-bench-profile-ui.md](deferred-bench-profile-ui.md)

## Goal

Consume a **separate visa/SCPI instrument library** as an OpenTAP pack so product instruments, SCPI maps, and protocol mocks live outside this shell. HardwareTest keeps process VISA coordination (`IVisaBroker` / `VisaSessionGate`), operator/safety **step types**, Presentation, sidecar, and Run UI.

This repo does **not** implement that library.

## Why this is deferred

Typed instrument functions belong in the library, not here. Growing `IDmmInstrument` / `VisaDmmInstrument` / `MockDmmInstrument` into a product instrument catalog would fork SCPI maps into the template and drag Core into Editor packs. The library pack does not exist yet, so binding is documented rather than coded.

## Locked decisions

### This repo (HardwareTest)

- **Consume** the library OpenTAP pack when it exists (`tap package install` / bake). Program packs list it in `.TapPackage` Dependencies — not in `{planId}.program.json`.
- Keep a **thin** Mock/Visa wrapper only while in-repo demos still need `HardwareDmm` / `IDmmInstrument`. Do not grow `IDmmInstrument`.
- `IdentityCheckStep` and `SafeShutdownStep` stay HardwareTest **step types** (operator/safety contract, DUT stamping). They should eventually bind `OpenTap.Instrument` plus library identity/shutdown **capabilities**, not a HardwareTest-only DMM interface.
- **One OpenTAP `Instrument` per physical device.** Nested capabilities are methods or `[EmbedProperties]` on that instrument — not extra Instrument slots.
- `IVisaBroker` stays here (process coordination, mock/real mode, Safety Stop preemption). The host supplies an `IVisaSession` from `IVisaBroker.OpenAsync` (`WriteAsync` / `QueryAsync`) to the thin in-repo wrapper around the library instrument. Plugins still must not call `Ivi.Visa`.
- The library must **not** depend on `HardwareTest.Core`.

### The visa/SCPI library (separate repo)

- Capability APIs, SCPI maps, protocol mocks.
- Optional OpenTAP pack: `Instrument` + writable `VisaAddress` + `IDeviceDiscovery` so Instruments can rebind and Discover OpenTAP lists addresses.
- Nested capabilities as methods / `[EmbedProperties]`.
- If the library ships function steps, they publish Phase I `Sample` / `Scalar`. Presentation mixins, sidecar, operator dialogs, and `IVisaBroker` stay in HardwareTest.

## Workstreams (when the library exists)

1. Add the library pack as a program-pack Dependency; bake it onto the appliance with Basic + Mixins.
2. Thin `Plugins.Visa` wrapper: construct library instrument with an `IVisaSession` from `IVisaBroker.OpenAsync` (`WriteAsync` / `QueryAsync`). Keep `VisaAddress` on the OpenTAP Instrument.
3. Retarget Identity / SafeShutdown / demo measure steps from `HardwareDmm` to `OpenTap.Instrument` + library capabilities (or a narrow adapter that does not grow `IDmmInstrument`).
4. Drop or freeze demo `MockDmmInstrument` once the library protocol mock covers sample/board-demo.
5. Architecture tests: library assembly not referenced by Core; Basic still Core-free; plugins still have no `Ivi.Visa`.

## Exit criteria

- [ ] A product plan can depend on the library pack, validate `--strict`, and rebind `VisaAddress` on Instruments
- [ ] Plan VISA I/O still goes through `IVisaBroker` / `VisaSessionGate`
- [ ] Library has no `HardwareTest.Core` / Presentation / sidecar / operator-dialog dependency
- [ ] `IDmmInstrument` is not extended with new product functions

## Out of scope

- Implementing the visa/SCPI library in this repository
- Avalonia instrument type editor or SCPI map UI
- Multiple OpenTAP `Instrument` instances per device
- In-app package feed install ([deferred-package-feed-install.md](deferred-package-feed-install.md))

## Dependencies

- Phase 22 `IVisaBroker` (Done)
- Authoring packs Basic + Mixins (landed)
- Template program pack cookbook ([adapting.md](../adapting.md#1-author-a-locked-program-cookbook))
- External visa/SCPI library pack (not in this repo)
