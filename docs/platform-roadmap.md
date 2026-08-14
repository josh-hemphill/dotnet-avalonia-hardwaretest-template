# Platform roadmap (hardening + delivery)

North-star for the **non-OpenTAP** work needed before this shell can be handed to real operators for hands-on testing: repo gates, configuration, diagnostics, crash capture, containerized CI, and code structure.

The [OpenTAP roadmap](opentap-platform.md) tracks *feature* parity with the test-executive. This track covers everything that makes those features **supportable**: knowing what version is running, why a setting has the value it does, what happened when it crashed, and whether a change broke a layering rule.

Related: [adapting.md](adapting.md) (productize), [testing.md](testing.md) (suite separation), [appliance-linux.md](appliance-linux.md) (long-term appliance layout), [deferred/](deferred/) (longer-horizon product plans).

## Two phase tracks

| Track | Folder | Naming | Scope |
| --- | --- | --- | --- |
| OpenTAP integration | [opentap-phases/](opentap-phases/) | Letters (A–L) | Interactions, parameters, mixins, packages, presentation, multi-DUT, authoring |
| Platform hardening | [platform-phases/](platform-phases/) | Numbers (1–25) | Gates, config, diagnostics, crash, CI, structure, storage, operator UX, live presentation, shell strip, touch density, correctness, supply chain, VISA, safety worker, session split, clock |

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
| 1 | [Repo gates + green CI](platform-phases/phase-1-repo-gates.md) | — | Done — format gate repaired in [Phase 19](platform-phases/phase-19-immediate-correctness.md); lockfiles/pins/audit in [Phase 20](platform-phases/phase-20-ci-honesty.md) |
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
| 14 | [Session façade split](platform-phases/phase-14-session-facade-split.md) | 8, 9 | Done |
| 15 | [Operator feedback & Settings chrome](platform-phases/phase-15-operator-feedback-chrome.md) | 12, 13 | Done |
| 16 | [Band board & Focus trend](platform-phases/phase-16-band-focus-presentation.md) | 15, J, L | Done |
| 17 | [Shell notification strip & layout-shift hygiene](platform-phases/phase-17-shell-notification-strip.md) | 12, 15, 16 | Done |
| 18 | [Operator touch density floor](platform-phases/phase-18-operator-touch-density.md) | 17 | Done |
| 19 | [Immediate correctness](platform-phases/phase-19-immediate-correctness.md) | 18 | Done |
| 20 | [CI honesty & supply chain](platform-phases/phase-20-ci-honesty.md) | 19 | Done |
| 21 | [Operator chrome & accessibility](platform-phases/phase-21-operator-chrome-a11y.md) | 18, 19 | Done |
| 22 | [VISA broker unification](platform-phases/phase-22-visa-broker.md) | 13, 19 | Planned |
| 23 | [Safety Stop + OpenTAP worker](platform-phases/phase-23-safety-opentap-worker.md) | 19, 22 | Planned |
| 24 | [OpenTAP session decomposition](platform-phases/phase-24-session-decomposition.md) | 14 | Planned — prerequisite for OpenTAP K |
| 25 | [Clock discipline](platform-phases/phase-25-clock-discipline.md) | 11 | Planned — promoted from deferred |

**Suggested order (1–10):** 1 first and alone — nothing else is verifiable until CI actually runs. Then 2 / 7 / 8 can proceed in parallel (independent seams). 3 → 4 → 5 is a chain and should stay one series. 6 lands after 4. 9 after 8. 10 after 3/6/9 (storage + chrome).

**Suggested order (11–16):** Prefer **13 ∥ 11**, then **12**, then **14 before** OpenTAP [Phase K](opentap-phases/phase-k-multi-dut-parallel.md) (multi-DUT), then **15** (first-impression feedback + Settings chrome) once 12/13 foundations exist. Phase 13 (UseMockVisa honesty) and Phase 11 (session activity / Same DUT) are independent; Phase 12 depends on 11 for session-banner hierarchy. **16** (Band board + Focus trend) after 15 and OpenTAP [Phase L](opentap-phases/phase-l-presentation-authoring.md) (L may share a PR train with 16). Round-2 correctness items in [review-post-phase-15.md](platform-phases/review-post-phase-15.md) should not be blocked by 16, and the `OpenTapSession` run guard remains a **prerequisite** for Phase K.

**Suggested order (17–18):** **17 then 18** (or a short shared PR train once 17’s strip host exists). Shell strip stops notification Auto-rows from shoving the board; touch density then lands on a calmer layout. Full kiosk image bake stays [deferred](deferred/deferred-appliance-kiosk.md) and assumes Phase 18’s floor.

**Suggested order (19–25):** **19 first** (contained correctness + format gate). Then **20 ∥ 21**. **22 before 23** (plan VISA must be preemptable before a killable worker is useful). **24** can overlap 22/23 on the Host types but is the structural prerequisite for OpenTAP K. **25** before the first unattended deployment; it does not block 19–24.

**Fresh-eyes reviews:**

- **Round 1 (pre–Phase 11):** [platform-phases/review-remediation.md](platform-phases/review-remediation.md) — routed into Phases 11–15, all implemented.
- **Round 2 (post–Phase 15):** [platform-phases/review-post-phase-15.md](platform-phases/review-post-phase-15.md) — F1–F6 (stale DI `AppSettings`, off-UI-thread mutation, run gate, Safety Stop, path/atomic storage) are fixed; CI follow-ups closed in [Phase 19](platform-phases/phase-19-immediate-correctness.md) (format gate) and [Phase 20](platform-phases/phase-20-ci-honesty.md) (lockfiles, audit, Action pins).
- **Round 3 (post–Phase 18):** [platform-phases/review-round-3.md](platform-phases/review-round-3.md) — keyboard, remaining UI-thread, PDF/export, plugin trust, Stop copy, chips/Settings names, slnx/format → Phase 19; CI honesty → 20; chrome/a11y → 21; VISA broker → 22; safety worker → 23; session split → 24; clock → 25.
- **Live presentation:** Squashed charts / band-first + earned Focus → [Phase 16](platform-phases/phase-16-band-focus-presentation.md) + OpenTAP [Phase L](opentap-phases/phase-l-presentation-authoring.md).
- **Touch + layout shift:** Reserved shell strip + Run height caps → [Phase 17](platform-phases/phase-17-shell-notification-strip.md); operator hit-target floor → [Phase 18](platform-phases/phase-18-operator-touch-density.md).

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
| [Clock discipline](deferred/deferred-clock-discipline.md) | NTP / skew detection — **promoted to [Phase 25](platform-phases/phase-25-clock-discipline.md)** |

## Known gaps

Real, acknowledged, and deliberately unscheduled (or scheduled as phases above). Revisit before the first unattended deployment.

- **Clock discipline is scheduled.** Timestamps are still `DateTimeOffset.UtcNow` until [Phase 25](platform-phases/phase-25-clock-discipline.md). Every idle/stale decision, run ordering, and retention prune depends on it.
- **Vendor VISA in CI is still unproven.** Discovery now surfaces failures (no silent empty list); real IVI runtimes remain outside the default CI matrix. The OpenTAP `VisaDmmInstrument` also opens IVI directly rather than through the Core `VisaSessionGate` — [Phase 22](platform-phases/phase-22-visa-broker.md).
- **Phase 1 / round-2 CI follow-ups.** Format gate is repaired in [Phase 19](platform-phases/phase-19-immediate-correctness.md). Lock files, vulnerability scan, coverage split, and Action pins shipped in [Phase 20](platform-phases/phase-20-ci-honesty.md).
- **Finding → phase maps.** [review-remediation.md](platform-phases/review-remediation.md) (round 1, closed), [review-post-phase-15.md](platform-phases/review-post-phase-15.md) (round 2 — F1–F6 fixed; CI follow-ups still open), [review-round-3.md](platform-phases/review-round-3.md) (round 3 → Phases 19–25).
- **Safety Stop is cooperative until Phase 23.** Phase 19 only corrects operator copy to **Stop Run**. Real interlocks + killable OpenTAP worker are [Phase 23](platform-phases/phase-23-safety-opentap-worker.md). Do not use `TapThread.Abort`.
- **Live charts squashed; band-first + earned Focus trend.** [Phase 16](platform-phases/phase-16-band-focus-presentation.md); authoring maintainability [Phase L](opentap-phases/phase-l-presentation-authoring.md).
- **Notification Auto-rows shift the work surface; touch targets undersized.** [Phase 17](platform-phases/phase-17-shell-notification-strip.md) (reserved shell strip) → [Phase 18](platform-phases/phase-18-operator-touch-density.md) (hit-target floor). Full kiosk bake remains [deferred](deferred/deferred-appliance-kiosk.md).
