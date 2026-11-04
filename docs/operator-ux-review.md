# Operator application UI/UX review

Final majority scores: **91–93**, strictly above 90 in all seven categories.

Review branch: `fix/operator-ux-voting-review`, independently based on `latest`.
Authoring PR #239 remains separate; this review covers the standalone operator
application in `src/HardwareTest` and its actual operating workflows.

Persona workflows and subsequent UI changes are evaluated separately in
[the persona review](operator-persona-review.md), against
[the frozen persona contract](operator-persona-workflows.md).

## Fixed method and target

Three independent competing reviewers assess each graphical candidate using the
same seven categories. The median is the majority vote. The goal is strictly
over 90 in every category (at least 91), with no observed blocking workflow issue.
Scores are heuristic expert judgments, not percentages of successful users.

| Category | Operator scope |
| --- | --- |
| Visual hierarchy/readability | Status, content and action hierarchy in both themes |
| Navigation/discoverability | Home, Run, Results, Settings and diagnostic destinations |
| Core workflow completion | First session, test execution, operator prompts and results |
| Hardware/setup clarity | Station readiness, program requirements and recovery |
| Space/scroll/resizing | 900×600/font20 and 1280×800/font14; scroll ownership |
| Feedback/error recovery | Invalid inputs, failed/stopped tests and retained evidence |
| Keyboard/accessibility | Focus, persistent labels, native API and keyboard workflows |

0–59 means blocked; 60–79 substantial friction; 80–89 usable with minor friction;
90+ polished. Reviewers do not consult one another's votes, and the rubric is
fixed before any numerical baseline. Native API checks do not establish spoken
screen-reader output, Windows UIA or physical instrument execution.

## Design guidance

Apply Anthropic's [frontend-design skill](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md),
read for this review. Audience: operators performing repeatable bench tests,
with engineer diagnostics available as secondary tasks. Keep the existing
Fluent/Inter identity and semantic colors: action blue `#1565C0`, pass/continue
green `#2E7D32`, stop/failure red `#C62828`, attention orange `#FB8C00`, with theme
canvas/surface/text resources. Use a 22px route heading, inherited body scale,
and at least 40px operational targets; enlarged text must remain usable.

The defining hierarchy is the current station task and its next required action.
Preserve session confirmation, credential gates, cooperative stop, evidence,
export and run-ownership behavior. Make filled-in values retain visible labels;
errors must lead to their actual field. Keep diagnostic implementation details
available through disclosure without preceding routine operator tasks.

```text
Navigation | Stable station feedback
           | Program / DUT / run state       Run / Stop
           | Session preparation OR required operator interaction
           | Steps / Details / Chart, using the available working area
Results    | Full-width list; focused details at compact sizes
Settings   | Station readiness / common preferences / disclosed site options
```

This plan is specific to bench operation: no decorative dashboard, unrelated
palette or reduction of required controls/authorization to improve a score.
Actual rendered screens and simulated-instrument journeys will determine the
changes; source observations alone do not establish graphical usability.

## Evidence and votes

Baseline capture used the real shell and a real isolated mock-instrument run.
All three reviewers independently inspected both themes and both viewport/text
configurations before voting.

| Category | A | B | C | Median |
| --- | ---: | ---: | ---: | ---: |
| Visual hierarchy/readability | 68 | 68 | 65 | 68 |
| Navigation/discoverability | 74 | 75 | 76 | 75 |
| First-run/test execution | 82 | 78 | 80 | 80 |
| Station/hardware/setup | 70 | 72 | 69 | 70 |
| Space/scroll/resizing | 61 | 58 | 58 | 58 |
| Feedback/error recovery | 69 | 66 | 66 | 66 |
| Keyboard/accessibility | 66 | 72 | 72 | 72 |

The baseline failed the target. Reviewers observed overlapping Results records,
clipped Chart/report actions, unreadable dark notifications, disappearing session
labels and buried readiness. Native probing additionally reproduced an AT-SPI
navigation peer exception that hid the complete page subtree. Actual asynchronous
Settings save reproduced UI-thread violations.

PNGs and native logs remain local; no screenshot publication is part of this review.

## Candidate 2

The first revision passed its expanded real-render journey (124 local PNGs).
Three independent reviewers then scored the new rendered workflows:

| Category | A | B | C | Median |
| --- | ---: | ---: | ---: | ---: |
| Visual hierarchy/readability | 88 | 88 | 88 | 88 |
| Navigation/discoverability | 91 | 90 | 91 | 91 |
| First-run/test execution | 89 | 88 | 90 | 89 |
| Station/hardware/setup | 79 | 82 | 79 | 79 |
| Space/scroll/resizing | 84 | 83 | 85 | 84 |
| Feedback/error recovery | 87 | 88 | 87 | 87 |
| Keyboard/accessibility | 85 | 86 | 88 | 86 |

The new coverage revealed a commissioning overlap (two groups occupied the same
grid column), focused prompt fields moving out of view after resizing, and a
stale completion notice during the next run. A dark failure-filter banner also
retained its light fill. The no-match screenshot showed an open detail rather
than the intended empty list; this was corrected as an evidence-state error.
Those findings were addressed before the final vote. Test infrastructure
also needed real pointer activation: manually raising ClickEvent bypassed
Avalonia's Command execution and did not reproduce a real user action.

## Final independent vote

Candidate 3's initial commissioning score was held at 90 by two reviewers until
active selection/Apply evidence was available. The final matrix demonstrates
selected resources and applied-unsaved target context, and the corrected
commissioning regression passes at both text sizes. Native checks also verify
natural Run focus after confirming a session and meaningful step/result names.
The reviewers independently returned these final votes without consulting one
another's scores:

| Category | A | B | C | Median |
| --- | ---: | ---: | ---: | ---: |
| Visual hierarchy/readability | 92 | 92 | 92 | 92 |
| Navigation/discoverability | 92 | 92 | 92 | 92 |
| First-run/test execution | 92 | 93 | 93 | 93 |
| Station/hardware/setup | 91 | 91 | 91 | 91 |
| Space/scroll/resizing | 91 | 91 | 91 | 91 |
| Feedback/error recovery | 92 | 92 | 92 | 92 |
| Keyboard/accessibility | 91 | 92 | 91 | 91 |

Every final reviewer/category score exceeds 90; all majority medians exceed 90.
These are heuristic review scores for the inspected application and workflows.

### Resulting behavior

- The operator shell exposes navigation, page content and transport controls to
  the native accessibility tree. Toggle navigation, step rows and saved results
  have meaningful names. Embedded Inter gives consistent readable typography.
- Session fields retain visible labels and required cues. Invalid serial, part,
  revision, technician and prompt inputs expose the right explanation and focus
  the actual field. Input is retained, and focused prompt fields remain visible
  after resizing or enlarging text. Successful confirmation focuses Run without
  starting execution.
- Run keeps program/session context and primary actions visible. Full identity
  is selectable in a keyboard-accessible disclosure. Chart controls and readout
  fit the working area; the overview preference is preserved across resize.
- Results uses readable summary rows and full-width compact details, supports
  closing and reopening the same run, restores focus, labels every filter and
  distinguishes empty history from no matches. Saved charts allow page scrolling
  until interaction is explicitly enabled.
- Commissioning separates program/readiness, slot and discovered-resource work
  with one page viewport and persistent target/Apply/Save actions. Identical
  serialized binding metadata appears once; differing binding values remain.
  Original discovery, query, override and save guards are preserved.
- Settings puts station readiness and operator preferences first, retaining all
  site, credential, diagnostic and storage controls in named disclosures.
  Asynchronous settings-save events and final feedback publish on the UI thread.
- New runs clear only their exact previous completion notice; newer run warnings
  and other-source critical alerts remain. Notification and filter colors update
  with the theme. Reports keep signing, copy, viewer, print and cancellation
  controls reachable, with selectable full diagnostics.

### Validation and evidence scope

The real-render harness uses the production shell, isolated settings/data and
mock instruments. It exercises actual session confirmation, prompts, failed and
stopped runs, Results/report guards and commissioning commands. The final matrix
contains 148 local PNGs across light/dark, 1280×800/font 14 and 900×600/font 20,
including actual popup/modal surfaces. Screenshots and native logs remain local.

Native Linux X11/AT-SPI checks reproduce the baseline selection-peer exception,
then verify repaired names, roles, navigation and selection. The full journey
covers required-field validation, keyboard session confirmation, selectable
identity/Escape recovery, Results search and Settings diagnostics. The bounded
final check additionally verifies natural focus on enabled Run (with Stop
still disabled and no execution), and human step/result row names. Native Run
execution, spoken announcements, Windows UIA, physical instruments and a human
newcomer study are outside that proof; actual mock execution is separate E2E
and rendered evidence.

The shared chart font fallback matches authoring PR #239. Operator changes are
on an independent branch based on `latest`; authoring UI changes are not included.

### Final checks

- Release builds succeeded for the operator UI and both test projects.
- View-model suite: **574 passed**, zero failures.
- Real-render E2E suite: **37 passed**, zero failures; the opt-in graphical capture
  test is skipped during normal test runs and run separately for review.
- Separate opt-in capture: **1 passed**, producing **148 verified local PNGs**.
- Canonical repository `format` and dependency `audit` tasks passed.
- The final native metadata probe passed against the frozen operator assembly
  SHA-256 `d2d9c053ab88f9661f8fac435f1b7a18e580b652b247d224f2460dca3ff660d1`.
  The earlier complete native journey and final bounded metadata probe have
  separate logs; the latter does not imply a repeat of every earlier journey.

Local evidence: `/workspace/operator-captures-verified`,
`/workspace/operator-test-results`, and
`/workspace/operator-native-a11y/REVIEW-EVIDENCE.md`.
