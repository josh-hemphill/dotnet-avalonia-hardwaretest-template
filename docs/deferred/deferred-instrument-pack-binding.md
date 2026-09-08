# Deferred — Remaining instrument-pack binding

**Parent:** [opentap-platform.md](../opentap-platform.md) · [adapting.md](../adapting.md)
**Status:** Partially landed — authoring consume path (session injection + cookbook). Remaining: retarget in-repo demos.
**Related:** [phase-22-visa-broker.md](../platform-phases/phase-22-visa-broker.md) (Done — `IVisaBroker`) · [deferred-bench-profile-ui.md](deferred-bench-profile-ui.md) · [OpenTAP pack guide](https://josh-hemphill.github.io/instrument-components/csharp/opentap/)

## Goal

Consume **InstrumentComponents.OpenTap** so product instruments, SCPI maps, and protocol mocks live outside this shell. HardwareTest keeps process VISA coordination (`IVisaBroker` / `VisaSessionGate`), operator prompts, Presentation, sidecar, and Run UI.

This repo does **not** implement that library.

## Landed

- Host registers `OpenTapScpiIo.Provider` from `IVisaBroker` when the library pack is loaded (no compile-time package reference; the pack still must not call IVI).
- `PlanContractValidator` / Run Selected recognize library *Identity Query* and *Safe Shutdown* by type name. Library identity does not require `HardwareDut`.
- Cookbook: product plans use typed library instruments/steps; Basic remains operator chrome + CI demos.
- Presentation mixin default `DisplayRole` is `scalar` (band-first). Demos that need waveforms still set `timeseries` explicitly.

## Locked decisions

### This repo (HardwareTest)

- **Consume** the library OpenTAP pack (`tap package install` / bake). Product program packs list it in `.TapPackage` Dependencies — not in `{planId}.program.json`. The template pack does not declare it (sample stays Basic).
- Keep a **thin** Mock/Visa wrapper only while in-repo demos still need `HardwareDmm` / `IDmmInstrument`. Do not grow `IDmmInstrument`.
- `IdentityCheckStep` and `SafeShutdownStep` in Basic stay for demos. Product plans use the library step types. Identity/SafeShutdown should eventually bind `OpenTap.Instrument` + library capabilities if demos are retargeted.
- **One OpenTAP `Instrument` per physical device.** Nested capabilities are methods or `[EmbedProperties]` on that instrument — not extra Instrument slots.
- `IVisaBroker` stays here. Plugins still must not call `Ivi.Visa`.
- The library must **not** depend on `HardwareTest.Core`.

### The visa/SCPI library (separate repo)

- Capability APIs, SCPI maps, protocol mocks.
- OpenTAP pack: `Instrument` + writable `VisaAddress` + `IDeviceDiscovery`.
- Nested capabilities as methods / `[EmbedProperties]`.
- Function steps publish Phase I `Sample` / `Scalar`. Presentation mixins, sidecar, operator dialogs, and `IVisaBroker` stay in HardwareTest.

## Remaining workstreams

1. Thin `Plugins.Visa` wrapper around library instruments for demos (or drop demo `VisaDmmInstrument` once sample/board-demo load library types).
2. Retarget Identity / SafeShutdown / demo measure steps from `HardwareDmm` to `OpenTap.Instrument` + library capabilities (or freeze `IDmmInstrument`).
3. Drop or freeze demo `MockDmmInstrument` once the library protocol mock covers sample/board-demo.
4. Architecture tests: library assembly not referenced by Core; Basic still Core-free; plugins still have no `Ivi.Visa`.

## Exit criteria

- [x] A product plan can depend on the library pack, validate `--strict` (Identity Query / Safe Shutdown recognized), and rebind `VisaAddress` on Instruments
- [x] Plan VISA I/O for library instruments goes through `IVisaBroker` / `VisaSessionGate` when the pack is loaded
- [ ] In-repo demos no longer grow `IDmmInstrument`
- [ ] Library has no `HardwareTest.Core` / Presentation / sidecar / operator-dialog dependency (enforced in the library repo)

## Out of scope

- Implementing the visa/SCPI library in this repository
- Avalonia instrument type editor or SCPI map UI
- Multiple OpenTAP `Instrument` instances per device
- In-app package feed install ([deferred-package-feed-install.md](deferred-package-feed-install.md))

## Dependencies

- Phase 22 `IVisaBroker` (Done)
- Authoring packs Basic + Mixins (landed)
- Template program pack cookbook ([adapting.md](../adapting.md#1-author-a-locked-program-cookbook))
- [instrument-components](https://github.com/josh-hemphill/instrument-components) OpenTAP pack
