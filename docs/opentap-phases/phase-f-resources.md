# Phase F — Resource / VisaAddress alignment

**Parent:** [opentap-platform.md](../opentap-platform.md)  
**Depends on:** [Phase C](phase-c-parameters.md) helpful but not strictly required  
**Unblocks:** third-party SCPI plugins feeling native on Instruments

## Goal

Tighten Instruments and Host binding to OpenTAP resource conventions (`VisaAddress` / SCPI), keep Avalonia Instruments page, document ComponentSettings as future.

## Locked rules

- Instruments UI stays Avalonia (no OpenTAP resource manager window).
- Keep `ResourceName` fallback ([`InstrumentResourceAccess`](../../src/HardwareTest.OpenTap.Host/InstrumentResourceAccess.cs)).

## Work items

1. **Lifecycle:** where safe, Open/Close instruments around plan run (or document OpenTAP’s own open behavior and avoid double-open).

2. **Binding:** prefer writing `VisaAddress` when present; document SCPI instrument expectations for adopters.

3. **MockDmm:** optionally add `VisaAddress` alias or dual property for demo parity — only if it clarifies adapters without breaking sample.

4. **Docs:** adapting + opentap-platform — resource binding story; ComponentSettings / bench profiles noted as deferred full UI.

5. **Tests:** sample bind + run still green; slot override still applies.

## Exit criteria

- Clear documented path for a third-party SCPI instrument TapPlan + slot override.
- No regression on MockDmm sample.

## Out of scope

- Full ComponentSettings editor.
- Multi-resource Parallel benches.
