# Operator persona review evidence and votes

Contract: [operator-persona-workflows.md](operator-persona-workflows.md), version 1.
This review extends operator PR #241 on `fix/operator-ux-voting-review`.
No authoring review or merge is part of this change.

Graphical analysis follows Anthropic's
[frontend-design skill](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md)
and the established Fluent/Inter task hierarchy described in
[the operator graphical review](operator-ux-review.md#design-guidance). Reviewers
inspect rendered workflows, focus and recovery as well as source behavior.

## Independent definition gate

Three reviewers independently proposed workflows before reading the synthesized
contract. All converged on five task personas, with keyboard/enlarged text across
roles. They accepted version 1 before scoring the implementation:

| Reviewer | D1 | D2 | D3 | D4 | D5 | D6 | D7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A | 95 | 96 | 95 | 96 | 97 | 94 | 94 |
| B | 95 | 94 | 94 | 95 | 96 | 94 | 93 |
| C | 95 | 94 | 95 | 95 | 96 | 94 | 93 |

All 21 definition cells exceed 90, with no missing critical journey. This is a
vote on definition sufficiency, not app acceptance. The contract remains frozen
through implementation; requirements are not weakened to improve scores.

## Original implementation baseline

Reviewers inspected original commit `5b8fba0`, the original 148 PNGs and source.
They excluded unvalidated working-tree fixes and did not consult other votes.
Each row contains C1 completion, C2 discoverability, C3 efficiency, C4 recovery,
C5 integrity, C6 access/space and C7 feedback/handoff.

### Reviewer A

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 | 88 | 84 | 89 | 91 | 87 | 92 | 86 |
| P2 | 88 | 86 | 88 | 92 | 86 | 92 | 87 |
| P3 | 80 | 82 | 84 | 78 | 65 | 91 | 83 |
| P4 | 72 | 91 | 85 | 81 | 60 | 92 | 88 |
| P5 | 86 | 89 | 89 | 86 | 75 | 91 | 87 |

### Reviewer B

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 | 89 | 82 | 88 | 91 | 89 | 91 | 84 |
| P2 | 89 | 88 | 88 | 91 | 89 | 91 | 86 |
| P3 | 79 | 84 | 84 | 72 | 66 | 91 | 79 |
| P4 | 68 | 91 | 82 | 78 | 63 | 91 | 80 |
| P5 | 79 | 86 | 85 | 87 | 73 | 91 | 82 |

### Reviewer C

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 | 84 | 80 | 88 | 91 | 82 | 91 | 84 |
| P2 | 85 | 82 | 88 | 91 | 83 | 91 | 84 |
| P3 | 69 | 78 | 82 | 63 | 48 | 90 | 79 |
| P4 | 54 | 86 | 88 | 65 | 45 | 88 | 82 |
| P5 | 69 | 88 | 88 | 78 | 52 | 91 | 85 |

All reviewers rejected the baseline. The prior page-level graphical scores did
not establish persona compliance. Critical findings were silently reset export
destinations, delayed outgoing badge responses crossing session boundaries,
wrong-program IDN evidence, and unrecorded selected/full execution scope.
Further gaps involved ordinary operator context, direct exact-result access,
compact signing controls, and proof of handoff and saved commissioning reload.

## Evidence ledger

The following ledger maps individual acceptance branches to checks. A test name
identifies evidence to run; it is not a claim of a passing result until validation
and frozen-candidate votes below confirm it. Graphical evidence uses actual views
and service commands; credential readers and instruments are explicitly mocked.

| Acceptance branch | Evidence/check |
| --- | --- |
| P1.1 assigned program, missing-program escalation, X1 stable identity/backend | Home getting-started instructions; `OperatorContextTests` effective-controller tests; `OperatorContextWorkflowTests.Equal_program_names_have_distinct_identity_and_full_keyboard_selectable_details` with actual dropdown/native item names and selectable popup |
| P1.1 required fields, scanner suffixes, no automatic execution | `OperatorWorkflowUiTests.Required_identity_errors_focus_the_exact_field_and_clear_after_keyboard_correction`; `RunBoardChildViewModelTests` session confirmation; `OperatorPersonaJourneyTests` actual keyboard entry/Confirm |
| P1.1 missing/unloadable assigned program, correction/retry | `OperatorProgramAvailabilityTests` explicitly prevents both execution scopes from using the previous loaded plan, serializes rapid selection, retains input/prior evidence and recovers; four actual `OperatorContextWorkflowTests.Missing_assigned_program_blocks_old_plan_execution_and_keeps_identity_and_evidence_for_reselection` cases show the disabled Run, Reload/escalation, absent selection and correction without execution |
| P1.1 stricter program or live badge policy after confirmation | `OperatorRequirementTransitionTests` same/different family and newly required operator/badge; `OperatorRequirementTransitionUiTests` actual Run command refusal, retained inputs, typed/badge-present but unconfirmed block, full explicit confirmation, prior-session Change action |
| P1.1 badge unavailable/policy boundary | `RunBoardChildViewModelTests.SessionPanel_tap_fills_technician_and_required_badge_blocks_confirm`, focus-probe failure and Same DUT badge-policy cases |
| P1.1/P5.2 health block/warn/override and safe return | `RunBoardChildViewModelTests.RunExecution_blocks_stale_station_health_when_sidecar_blocks`, warn/health-program cases; `RunTestViewModelTests.Block_gate_disables_start_and_override_off_restores_it`; core StationHealth gate tests |
| P1.2 prompt validation, retained values, focus and resize | `OperatorValidationRecoveryTests`; `OperatorWorkflowUiTests.Long_required_prompt_retains_invalid_input_and_keeps_Continue_outside_its_scroller`; real long-prompt/invalid-number captures |
| P1.2 complete/pass/fail/stop and exact RunId navigation | Existing `RunFlowE2ETests`; `OperatorContextWorkflowTests.Completed_mock_run_exposes_a_reachable_action_opening_its_exact_saved_record`; real failed/stopped captures; cross-page Stop persona journey |
| P2.1 successive runs/Same DUT/every-run policy | `OperatorPersonaJourneyTests` four actual two-job journeys; `RunTestViewModelTests.Second_run_same_dut_does_not_require_reconfirm`, stale/every-run cases |
| P2.2 full versus selected scope persisted/reloaded/listed | `RunExecutionScopeTests`; new `RunTestViewModelTests` persisted full/selected cases; actual selected-step completed/result captures |
| P2.2 scope retained in exported/generated reports | `TypstReportServiceTests.Generated_report_preserves_execution_scope_in_actual_pdf` extracts text from six actual PDFs; selected/unknown certification never says full-suite certified pass; export diagnostics records scope |
| P2.2 clear display retains session/records | `ClearCurrentRunTests` and actual Change/Clear/Same DUT controls in `OperatorPersonaJourneyTests` |
| P2.2 stale completion/new warnings | `RunNotificationContinuityTests` accepted-run transition and preservation of newer/other-source alerts |
| P3.1 terminal handoff, old evidence immutable, incoming attribution | `OperatorPersonaJourneyTests.Departing_and_incoming_operators_complete_distinct_jobs_without_changing_saved_evidence`: four configurations, actual badge/fields/prompts/buttons, two RunIds/SessionIds, original run.json byte comparison, search and reopening prior run |
| X3 program mutation while run/prompt owns station | `OperatorProgramOwnershipTests` real active required prompt refuses catalog/picker without changing program/session/loaded plan/evidence; terminal retry works; delayed picker cannot replace newly owned context |
| P3.1 active-run session guard and Stop across navigation | `OperatorPersonaJourneyTests.Operator_can_stop_a_required_prompt_from_Settings_and_inspect_the_cancelled_saved_record`; existing busy/ownership VM tests |
| P3.1/P3.2 late badge success/failure, session/program boundary, fresh incoming capture | Nine deterministic `OperatorHandoffCredentialTests` cases including cancellation-ignoring reader, actual idle expiry, fresh stale-session capture and stale finally/busy-state isolation |
| P3.2 idle warning/stale/every-run re-confirmation | `RunBoardChildViewModelTests` injected-clock soft-warning/stale/Same DUT tests; `RunTestViewModelTests` stale policy; real stale-session capture and persona reconfirmation |
| P4.1 persistent verdict and complete keyboard-copyable identity | `ResultsRunIdentityTests` all five verdicts/legacy unknown metadata; four `OperatorRunIdentityWorkflowTests` production-shell actual keyboard popup, clipboard, timestamps/IDs and natural row reopen with immutable saved bytes |
| P4.1 search after close retains focus/list and selects another record | `ResultsFilterContinuityTests` actual file-store load counter, preserved selection and zero unintended detail loads during filter rebuilding; four `OperatorResultsFilterContinuityTests` actual Escape, Ctrl+A/Backspace, incremental typing, Tab/ArrowDown/Enter and natural row reopen |
| P4.1 exact identity/search, no matches, close/reopen focus | `ResultsFilterRecoveryTests`, `OperatorResultsLayoutTests`, persona prior-RunId search, `ResultsComparisonTests`, existing Results detail/report-history tests |
| P4.2 selected non-first target, actual files and source identity | `ResultsExportDestinationTests.Selected_nonfirst_destination_receives_the_opened_run_package_after_target_refresh`; actual core export filesystem operations, run.json/diagnostics contents |
| P4.2 removed/changed target, retry, deferred signing target freeze | Five remaining `ResultsExportDestinationTests` cases: no output at alternate root, explicit reselection, changed configured root, delayed certification with destination change/removal |
| P4.2 signing/cancel/PIN correction/focus/compact actions/actual package | `OperatorSigningWorkflowTests`: four production-shell/view cases with deterministic PIN reader, real report/attestation/export services; Escape cancellation/focus return, wrong PIN, retry, chosen destination and original working PDF bytes |
| P4.2 missing package evidence, no false success | Six `ResultsExportDestinationTests.Missing_declared_evidence_blocks_all_destination_writes_until_exact_evidence_is_restored` cases remove and restore run.json, working/current issued/historical issued PDF, issued snapshot and sidecar; no package directories are created before validation, exact destination/context and original bytes retained |
| P4.2 exact revision, issued originals, late cancellation/run switch | `ReportActionTests` cancellation/navigation/single-flight/historical revision cases; `ResultsExportEvidenceTests`; core credential issuance/history tests |
| P4.2 newer schema, missing PDF, refused signing, regenerate working only | `ReportActionTests.Readonly_working_run_cannot_sign_or_satisfy_required_policy`; existing Results/report schema/missing-path tests; `TypstReportServiceTests` prior issue/history preservation and blocked destination cases |
| P5.1 actual Apply/query/Save/reload, unchanged sources | `InstrumentsPersonaWorkflowTests.Saved_binding_and_identity_reload_only_for_the_intended_program_without_rewriting_sources`: real temporary SettingsStore and persisted plans/sidecars; `OperatorInstrumentsLayoutTests` actual Apply/target UI and plan bytes |
| P5.1 same-named programs and exact deep-link target | Three actual-catalog `InstrumentsPersonaWorkflowTests` cases plus four `OperatorCommissionProgramIdentityTests`: exact second PlanId with/without slot, distinct filter IDs, missing target refuses fallback, Apply only second program, unchanged source/settings bytes |
| P5.1 shared resource, asynchronous selection/rebind, wrong-slot evidence | `InstrumentsPersonaWorkflowTests` exact selected program/slot and delayed query cases; unapplied discovered resource has display-only identity |
| P5.1 invalid/failed query, ownership and lease release | `InstrumentsPersonaWorkflowTests.Failed_query_preserves_binding_and_releases_the_bench_without_recording_identity`; existing Instruments busy-run/device-transfer/dispatch-failure tests |
| P5.1 thrown/reported Save failure, retained edits, honest retry | `InstrumentsPersonaWorkflowTests.Save_failure_retains_edits_restores_effective_settings_and_allows_retry` uses both failure forms |
| P5.1 unsupported IDN and deployment-owned bindings | `InstrumentsPersonaWorkflowTests` unsupported PXI resource makes zero VISA opens and records no identity; real environment/CLI indexed overlays reject editing/save with unchanged effective/file values; four `OperatorDeploymentWorkflowTests` production-view cases prove the ownership cue, keyboard read-only input, disabled edits and permitted exact mock query |
| P5.1 manual edit, unsaved/readiness/identity feedback | `InstrumentsPersonaWorkflowTests` immediate manual binding feedback case and exact resource-matched identity reload/rebind checks |
| P5.2 deployment-owned settings, async save, diagnostics | `SettingsViewModelTests`, `SettingsUiThreadTests`, `OperatorSettingsWorkflowTests`; actual diagnostics/read-only UI and native keyboard journey |
| P5.2 support evidence without OS browsing | `HomeSupportWorkflowTests.Technician_exports_real_support_zip_through_notification_when_folder_browsing_is_disabled`: real unreviewed dossier, browse action absent/refused, actual Home export command, ZIP entries/content and destination receipt |
| X2 filtered Results initially unselected, keyboard selection | `OperatorResultsKeyboardTests` types a unique search, verifies no previous selection, uses actual ArrowDown/Enter to open the exact saved record and preserves source bytes |
| X2 both themes, compact/wide, enlarged text and scroll ownership | Refreshed main capture matrix plus persona/signing/context captures; `OperatorResultsLayoutTests`, `RunWorkspaceResponsiveE2ETests`, `OperatorShellReportUxTests`, native accessibility evidence |

The signing GUI fixture uses the production shell and view with real file store,
report, attestation and export services; only the credential reader is replaced
by a deterministic PIN-requiring mock. Mock signatures are not physical signing
certification. The commissioning persistence fixture uses real temporary settings
and plan files with mock discovery/query. Native evidence remains Linux-only.

## Improvement rounds

The baseline failed the fixed persona gate. The first implementation round passed
449 relevant core tests and 600 VM tests, but expanded GUI checks found signing
focus-return trouble and a filtered-row test that had never actually selected a
row. Focus return was fixed; actual pointer row activation and a separate genuine
ArrowDown/Enter journey replaced that assumption. Dynamic graphical-capture skip
was moved inside the Avalonia test context after a renderer cleanup failure.

A further independent audit required explicit absent/unloadable-program recovery,
a real browse-disabled support export, unsupported-IDN proof, deployment-owned
binding refusal, and missing-package-evidence refusal. These requirements were
already in the frozen contract; source-only or unrelated tests were not accepted
as proof. The new tests execute real file operations and production views with
mock hardware/credentials and isolated settings.

## Candidate 1 validation

The candidate retains the fixed contract. Candidate 1 product assembly SHA-256:
`4282994c6ec731f13779c46ad13545a8287548aaf402b8ad3f370b58b042d3a9`.
Canonical repository formatting passed. Core run/report/schema/credential/health
checks passed **449/449**, including the actual scope/PDF extraction cases.
Final full VM, normal E2E, local graphical capture and native results are recorded
below when complete; earlier partial-suite results are not used as final passes.

## Targeted graphical round 1: rejected

The 156 fresh main renders exposed regressions caused by the added context and
exact-result route. All three reviewers independently rejected the graphical
gate; auxiliary signing/deployment/context renders were also still pending.
Scores below refer to this candidate, before the subsequent fixes:

| Reviewer | Hierarchy | Navigation | Execution | Setup | Space | Recovery | Keyboard |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A | 90 | 92 | 89 | 89 | 87 | 88 | 89 |
| B | 88 | 92 | 88 | 89 | 85 | 86 | 89 |
| C | 87 | 91 | 89 | 89 | 86 | 84 | 89 |

- At 900×600/font 20, focusing Part/Revision left their validation error clipped
  below the field viewport. The revised anchoring includes label, input and
  complete error as a group; regression bounds now include the error and resize.
- Compact exact-result details hid the list but lacked a persistent verdict and
  complete keyboard-inspectable identity/time. The new details keep the verdict
  visible and provide a selectable full-identity disclosure.
- The native probe confirmed real Ctrl+A/C copying of complete program identity,
  required-field recovery and natural Run focus without execution. It also
  found that Enter on the naturally focused result row after Escape did not
  reopen details. The old headless test had focused the ListBox itself, missing
  the real row route. The new regression retains natural row focus and intercepts
  Enter before the ListBoxItem handles it.

The native run on assembly `4282994c…` is explicitly partial/failing for row
reopening; it is not reported as an accepted final native journey. Dynamic scope
text was available in native names/descriptions despite Avalonia ignoring the
fixed TextBlock automation name. Native selection-count API returned zero even
though actual Ctrl+A/C copied all text; the clipboard result is the proof.

## Subsequent workflow continuity checks

The refreshed layout evidence passed **49 real-render operator cases** and
produced **238 PNGs**. All three reviewers confirmed that the clipping, persistent
verdict, full identity, signing and deployment presentation findings were fixed.
That did not by itself pass the persona gate: native execution then reproduced
search clearing that unexpectedly reopened a closed selected run. Filtering now
suppresses automatic opening during the whole list rebuild/restoration, with
actual incremental keyboard typing and row-reopen regressions.

A further exact-ID check found commissioning deep links selecting the first
same-named slot from another program. Targeting now uses the requested PlanId
and slot, duplicate program labels include IDs, and missing targets refuse to
fall back. Real catalog fixtures prove only the intended override changes.

The shared ownership/confirmation audit also added refusal of program changes
while a run owns the station, and full reconfirmation when a previously confirmed
context lacks newly required part/revision/operator information or badge policy.
Typing, badge presentation and Same DUT cannot bypass that explicit confirmation.
The core DUT-only `CanRun` contract stays intact; the operator app enforces its
complete panel gate at execution as well as in the UI.

Final renderer initialization claims the actual per-test UI dispatcher before
font/Skia/compositor setup. Outgoing session callbacks retain their own dispatcher
and are stopped at teardown. Fresh per-test app/settings and real Skia rendering
remain enabled; setup failures are not hidden by test retries.

## Candidate 3 freeze

Product assembly SHA-256:
`3adba11ab9acf8e5b0e801737d5e84e9985c2900384c4823a5d9e669ff0b773e`.
The normal suite and graphical runner use byte-identical product assemblies.
The graphical runner has its own copied test-output directory so temporary
catalog fixtures cannot interfere with the normal suite. Both use fresh per-test
settings, mock instruments/credentials and the production application views.
This candidate passed the complete normal E2E suite: **76 passed, one opt-in
capture skipped, zero failed**. Its graphical runner passed **59/59** and produced
**268 local PNGs**. Native keyboard inspection passed all three execution-scope
routes, full identity copying, natural reopen and search continuity. Native
inspection nevertheless found visible, unselected Results rows exposing cached
generic/hidden peers after recycling. All three reviewers held accessibility at
88–89; these passing functional tests did not satisfy the frozen gate.

## Native discovery repair and final candidate

Results now retains the default virtualized/recycled ListBox and coalesces its
container preparation/clearing into a list-peer children invalidation after
layout. Avalonia AT-SPI flattens selection descendants into a cached list, but
previously received no list-level notification when the descendant panel reused
rows. The repair updates that boundary without changing selection, focus or
realization behavior. New regressions inspect human names, ListItem roles and
visible bounds before row focus/selection, and keep realization bounded while
scrolling/replacing 1,000 records.

The same candidate invalidates pending badge work on actual idle expiry and
clears only the exact obsolete session-guard notice after successful confirmation;
newer alerts and other sources remain intact. Five notification cases and two
expiry cases exercise those branches. Real OpenTAP catalog serialization fixtures
are isolated from parallel metadata-cache mutation with a dedicated nonparallel
collection; assertions and product behavior are unchanged.

Final candidate product assembly SHA-256:
`dd06e12ad670ceff3dcbfff5095852a571facf9062e4f4709035606222267c7c`.
The test-only rebuild and independent graphical runner use that same product
assembly. Subsequent source edits only normalize whitespace. The five focused
row-discovery/virtualization and policy-reconfirmation GUI cases passed.
Final VM validation passed **637/637**, including all expiry, exact notice,
export, commissioning, ownership and availability regressions. The earlier one-case
OpenTAP global metadata cache collision is not counted as a pass; the full suite
was rerun after fixture isolation.

Strict Linux native discovery passed all six before-selection cases on this
assembly: full/selected/legacy exact filters, all three after wide filter clearing,
and top/bottom rows after compact wheel scrolling with Search focus retained.
Live GI and direct D-Bus agree on human names, ListItem roles, SHOWING state and
nonzero viewport bounds; target rows were neither selected nor focused. The
before/after DLL hashes match. This closes the previously demonstrated native
cache failure; all three reviewers independently accepted that specific evidence.

Canonical repository formatting passed after whitespace normalization. The
existing full dependency audit passed with no dependency or lockfile changes.
The separate final native keyboard process also passed on the same hash: all
three scope records, complete actual Ctrl+A/C clipboard bytes, persistent verdict
and scope, popup Escape, natural row restoration/Enter reopening, incremental
search and final clear. Original/copied run.json hashes remain unchanged. Both
native processes exited zero. Earlier native preparation/context checks remain
attributed to their earlier candidate, not relabeled as final-binary results.

The initial concurrent final normal/capture attempts were both killed with
exit 137 by the workspace's 16 GiB limit (`memory.events`: two OOM kills). Their
78 passing/one skipped and 61 passing records are partial results, not accepted
suite passes. All three new row tests completed in both processes; the unfinished
normal case was the enlarged-text completed-run journey. A sequential diagnostic run confirmed worker accumulation and was cancelled,
not counted as a pass. The test harness now disposes its exact owning app service
provider after closing the window, releasing each worker before the next isolated
app. It retains per-test app/settings, real Skia rendering and the production
workflow assertions. Five focused GUI cases passed after this test-only fix, with
memory returning to about 0.55 GiB. Full suites are rerun on the unchanged frozen
product binary with independent output/catalog directories. Monitoring during
the restarted pair shows workers closing between cases and roughly 2.7–3.0 GiB
memory, rather than accumulating to the workspace limit. Canonical formatting
passed again after the harness cleanup.

## Completed final validation

Both restarted runs completed successfully with zero failures on the frozen
product binary. Memory returned to about 0.7 GiB; the OOM-kill counter did not
increase. Temporary catalog/output roots are independent between runners.

| Check | Completed result | Local record |
| --- | --- | --- |
| Relevant core run/report/schema/credential/health tests | 449 passed | `persona-core-v1.trx` |
| Full view-model suite | 637 passed | `persona-vm-final-v4.trx` |
| Full real-render E2E suite | 79 passed, one opt-in capture skipped, 80 total | `persona-e2e-final-v6.trx` |
| Separate graphical/workflow run | 63 passed; 268 PNGs | `persona-capture-final-v6.trx` |
| Canonical formatting | Passed after final harness change | `deno run -A tools/ci/main.ts format` |
| Dependency audit | Passed; dependencies/locks unchanged | `deno run -A tools/ci/main.ts audit` |
| Native Results discovery | Six strict before-selection cases passed | `strict-native-dd06e12.log` |
| Native Results keyboard/identity | Three complete scope routes passed | `keyboard-native-dd06e12.log` |

TRX files are local under `/workspace/operator-test-results`; final images are
under `/workspace/operator-persona-captures-v6`, and native evidence/limits are in
`/workspace/operator-native-a11y/PERSONA-EVIDENCE-FINAL.md`. No screenshots,
native logs or generated packages are committed or publicly uploaded. The final
capture selection includes both baked shell-app checks and all three new row
checks; this explains the four-case increase over the earlier 59-case selection.

## Final simultaneous persona votes: accepted

All reviewers assessed the same frozen final candidate and completed evidence.
Each issued an independent acceptance; no reviewer consulted another's final
votes. C1–C7 retain the frozen rubric order above.

### Reviewer A

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 | 94 | 93 | 92 | 94 | 94 | 92 | 93 |
| P2 | 94 | 92 | 93 | 94 | 94 | 92 | 93 |
| P3 | 94 | 92 | 92 | 94 | 95 | 92 | 94 |
| P4 | 94 | 93 | 92 | 94 | 95 | 92 | 93 |
| P5 | 94 | 92 | 92 | 94 | 95 | 92 | 93 |

### Reviewer B

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 | 94 | 93 | 92 | 94 | 94 | 92 | 93 |
| P2 | 94 | 93 | 94 | 93 | 95 | 92 | 94 |
| P3 | 94 | 92 | 93 | 95 | 96 | 92 | 94 |
| P4 | 94 | 92 | 92 | 95 | 96 | 92 | 93 |
| P5 | 94 | 92 | 92 | 94 | 95 | 91 | 93 |

### Reviewer C

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 | 94 | 92 | 92 | 94 | 94 | 92 | 93 |
| P2 | 93 | 92 | 93 | 93 | 94 | 92 | 93 |
| P3 | 94 | 92 | 92 | 94 | 95 | 92 | 93 |
| P4 | 94 | 92 | 92 | 94 | 95 | 92 | 94 |
| P5 | 93 | 92 | 92 | 94 | 95 | 91 | 93 |

### Independent final graphical review

| Reviewer | Hierarchy | Navigation | Execution | Setup | Space | Recovery | Keyboard |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A | 93 | 93 | 94 | 92 | 92 | 93 | 92 |
| B | 92 | 93 | 94 | 92 | 91 | 94 | 92 |
| C | 92 | 93 | 93 | 92 | 92 | 94 | 92 |

**Gate passed:** all **105 persona cells** and all **21 graphical cells**
are above 90 simultaneously. Persona scores range **91–96**;
graphical scores range **91–94**. The lowest cell is **91**.
No average or median substitutes for a failing reviewer/category. Definition
acceptance separately passed all 21 D cells before implementation scoring.

All three reviewers found the demonstrated blockers resolved, including native
unselected-row discovery before any focus workaround. No unresolved critical or
minor actionable finding remains in their inspected contract scope. The narrowly
owned list-peer repair avoided broader upstream rework, so no such rework remains
to elevate. The test-cleanup repair changes fixture lifetime, not product behavior.

These are expert suitability judgments for the defined mock-station workflows,
not measured user-success percentages. Windows UIA, spoken output, physical
instruments/credentials/calibration/safety, printing hardware and newcomer studies
retain the contract's explicit separate validation limits. Earlier page-level
scores and failed/partial attempts remain historical evidence, not final votes.
