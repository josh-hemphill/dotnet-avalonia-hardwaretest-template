# Platform roadmap (hardening + delivery)

North-star for the **non-OpenTAP** work needed before this shell can be handed to real operators for hands-on testing: repo gates, configuration, diagnostics, crash capture, containerized CI, and code structure.

The [OpenTAP roadmap](opentap-platform.md) tracks *feature* parity with the test-executive. This track covers everything that makes those features **supportable**: knowing what version is running, why a setting has the value it does, what happened when it crashed, and whether a change broke a layering rule.

Related: [adapting.md](adapting.md) (productize), [testing.md](testing.md) (suite separation), [appliance-linux.md](appliance-linux.md) (long-term appliance layout).

## Two phase tracks

| Track | Folder | Naming | Scope |
| --- | --- | --- | --- |
| OpenTAP integration | [opentap-phases/](opentap-phases/) | Letters (A–J) | Interactions, parameters, mixins, packages, presentation |
| Platform hardening | [platform-phases/](platform-phases/) | Numbers (1–9) | Gates, config, diagnostics, crash, CI, structure |

Distinct namespaces on purpose — "Phase C" and "Phase 3" are never the same thing.

## Locked product decisions

| Topic | Decision |
| --- | --- |
| Immediate product | **Windows desktop program.** Ship and demo on `win-x64` first. |
| Linux appliance | **Committed long-term target, not the current gate.** Keep the code portable and prove `linux-x64` in CI; defer image/OS integration. |
| Containers | **Dev + CI tooling first.** Podman, quadlets over compose. Appliance images ride the same rails later. |
| Local CI parity | Devs may opt into running the CI matrix locally in containers. Workflow and local runs share **one** task definition — no drift. |
| Line endings | **LF everywhere**, enforced by `.gitattributes` (not just `.editorconfig`). |
| License | **MIT.** |
| Architecture tests | **Smoke level.** Encode the layering rules already written in prose; do not build an exhaustive rule engine. |
| Schema versioning | **Stamp and gate now.** A general migration engine is deferred — the read path must fail loudly rather than silently coerce. |
| Crash reporting | **Local dossier first, offline.** No network upload, no third-party SDK. Keep the format stable so an uploader is additive. |
| Config precedence | defaults → `settings.json` → environment → command line. Every effective value carries **provenance**. |

## Cross-cutting rules (every phase)

- **Safety outranks diagnostics.** Any failure path that can run while a plan is executing must attempt a safe stop *before* it does bookkeeping.
- **No new operator-facing dialogs.** The [appliance rule](opentap-platform.md#interaction-contract-avalonia-owned) holds: no `Window`, no OS modal, no message box for operator flow. Diagnostics surfaces are in-panel.
- **No reflection-based configuration binding.** `JsonSerializerIsReflectionEnabledByDefault=false` is set repo-wide; keep new config/serialization paths source-generated or hand-written.
- **`HardwareTest.Core` stays Avalonia-free and OpenTAP-free.** Phase 2 makes this executable.
- Update [testing.md](testing.md) when a phase adds a suite or a fixture convention.
- Check off the phase row below when the plan ships.

## Phase checklist

| Phase | Plan | Depends on | Status |
| --- | --- | --- | --- |
| 1 | [Repo gates + green CI](platform-phases/phase-1-repo-gates.md) | — | In progress |
| 2 | [Architecture compliance tests](platform-phases/phase-2-architecture-tests.md) | 1 | Done |
| 3 | [Configuration & environment model](platform-phases/phase-3-configuration-model.md) | 1 | Done |
| 4 | [Build & system info surface](platform-phases/phase-4-build-info.md) | 3 | Done |
| 5 | [Schema versioning](platform-phases/phase-5-schema-versioning.md) | 4 | Not started |
| 6 | [Crash reporting](platform-phases/phase-6-crash-reporting.md) | 3, 4 | Not started |
| 7 | [Containerized local CI](platform-phases/phase-7-containers-local-ci.md) | 1 | Not started |
| 8 | [Session contract test suite](platform-phases/phase-8-session-contract-tests.md) | 1 | Not started |
| 9 | [Run board decomposition](platform-phases/phase-9-runboard-decomposition.md) | 8 | Not started |

**Suggested order:** 1 first and alone — nothing else is verifiable until CI actually runs. Then 2 / 7 / 8 can proceed in parallel (independent seams). 3 → 4 → 5 is a chain and should stay one series. 6 lands after 4. 9 last, because it is the largest refactor and wants 8's safety net underneath it.

## Deferred (do not implement yet)

- Remote crash/telemetry upload, third-party crash SDKs.
- A general schema migration engine or data-conversion tooling.
- Appliance OS integration: systemd units, kiosk session, image bake automation.
- Auto-update / delivery channel.
- Run-history retention and disk-quota policy (tracked, unplanned — see [Known gaps](#known-gaps)).
- Localization / multi-language operator UI.

## Known gaps

Real, acknowledged, and deliberately unscheduled. Revisit before the first unattended deployment.

- **`runs/` grows without bound.** App logs rotate at 14 days ([`LoggingBootstrap`](../src/HardwareTest.Core/Logging/LoggingBootstrap.cs)); run folders, PDFs, and CSV exports do not. No free-space check anywhere.
- **No clock discipline.** Timestamps are `DateTimeOffset.UtcNow` with no NTP sync or skew detection — a bench with a drifted RTC produces misordered run history.
- **`IOpenTapSession` is a 29-member god interface.** Phase 8 pins its behavior; splitting it is a later decision.
- **Real vendor VISA paths are untested.** [`IviVisaResourceDiscovery`](../src/HardwareTest.Core/Hardware/VisaResourceDiscovery.cs) swallows every exception into an empty list with no log line.
