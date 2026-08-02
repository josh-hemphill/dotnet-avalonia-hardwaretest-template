# Platform roadmap (hardening + delivery)

North-star for the **non-OpenTAP** work needed before this shell can be handed to real operators for hands-on testing: repo gates, configuration, diagnostics, crash capture, containerized CI, and code structure.

The [OpenTAP roadmap](opentap-platform.md) tracks *feature* parity with the test-executive. This track covers everything that makes those features **supportable**: knowing what version is running, why a setting has the value it does, what happened when it crashed, and whether a change broke a layering rule.

Related: [adapting.md](adapting.md) (productize), [testing.md](testing.md) (suite separation), [appliance-linux.md](appliance-linux.md) (long-term appliance layout), [deferred/](deferred/) (longer-horizon product plans).

## Two phase tracks

| Track | Folder | Naming | Scope |
| --- | --- | --- | --- |
| OpenTAP integration | [opentap-phases/](opentap-phases/) | Letters (A–K) | Interactions, parameters, mixins, packages, presentation, multi-DUT |
| Platform hardening | [platform-phases/](platform-phases/) | Numbers (1–14) | Gates, config, diagnostics, crash, CI, structure, storage, operator UX |

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
| 5 | [Schema versioning](platform-phases/phase-5-schema-versioning.md) | 4 | Done |
| 6 | [Crash reporting](platform-phases/phase-6-crash-reporting.md) | 3, 4 | Done |
| 7 | [Containerized local CI](platform-phases/phase-7-containers-local-ci.md) | 1 | Done |
| 8 | [Session contract test suite](platform-phases/phase-8-session-contract-tests.md) | 1 | Done |
| 9 | [Run board decomposition](platform-phases/phase-9-runboard-decomposition.md) | 8 | Done |
| 10 | [Export, storage, cleanup, chrome](platform-phases/phase-10-export-storage-chrome.md) | 3, 6, 9 | Done |
| 11 | [Session activity & stale UX](platform-phases/phase-11-session-activity-stale.md) (+ Same DUT / `RequireOperator`) | 9, 10 | Done |
| 12 | [Error surfacing & chrome polish](platform-phases/phase-12-error-surfacing-chrome.md) (+ wayfinding) | 9, 11 | Done |
| 13 | [Settings live semantics](platform-phases/phase-13-settings-live-semantics.md) (`UseMockVisa` honesty) | 3, 10 | Done |
| 14 | [Session façade split](platform-phases/phase-14-session-facade-split.md) | 8, 9 | Planned |

**Suggested order (1–10):** 1 first and alone — nothing else is verifiable until CI actually runs. Then 2 / 7 / 8 can proceed in parallel (independent seams). 3 → 4 → 5 is a chain and should stay one series. 6 lands after 4. 9 after 8. 10 after 3/6/9 (storage + chrome).

**Suggested order (11–14):** Prefer **13 ∥ 11**, then **12**, then **14 before** OpenTAP [Phase K](opentap-phases/phase-k-multi-dut-parallel.md) (multi-DUT). Phase 13 (UseMockVisa honesty) and Phase 11 (session activity / Same DUT) are independent; Phase 12 depends on 11 for session-banner hierarchy.

**Fresh-eyes review:** Findings (idle/Same DUT/`RequireOperator`, Status/async chrome, UseMockVisa split-brain, wayfinding, doc drift) are mapped in [platform-phases/review-remediation.md](platform-phases/review-remediation.md). Overlaps with session work are **absorbed into Phase 11**; chrome/async into **12**; VISA honesty into **13**.

## Deferred (detailed plans — do not implement yet)

Longer-horizon product work lives under [`docs/deferred/`](deferred/). Each file has Goal, Locked decisions, Workstreams, Exit criteria, and Out of scope.

| Plan | Topic |
| --- | --- |
| [Run comparison](deferred/deferred-run-comparison.md) | Replace `StubRunComparisonService` |
| [Appliance kiosk](deferred/deferred-appliance-kiosk.md) | systemd / kiosk / image bake |
| [Package feed install](deferred/deferred-package-feed-install.md) | In-app OpenTAP feed install |
| [Bench profile UI](deferred/deferred-bench-profile-ui.md) | Full ComponentSettings / bench-profile editor |
| [Schema migration](deferred/deferred-schema-migration.md) | General schema migration engine |
| [Remote crash upload](deferred/deferred-remote-crash-upload.md) | Additive uploader on local dossiers |
| [Localization](deferred/deferred-localization.md) | Multi-language operator UI |
| [Auto-update](deferred/deferred-auto-update.md) | Delivery / update channel |
| [Clock discipline](deferred/deferred-clock-discipline.md) | NTP / skew detection for run history |

## Known gaps

Real, acknowledged, and deliberately unscheduled (or scheduled as phases above). Revisit before the first unattended deployment.

- **No clock discipline.** Timestamps are `DateTimeOffset.UtcNow` with no NTP sync or skew detection — see [deferred-clock-discipline.md](deferred/deferred-clock-discipline.md).
- **`IOpenTapSession` is a fat façade.** Phase 8 pins its behavior; splitting is [Phase 14](platform-phases/phase-14-session-facade-split.md) (unblocks multi-DUT).
- **Vendor VISA in CI is still unproven.** Discovery now surfaces failures (no silent empty list); real IVI runtimes remain outside the default CI matrix.
- **Operator session still confirm-clock based.** Activity-aware idle, soft-warn, Same DUT / `RequireOperator` fixes — [Phase 11](platform-phases/phase-11-session-activity-stale.md).
- **UseMockVisa can diverge from DI factories after save.** Honest rebuild or refuse — [Phase 13](platform-phases/phase-13-settings-live-semantics.md).
- **Errors / async UI / Run chrome / Home wayfinding.** [Phase 12](platform-phases/phase-12-error-surfacing-chrome.md) — **Done**.
- **Finding → phase map.** [platform-phases/review-remediation.md](platform-phases/review-remediation.md).
