# Offline bench delivery review

This review extends the completed [cross-app review](cross-app-persona-review.md)
with [offline delivery workflows](offline-bench-workflows.md). The prior votes
do not score the new feature. Anthropic's
[frontend-design skill](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md)
guides new controls within the existing native Fluent/Inter instrument UI.

## Definition round

Three independent reviewers held the first proposal for unclear target/runtime
identity, installation/data ownership, unsupported package semantics and proof.
They required target-aware collection, a nonexecuting check, safe old-client
refusal, separate application/data publication and actual installed execution.
Existing-data reuse would inherit golden/health qualification without accounting
for changed package payloads, so it is explicitly unsupported in this workflow.

| Reviewer | D1 realism | D2 preconditions | D3 complete path | D4 recovery | D5 continuity | D6 inclusive workflow | D7 testability |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A initial: hold | 95 | 86 | 88 | 90 | 86 | 89 | 88 |
| B initial: hold | 94 | 88 | 89 | 89 | 89 | 92 | 89 |
| C initial: hold | 94 | 88 | 89 | 90 | 89 | 92 | 88 |
| A amended: accept | 96 | 95 | 95 | 94 | 95 | 93 | 96 |
| B amended: accept | 95 | 94 | 94 | 95 | 94 | 94 | 95 |
| C amended: accept | 96 | 95 | 95 | 95 | 96 | 94 | 95 |

All 21 amended definition cells exceed 90 before implementation voting. These
are expert definition ratings, not measured human success percentages.

## Source review and implementation iterations

Independent source preflight found and required repairs for:

- Eagerly validating unrelated installed/manual packages, defeating required-only
  resolution and manual fallback. Candidate payloads are now captured only when
  their identities become reachable; required failures retain their diagnostics.
- Deduplicating equal bytes at different deployment paths. Actual normalized
  installation paths now contribute to package payload identity.
- Different authoring/bench action and prerequisite policies. Both now use a
  shared metadata policy; bench checking independently verifies the archives.
- Unbounded source rechecks and a missing aggregate expanded-archive budget.
  Capture/hashing is bounded and cancellable; the bundle shares byte/entry limits.
- Data options hidden after `--`, plus alternate CLI spellings. Startup validates
  effective arguments through the production parser and binds data before any
  settings/logging; old/external settings/plugin/data overrides are refused.
- Hidden progress/errors, stale authoring completion after input/context changes,
  and stale Settings detail-dialog focus. Feedback is pinned/live-announced,
  local controls recover after stale work, completion is revoked on changed
  choices, and focus continuations retain their owner checks.

Startup additionally verifies the immutable runtime/program inventory before
using its bound data. Helpers verify a release capability receipt bound to the
actual application/host DLLs before invoking options older apps do not support.
No package action, feed lookup or plugin execution is used by offline inspection.

Additional review repaired effective executable-mode conflicts, package/file
completeness, protected runtime anchors, aggregate archive expansion limits,
source-mode changes, source/capability races, merged file/directory conflicts,
concurrent foreign destinations and cancellation at publication boundaries.
Runtime collection follows the release dependency ledger rather than recursively
copying station data. Startup refuses external plugin/settings overrides and
validates its immutable runtime/program inventory before using bound data.

The genuine OpenTAP package declares built-in installation actions. All three
definition reviewers accepted the narrowly documented same-version engine
exceptions in W7: apply Linux executable permissions declaratively; never invoke
the Windows in-place upgrader for a fresh same-version clone. Altered, additional
or unrelated actions remain refused. This is not evidence of Windows execution.

The joined execution exposed different same-version OpenTAP assemblies: authoring
used the SDK runtime while operator/worker transitive references selected the
NuGet library variant. Executable projects now reference the SDK directly and
publish its complete runtime. Authoring, operator, worker, test process and the
installed copy agree on engine SHA-256
`674cdb1841b2371a77233007024bf52de15b9df4117cecf41c8942ee65345211`
and loaded identity `9.35.0.0/bb7b4c3f-52e5-4575-8ac0-8cf134d5a828`.
Golden-reference configuration now includes the loaded engine version/MVID. A
same-version engine change blocks a stale selection before execution, requires a
compatible choice or fresh acquisition, and preserves historical files.

Verification also found an incomplete export receipt and generic reference
recovery copy. The receipt now includes exact package versions, requirements and
hashes; the recovery message offers compatible selection or fresh acquisition.
An Escape test incorrectly sent a key release to an already closed window; its
event sequence was corrected while retaining focus-return assertions.

## Verification evidence

Artifacts stay outside the repository and are not uploaded publicly. The source
contract and recorded paths make the review reproducible without publishing
station data. Intermediate failures are retained rather than counted as passes.

| Evidence | Result |
| --- | --- |
| Initial Host runs | 63/79, then 86/108; SDK resolution and startup exception handling repaired |
| Host focused round 3 / round 4 | 111/111, then 142/142 passed as coverage expanded |
| Initial authoring runs | 16/19, then 22/24; compiled dependency fixtures and deepest conflict diagnostics repaired |
| Authoring round 3 / round 4 | 26/26, then 27/27 passed, including genuine engine metadata |
| UI rounds 1–4 | Revealed fixture setup problems, diagnostic overwriting, compact receipt overflow, genuine engine actions and missing exact-version receipt; corrected and rerun |
| Joined rounds 1–3 | Failed at missing test-process SDK, strict cleanup fixture, then runtime variant mismatch; none counted as success |
| Disconnected joined rounds 4 and 5 | Passed actual export, transfer, check/install, bound startup, installed worker plugin/dependency execution and saved result/PDF |
| Disconnected published joined round 5 | Same journey passed against a freshly published operator baseline |
| Release traversal and publication at `45dc97f` | Zero warnings/errors |
| Canonical dependency audit | Passed, no known vulnerable packages in the evaluated traversal |
| Full Host suite, round 5 | 1,091/1,091 passed, including all 142 offline checks |
| Full operator VM suite, round 5 | 679 passed; one existing Settings-priority assertion exposed the new disclosure ahead of daily preferences |
| Repaired operator VM checks, round 6 | 41/41 passed, including Settings order, engine requalification and offline inspection |
| Authoring offline suite, round 6 | 32/32 passed, including five actual generated Linux-script protocol cases |
| Authoring graphical handoff, round 5 | 12/12 passed across Light/Dark wide and compact conditions |
| Operator graphical offline workflow, round 5 | 5/5 passed; four presentation cases use an explicitly injected inspection result and one checks dialog ownership |
| Architecture, round 5 | 45/45 passed |
| Default headless sequence discovery, round 6 | 3/3 passed with capture disabled, reproducing the prior CI environment |

The standalone xUnit journey is run in the network-disabled sandbox, without the
VSTest network transport. `OfflineDeliveryJourneyTests` creates a strict authored
plan and unique transitive plugin absent from the operator baseline, transfers
only the bundle, renames source package/home paths out of reach, then uses the
actual operator CLI and installed worker. It records the loaded plugin,
dependency and engine paths/identity, value 1.25, saved run and generated PDF.
Original runtime, existing credential/reference sentinels and transfer contents
are compared before/after. The installed application also refuses an old data
override before configuration is loaded.

Round-5 artifacts are under `/workspace/offline-bench-joined-round5` and
`/workspace/offline-bench-joined-published-round5`; TRX files and helper logs are
under `/workspace/offline-bench-results`. Final graphical/native evidence and
independent implementation votes follow after remaining checks.

Executing the generated Linux helper against the genuine release exposed the
JSON writer's escaped `+` in the build identity. The field reader now decodes only
that exact escape before its existing restricted grammar. Actual generated-script
protocol tests verify escaped identity, quoted argument boundaries, check-before-
install ordering, and old/tampered-client refusal before any invocation. Other
escapes fail closed. These stub-based protocol tests are distinct from the real
published-release integration proof.

Prior PR CI failures were retrieved through the GitHub connector. Linux and
Windows both failed the same three sequence-discovery tests because their settle
helper required a bitmap while the default harness selected non-Skia headless
drawing. The fix retains render/layout ticks and all peer/name/bounds/keyboard
assertions, and requires a bitmap only when capture mode installs Skia.

Native intermediate Light/Dark authoring journeys passed real missing-package
recovery, manual archive retry, cancellation at a pending private loopback HTTPS
endpoint, retained inputs, full receipt copying, option-change revocation and
natural Escape. The native pass caught an incorrect directional error-message
suffix, which was replaced with neutral wording. Compact rendered review also
shortened the pinned success summary and moved the full folder path into the
copyable receipt.

Native operator journeys used the real shared `CheckBundle`, copied exact
program/export/build/package provenance from the full dialog, exercised fresh-
folder correction and copied the explicit command without installing. Real source
mutation then exposed a recovery defect: installation controls disappeared while
focus/scroll stayed in the removed area, leaving the recheck feedback offscreen.
The view now captures inspection ownership before hiding those controls, restores
the path and feedback only for the still-owned flow, and rechecks current focus
before the deferred move. Six real-watcher GUI cases cover owned null-focus
recovery, retained unrelated Settings focus and an outer navigation race. Both
native themes subsequently returned to a visible path and recheck message.

All four final round-9 native feature cases passed their functional journeys;
separate production startup used explicit mocks, preserved the immutable runtime
and saved evidence, and performed no navigation or execution. The reviewer then
challenged the typography claim: surrounding text enlarged to 20 px, while
operator text fields appeared smaller. Numeric baseline tests confirmed every
offline TextBox and its realized `PART_TextPresenter` remained at 14 px under a
20 px window (two compact failures, two normal-size passes). The earlier native
round therefore establishes window size/font and functional behavior, not full
20 px input typography.

One shared operator TextBox style now inherits the owning window's font size,
with the normal 14 px fallback. Numeric tests cover offline paths, full inspection
and golden-save name/validity fields and their realized presenters. All 15
affected operator graphical cases pass after the fix. The reference selector and
its selected label already inherit the expected 14/20 px values and were measured
without a production change. Authoring independently passes all four numeric
typography cases for paths, repository and error/completion receipts; it needs no
corresponding product change. Final post-fix native evidence supersedes the
earlier partial typography claim.

The current SDK runtime also passed all four fresh golden-reference joined stages
under `/workspace/offline-cross-app-joined-round9`, including actual measured
A/pass → B/fail → A/pass, immutable recording import, authored revision change,
old-reference refusal and fresh requalification. This repeats the earlier
cross-app oracle after changing the operator's engine variant.

The complete round-9 default authoring graphical suite passed 547 cases with six
explicit opt-in skips (553 total); the capture-mode handoff/sequence selection
passed all 15. The full operator VM suite passed all 680. Offline authoring passed
32 and architecture passed 45. Both disconnected bundle journeys and genuine
Linux helpers passed against the current build and publication. These results
precede the final shared text-field typography change; its affected UI checks and
native rerenders are recorded separately below.

## Elevated follow-up

The general existing-data `StationHealthGate` checks profile, verdict and age;
it does not bind approval to engine or effective bench configuration. A general
migration/invalidation policy is a larger change and is elevated for review.
The offline workflow requires a new bound data directory, so it inherits neither
old station-health approval nor golden references. Ordinary installations that
reuse data must invalidate/rerun health after those changes. The engine-aware
golden guard does not claim to solve general station-health qualification.

## Final candidate verification

The frozen product candidate is `6e49ca5d8db9ef77606a39cb82c6fefb9f65e9a0`.
Subsequent review-record commits change documentation only. The final native
ledger is `/workspace/cross-app-native-a11y/offline11-native-final-results.json`;
exact product/host/fixture hashes, logs and 35 local PNGs identify the tested
candidate. Six native feature cases pass: both applications in Light/Dark at
actual 900×600/font20, plus both golden-save form typography cases. A separate
actual installed apphost startup passes with explicit mock flags and the bound
fresh data, preserving runtime and saved evidence; it performs no navigation or
execution. AT-SPI does not supply font attributes here: numeric TextBox and
realized-presenter tests establish 14/20 px scaling, corroborated by native
pixels, 43 px input bounds and visible 40 px actions.

| Final check | Outcome |
| --- | --- |
| Full Release traversal and fresh publication | Passed, zero warnings/errors |
| Canonical format and dependency audit | Passed |
| Host full suite, unchanged Host source | 1,091 passed |
| Operator VM full suite, unchanged VM source after typography | 680 passed |
| Full ordinary authoring graphical suite, unchanged product source | 547 passed; six opt-in skips (553 total) |
| Final authoring Skia handoff/sequence and numeric typography | 15/15 passed |
| Final full operator graphical suite, including typography and watcher races | 95 passed; three opt-in skips (98 total) |
| Final offline authoring and generated-script protocol suite | 32/32 passed |
| Final architecture | 45/45 passed |
| Final disconnected ordinary/published delivery journeys | Both 1/1 passed |
| Final genuine Linux helper Check/install and legacy refusal | Passed; legacy app never launched |
| Final actual authoring/reference/execution/edit/requalification chain | Four stages, each 1/1 passed |
| Final native feature cases / installed startup | Six passed / one passed |

Final TRX files and helper logs are under `/workspace/offline-bench-results`,
with `round11` suffixes; retained broad Host/VM/default-authoring results are
explicitly identified in the earlier table. Final renders are under
`/workspace/offline-bench-captures-round11/{authoring,operator}`. Opt-in skips are
not execution proof: both offline journeys and all four golden stages are also
run explicitly.

The final disconnected ordinary and published roots are
`/workspace/offline-bench-joined-round11` and
`/workspace/offline-bench-joined-published-round11`. Each executes the unique
installed plugin and its transitive dependency, records value 1.25 and the
actual installed engine identity, saves a run/PDF, and preserves the original
runtime/data and transferred bundle. Generated Linux helpers are exercised
against the genuine final publication, not only a recording stub.

The complete golden chain was repeated on this exact final candidate under
`/workspace/offline-cross-app-joined-round11`. For the same `PRODUCTION-UNIT`,
run `097c7d2f0fb24dfc8c16cace74423d85` passes against Golden A,
`ec46b4791d2e442abc24082653f66272` fails against Golden B, and
`8a690127eb6e4735a2345cd258a7108b` passes against A again. The exact recording
returns to authoring; revised deployment rejects old qualification and succeeds
with newly acquired data. Earlier round-9 evidence is retained separately.

## Final independent voting

Three reviewers independently inspected the frozen candidate, source closures,
real native/Skia views, actual offline execution and the golden-reference oracle.
They did not consult one another's final scores. All **147 persona cells and 42
separate-app graphical cells pass simultaneously: 189/189 exceed 90, minimum
91**. There is no average or selected best vote masking a failure. These are
expert suitability ratings within the declared Linux/mock/fresh-data evidence
boundary, not measured human success percentages or physical qualification.

C1 completion/correctness; C2 discoverability; C3 efficiency/control;
C4 recovery; C5 integrity; C6 access/space; C7 feedback/handoff.

### Reviewer A

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 First-day operator | 94 | 93 | 94 | 94 | 95 | 92 | 94 |
| P2 Repeat operator | 95 | 93 | 94 | 94 | 96 | 92 | 94 |
| P3 Shift operators | 94 | 93 | 93 | 94 | 95 | 92 | 94 |
| P4 Quality reviewer | 95 | 93 | 93 | 95 | 96 | 93 | 95 |
| P5 Commissioning/reference technician | 94 | 92 | 91 | 95 | 96 | 92 | 94 |
| P6 Test author | 94 | 92 | 92 | 95 | 95 | 91 | 94 |
| P7 Release/integration engineer | 94 | 92 | 91 | 95 | 96 | 91 | 95 |

### Reviewer B

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 First-day operator | 95 | 93 | 93 | 94 | 96 | 93 | 94 |
| P2 Repeat operator | 95 | 93 | 94 | 94 | 96 | 93 | 94 |
| P3 Shift operators | 94 | 92 | 93 | 94 | 95 | 93 | 94 |
| P4 Quality reviewer | 95 | 93 | 93 | 94 | 96 | 93 | 95 |
| P5 Commissioning/reference technician | 95 | 92 | 92 | 95 | 96 | 92 | 94 |
| P6 Test author | 95 | 93 | 93 | 95 | 96 | 92 | 95 |
| P7 Release/integration engineer | 95 | 92 | 91 | 95 | 96 | 92 | 95 |

### Reviewer C

| Persona | C1 | C2 | C3 | C4 | C5 | C6 | C7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| P1 First-day operator | 94 | 93 | 92 | 94 | 95 | 92 | 94 |
| P2 Repeat operator | 95 | 93 | 94 | 94 | 95 | 92 | 94 |
| P3 Shift operators | 93 | 92 | 92 | 94 | 95 | 92 | 94 |
| P4 Quality reviewer | 95 | 93 | 92 | 94 | 96 | 92 | 95 |
| P5 Commissioning/reference technician | 95 | 92 | 91 | 95 | 96 | 91 | 94 |
| P6 Test author | 95 | 92 | 91 | 95 | 96 | 91 | 95 |
| P7 Release/integration engineer | 95 | 93 | 92 | 95 | 96 | 92 | 95 |

### Separate graphical votes

| Reviewer | App | Hierarchy | Navigation | Primary task | Setup | Space | Recovery | Keyboard |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A | Authoring | 93 | 92 | 94 | 94 | 91 | 94 | 92 |
| A | Operator | 94 | 93 | 94 | 94 | 92 | 95 | 93 |
| B | Authoring | 94 | 93 | 95 | 94 | 92 | 95 | 93 |
| B | Operator | 94 | 93 | 95 | 94 | 91 | 95 | 94 |
| C | Authoring | 93 | 92 | 94 | 93 | 91 | 95 | 92 |
| C | Operator | 93 | 93 | 95 | 94 | 91 | 95 | 91 |

The technician/release efficiency and compact-space ratings retain headroom:
commissioning, long provenance and the explicit external installation command
require care and scrolling. Required fields, correction paths, keyboard recovery
and installation state remain explicit. Every concrete review finding, including
typography and focus races, is closed in the tested product.

Raw independent reports are retained locally as
`/workspace/offline-bench-final-review-{a,b,c}.md`; the tables above preserve their
complete votes in the repository. The general existing-data StationHealthGate
policy remains the elevated larger follow-up described above. Acceptance applies
to the fresh bound offline workflow and does not authorize a general data
migration. Windows UIA/PowerShell execution, Linux ARM execution, physical drivers,
physical qualification, spoken output and newcomer studies remain unverified.
GitHub CI is separate from these local results and was still running when the
review record was prepared. The combined PR remains a draft for parallel review.
