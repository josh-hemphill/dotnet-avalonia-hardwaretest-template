# Authoring UI/UX voting review

Baseline: `f918475c544405bcdf376e16d86b1bf420185e91`.
Review guidance: [Anthropic frontend-design](https://github.com/anthropics/skills/blob/a5333457c414d20d625f307df945842c0952ecc3/skills/frontend-design/SKILL.md).

Three independent reviewers score the same seven categories. The median is the
majority vote; the highest individual score cannot establish acceptance. Scores
below 60 indicate a blocked task, 60–79 substantial friction, 80–89 a usable
workflow with minor friction, and 90 or more a polished experience. Acceptance
requires a median of at least 80 in every category and no unresolved workflow
blocker. Scores are heuristic design judgments, not percentages of users or
formal accessibility certification. Keep this rubric fixed between rounds.

| Category | A | B | C | Median |
| --- | ---: | ---: | ---: | ---: |
| Visual hierarchy/readability | 78 | 76 | 77 | 77 |
| Navigation/discoverability | 76 | 78 | 83 | 78 |
| First-plan/step-authoring completion | 84 | 84 | 85 | 84 |
| Hardware/environment setup clarity | 75 | 76 | 76 | 76 |
| Space/scroll/resizing | 69 | 72 | 71 | 71 |
| Feedback/error recovery | 74 | 78 | 82 | 78 |
| Keyboard/accessibility | 77 | 75 | 82 | 77 |

Round 1 is provisional: current source and historical committed captures were
available, but the managed execution environment failed before commands could
run. Historical captures omit the newest plan-title and download controls.

## Findings and implementation

- Preview: disclose full provenance and recording selection on demand, hide an
  empty recording picker, and reserve most of the initial route for the board.
  Keep chart interaction opt-in and independent board scrolling.
- Environment: put preparation, one-shot download, offline import, cancellation,
  errors and progress before readiness reference material. Keep source helpers
  available and disclose verbose manual guidance.
- Guidance: lead with status and the next action; put reference information in
  Plan details. Retain one scroll viewport and the fixed close action.
- Add step: keyboard and command-palette entry open the same chooser as the
  visible button. An unmatched search explains the empty state and offers Clear
  search without changing the draft or insertion target.
- Rename and diagnostics: respect the chosen text size; provide selectable full
  error/log text through a keyboard-accessible Details action.
- Navigation: distinguish the guided voltage example from New test plan; remove
  numbering that implied independent tasks had to follow a fixed order.
- Operator/report definitions: keep field labels visible after entry.

## Graphical evidence and validation

The opt-in `GraphicalReviewCapture` test uses actual Avalonia/Skia rendering in
CI, independent of the ordinary fast headless backend. The dedicated workflow
captures light/dark at 1280×800/font14 and 960×600/font20. PNG artifacts and their
source commit identity are retained with the workflow run. Images are also
encoded in job logs so connected reviewers can inspect them when the managed
executor is unavailable.

Round 2, fresh captures and behavioral validation are pending. No acceptance
claim is made while they remain pending.

## Elevated follow-up

[Issue #237](https://github.com/josh-hemphill/dotnet-avalonia-hardwaretest-template/issues/237)
tracks safe cancellation of long OpenTAP imports. It requires process ownership
and cleanup work; a presentation-only Cancel button would misrepresent support.
Native screen-reader interaction, Windows window-manager behavior and physical
instrument operation are outside Linux headless graphical verification.
