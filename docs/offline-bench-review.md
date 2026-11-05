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

## Elevated follow-up

The general existing-data `StationHealthGate` checks profile, verdict and age;
it does not bind approval to engine or effective bench configuration. A general
migration/invalidation policy is a larger change and is elevated for review.
The offline workflow requires a new bound data directory, so it inherits neither
old station-health approval nor golden references. Ordinary installations that
reuse data must invalidate/rerun health after those changes. The engine-aware
golden guard does not claim to solve general station-health qualification.
