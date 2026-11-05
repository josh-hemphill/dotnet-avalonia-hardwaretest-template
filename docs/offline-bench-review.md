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

Initial full Release compilation passed after correcting SDK-reference wiring,
the receipt build target and test access/import issues. The first 79-case Host
run passed 63 and failed 16: runtime SDK resolution and an omitted explicit
InvalidDataException recovery catch were found and repaired; reruns are pending.
The first authoring run passed 16 of 19; three fixtures lacked the compiled plan's
OpenTAP dependency. Compiled-plan dependency roots and their fixture archives are
being integrated and verified. These intermediate runs are not final passes.

The canonical dependency audit passes across the evaluated repository traversal.
Final builds, regression outcomes, joined offline proof, rendered/native evidence
and independent implementation scores will be recorded after verification.
