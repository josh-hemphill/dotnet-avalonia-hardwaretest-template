# Phase F — Resource / VisaAddress alignment

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** [Phase C](phase-c-parameters.md) helpful but not strictly required  
**Unblocks:** third-party SCPI plugins feeling native on Instruments  
**Status:** Done

## Goal

Tighten Instruments and Host binding to OpenTAP resource conventions (`VisaAddress` / SCPI), keep Avalonia Instruments page, document ComponentSettings as future.

## Locked rules

- Instruments UI stays Avalonia (no OpenTAP resource manager window).
- Keep `ResourceName` fallback ([`InstrumentResourceAccess`](../../src/HardwareTest.OpenTap.Host/InstrumentResourceAccess.cs)).
- Host does **not** call `Instrument.Open`/`Close` around runs (OpenTAP opens resources during plan execution).

## Implementation

1. **Binding:** `InstrumentResourceAccess` prefers `VisaAddress`, then `ResourceName`, then `Address`.
2. **MockDmm:** `VisaAddress` and `ResourceName` share one backing field for demo parity.
3. **Docs:** adapting SCPI recipe + platform lifecycle note; ComponentSettings remains deferred.

## Exit criteria

- Clear documented path for a third-party SCPI instrument TapPlan + slot override.
- No regression on MockDmm sample.

## Out of scope

- Full ComponentSettings editor.
- Multi-resource Parallel benches.
