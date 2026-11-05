# Cross-application persona review evidence

Contract: [cross-app-persona-workflows.md](cross-app-persona-workflows.md), version 2.
Branch: `review/authoring-to-execution`. Prior separate app votes are historical;
they did not include golden-unit references or a joined authoring/bench journey.

Design guidance: Anthropic's official
[frontend-design skill](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md),
read at blob `a5333457c414d20d625f307df945842c0952ecc3`. Its hierarchy, copy,
restraint and screenshot critique guidance is applied to the existing Fluent/Inter
native instrument application. The operational semantic palette and explicit
measurement/provenance controls remain deliberate product choices.

## Definition review

Three independent reviewers first proposed and challenged the added workflows.
They converged on keeping the five operator personas and adding author and
release/integration engineer; golden-reference custody is an explicit P5 task.
The initial definition round was held for ambiguous expiry and configuration
compatibility. Both were made explicit before acceptance or implementation scoring.

| Reviewer | D1 | D2 | D3 | D4 | D5 | D6 | D7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A | 95 | 95 | 96 | 95 | 96 | 95 | 95 |
| B | 95 | 95 | 95 | 94 | 96 | 94 | 95 |
| C | 95 | 93 | 96 | 95 | 96 | 94 | 94 |

All 21 final definition scores exceed 90. The amended contract is frozen before
implementation scoring. The implementation gate requires all 147 persona cells
and all 42 separate-app graphical cells above 90 on the same candidate, with no
critical evidence gap. Reviewer judgments are not measured user-success rates.

## Combined baseline

Baseline commit: `1b8e0c4c72aac5a4484e1abbcecaa0fe0cf38eaf`. It contains both
authoring #239 and operator #241, but has no golden-reference store, promotion,
selector, reference-dependent runtime acceptance or frozen provenance. The earlier
19 authoring and 15 operator checks are app-local smoke checks, not evidence of
the same newly authored plan crossing the complete handoff. Independent baseline
matrices and implementation/evidence rounds are recorded below when completed.

## Scope and evidence rules

Use actual rendered views for new workflow and graphical claims. Use deterministic
filesystem/VM/runtime tests for corruption, expiry, immutable snapshots, failed
saves and ownership races; identify these separately. A complete joined test must
compile the same newly authored plan, transfer it, discover/execute it through the
operator, persist/report/export it, and return that exact run to authoring.
The profile A/pass versus profile B/fail oracle must verify actual persisted
outcomes and numeric provenance, not only a selector or status label.

Screenshots/native logs remain local. No prior native binary or app-local score
is relabeled as proof of the new combined candidate. Windows UIA, spoken output,
physical instruments/credentials/calibration/safety and newcomer studies remain
outside the stated mock-station evidence.

## Independent expanded-scope baseline votes: rejected

Each reviewer inspected only original commit `1b8e0c4` and its existing renders;
in-progress implementation was excluded. C1–C7 follow the frozen rubric.

### Reviewer A

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 | 64 | 74 | 78 | 80 | 62 | 89 | 72 |
| P2 | 66 | 76 | 78 | 82 | 62 | 89 | 74 |
| P3 | 73 | 80 | 82 | 82 | 65 | 89 | 75 |
| P4 | 64 | 80 | 80 | 78 | 50 | 89 | 70 |
| P5 | 40 | 48 | 58 | 60 | 38 | 80 | 50 |
| P6 | 60 | 70 | 76 | 85 | 55 | 86 | 70 |
| P7 | 62 | 66 | 66 | 76 | 50 | 82 | 58 |

### Reviewer B

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 | 74 | 70 | 80 | 82 | 75 | 89 | 76 |
| P2 | 74 | 73 | 82 | 83 | 76 | 90 | 77 |
| P3 | 82 | 78 | 86 | 85 | 78 | 91 | 80 |
| P4 | 68 | 76 | 82 | 80 | 58 | 91 | 75 |
| P5 | 28 | 39 | 48 | 45 | 30 | 86 | 44 |
| P6 | 53 | 64 | 76 | 80 | 62 | 88 | 68 |
| P7 | 59 | 62 | 70 | 77 | 58 | 88 | 63 |

### Reviewer C

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 | 65 | 63 | 82 | 76 | 56 | 88 | 68 |
| P2 | 64 | 62 | 81 | 75 | 55 | 88 | 66 |
| P3 | 72 | 69 | 84 | 78 | 60 | 88 | 70 |
| P4 | 60 | 70 | 82 | 78 | 45 | 89 | 67 |
| P5 | 35 | 38 | 68 | 52 | 32 | 87 | 45 |
| P6 | 60 | 61 | 80 | 81 | 48 | 88 | 66 |
| P7 | 64 | 59 | 73 | 68 | 49 | 87 | 58 |

### Separate graphical baseline

| Reviewer | App | Hierarchy | Navigation | Primary task | Setup | Space | Recovery | Keyboard |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A | authoring | 92 | 82 | 66 | 86 | 89 | 87 | 85 |
| A | operator | 92 | 78 | 55 | 65 | 92 | 82 | 84 |
| B | authoring | 91 | 84 | 59 | 85 | 89 | 82 | 88 |
| B | operator | 92 | 78 | 52 | 70 | 91 | 75 | 88 |
| C | authoring | 91 | 70 | 58 | 68 | 89 | 79 | 88 |
| C | operator | 92 | 67 | 50 | 52 | 91 | 75 | 88 |

All three rejected the expanded baseline. Reference declaration, acquisition,
storage/selection, actual numeric acceptance, frozen provenance and joined
handoff evidence were absent. The ordinary layouts remain useful; lower scores
reflect added tasks rather than removal of prior improvements. Original authoring
compact evidence is 960×600/font20; the new 900×600 contract must be tested fresh.

## Initial implementation and verification rounds

The first implementation adds typed scalar reference declarations, immutable
bench profiles and retirement markers, frozen run inputs, actual additional
operator acceptance, full provenance and a checked compiled-pair handoff.
Additional reference acceptance belongs to the operator app, separately from the
raw OpenTAP backend result. Authoring directs reference-dependent programs to the
operator instead of offering external TUI execution as equivalent acceptance.

Early checks completed:

- 151 focused Core/schema/storage/credential/report checks passed, including
  actual PDF extraction of reference provenance and acquisition-only wording.
- The first 52-case authoring/domain run found two failures: malformed reference
  declarations escaped the catalog error boundary, and a recovery test asserted
  workspace-wide compilation state despite an unrelated legacy migration.
  The catalog now exposes the unusable entry without breaking other programs;
  the test checks the corrected program's actual compiled value/hash. All 52
  cases passed on rerun, including nine handoff/export cases.
- The first real-render authoring run passed eight of twelve cases. Four failed
  because the test sent only Space key-down, without the release required by
  CheckBox release-click behavior. The joined first stage stopped at the same
  test-input error. Complete key press/release was added, with no direct checked
  state assignment or product workaround. The rerun passed 13/13, including the
  new external-TUI acceptance guard, and produced 19 local PNGs.
- Joined stage 1 passed on the fresh `cross-app-joined-round2` directory: the
  persistent authored program was compiled, then the actual bench-preparation
  dialog published its checked pair/receipt. Later stages must use these exact
  artifacts; this is not yet a completed joined workflow.

The implementation review also caught stale preserved samples being mistaken
for selected-acquisition coverage, missing criterion remapping on duplicate/delete,
missing frozen inputs vacuously passing, and same-ID reference-file replacement
changing confirmed numbers. These are addressed with explicit scope/time filtering,
document lifecycle hooks, declared-input matching and confirmation/hash checks;
their final regression results are recorded when run.

TRX records are local under `/workspace/cross-app-results`. Fresh authoring images
are under `/workspace/cross-app-captures-round2/authoring`; joined artifacts are
under `/workspace/cross-app-joined-round2`. Initial failures remain historical
evidence and are not relabeled as passes. Final scoring has not begun.

The authoring controls passed their first complete 13-case rendered run, and the
joined authoring stage published the actual program/receipt. Further known
required-field recovery refinements remain under test. A compatibility guard is
also being added so older or unprepared standalone runtimes cannot silently
ignore reference-dependent acceptance. Final candidate evidence will be refreshed
after that change; the early guarded/un-guarded artifacts are not interchangeable.

The early joined operator stage exposed a genuine cross-application load failure:
repeated references to the same authored DMM produced duplicate configuration
fingerprint keys. This did not execute or qualify a run. Identical logical
slot/type/resource references must be canonicalized, while conflicting bindings
remain explicit errors. The diagnostic confirmed the duplicate-key failure before execution. The failed prototype and diagnostic TRX are retained;
the fully guarded candidate will rerun the complete joined chain from fresh data.

The guarded authoring matrix and joined first stage then passed 14/14, and the
initial operator VM reference suite passed 12/12. These are intermediate checks.
The joined second stage passed loading and acquisition; an incorrect automation
name stopped promotion in its first attempt. After correcting that test input,
the actual numeric oracle failed: cleanup and acquisition held different
deserialized objects for the same logical instrument, so cleanup did not reset
the measured mock DMM. The failure is retained, and shared runtime binding is
being corrected before claiming a joined pass.

Independent source preflight found further required improvements: proposed replay
must not reuse a stale tolerance while numeric text is incomplete; multiple
imported criteria must be disclosed and removal scope explicit; malformed saved
reference evidence needs preserved-byte recovery; pending retirement and retirement
during multi-profile loading must block pre-acceptance; percent comparison needs
an overflow-safe extreme boundary; native names must distinguish independent
criteria and profile items. A native probe also found that the authoring minimum
width silently expanded requested 900-pixel captures to 960 pixels. Product
minimum width and actual-bounds assertions are being corrected, with fresh
compact evidence required. No final score is assigned to these intermediate builds.

The canonical dependency audit subsequently passed for the full traversal, with
no known vulnerable packages. An isolated older operator payload was also checked
with `--validate-plan` against the guarded round-3 plan: exit 1,
`PLAN_LOAD_FAILED`, unable to locate `RequireOperatorReferenceAcceptanceStep`.
It did not silently deserialize past the unsupported guard. The checked older
`HardwareTest.dll` SHA-256 was
`dd06e12ad670ceff3dcbfff5095852a571facf9062e4f4709035606222267c7c`.
This proves that particular older mock-client validation refusal, not arbitrary
third-party runtime behavior.

The next full traversal Release build passed with zero warnings/errors. The
232-case focused Core run initially passed 223 and failed all nine newly added
corruption-recovery cases: `InvalidDataException` is not caught by an
`IOException` branch. An explicit catch fixed that boundary; all 232 passed on
rerun, including shared-instrument cleanup, guarded execution, atomic acceptance,
expiry, immutability, numeric extremes and actual PDF evidence. The focused
authoring declaration/export run passed 33/33.

Broader regressions are kept separate from those focused results. Operator VM:
647 passed, three failed of 650. The binding checker incorrectly attributed the
new child view's bindings to the parent VM, and two picker tests supplied missing
files that the new real catalog validation correctly refused. The checker now
validates declared root/item types, the fixtures create actual plan files, and a
missing-file recovery case was added. Authoring real-render UI: 528 passed, three
failed of 531, with 157 local PNGs. The failures exposed Pack being pushed below
the initial viewport, unstable criterion option instances preventing selection,
and an oversized compact summary. The source fixes move bench preparation into
the Build heading, retain stable options with explicit two-way selection, and
keep the summary short while exposing each equation through the criterion
selector. These repairs require their own passing reruns before final voting.

Those authoring repairs then passed 49/49 real-render checks and produced 156
fresh images, including actual 900-pixel windows. Three additional native-peer
UI cases passed after the sequence list's realized-row cache was corrected.
The full authoring domain run completed with 1,983 passed, four intentional
platform/tooling skips and six failures. Three were stale assertions about
historical labels/schema or a newly reused binding label; three exposed a real
regression blocking ordinary schema-4 board previews. The files remain read-only;
known historical sample/formula preview is now supported without golden-reference
approval. All six failures and the surrounding preview/editor/schema cases passed
in a 111-case targeted rerun. Operator repair checks passed 86/86.

The joined numeric oracle now proves actual A/pass versus B/fail and saved PDF
provenance. Further joined iterations found stale export destinations after
settings changes, contradictory backend-only success counts, and a failed
reference row below the 900×600 window. Export choices now refresh without
redirecting a captured request. Read-only reference leaves drive final rollup,
failure filters and navigation; passing reruns clear the prior failure. Compact
header duplication is being reduced and must pass the actual rendered failure
matrix before acceptance. Failed attempts remain separate TRX/data directories.

A fresh independent three-reviewer preflight also found malformed reference
preferences escaping settings recovery (null list/items), indistinguishable
same-metric promotion candidates, wrong missing-unit issue routing, silent stale
handoff actions and busy preview details. Settings recovery now preserves the
file and blocks writing until corrected; all 250 relevant Core/settings checks
pass. Candidate labels/native identities and the remaining recovery refinements
are under verification. Dark recording-dialog captures that used only an owner
theme were mislabeled light images; their fixtures must assert the actual dialog
theme before replacement captures count as Dark evidence.

Native Linux testing proved true-size handoff/review copy and sequence discovery,
then caught Add criterion leaving the editor on the first rule. A new stable
choice/publication fix and stronger actual-key assertion are required; the old
test did not assert that the selected ID was newly created. Environment restart
paused the round without changing its acceptance criteria. Final numerical
votes remain held until all joined, compact and native checks finish.

The complete operator stage subsequently passed its wide/compact Light/Dark
failure matrix, genuine A/pass→B/fail→A/pass sequence, exact export and retirement
checks (`cross-joined-stage2-round8.trx`). This was still intermediate: review of
the saved attempt display exposed a separate editable-confirmation boundary
allowing a new DUT to retain the preceding unit's session ledger. The corrected
boundary also clears old credentials/attempts for a changed known technician,
as required by P3; same-identity reconfirmation and initially missing metadata
remain continuous. New direct pipeline/badge/active-ownership regressions cover
both routes. The final joined oracle uses the same production DUT for A/B/A and
the actual Change session action for different golden units.

Final preflight repairs also prevent reserved factory IDs at human plan creation,
route missing-unit findings to the real unit editor, make stale handoff actions
explain reopening, and disable/guard details during pending previews. Native
testing exposed Add selecting the existing criterion; stable observable options
and publication ownership now preserve the genuinely new selected ID. GUI tests
assert previous criteria remain unchanged. Same-metric promotion candidates now
disclose their input keys and unique native criterion identities. These changes
are being built and reverified together before final independent scoring.
