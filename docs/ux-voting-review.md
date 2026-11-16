# Authoring UI/UX voting review

The initial review reached the requested 80-point floor. After the user raised
the goal to **strictly above 90 in every category**, three fresh reviewers
iterated on a further candidate. Their final category medians are **91–93**,
and every reviewer scores every final category at least 91.

See [the over-90 experiment](#over-90-experiment) for the current result.
The original 80-point review is retained below as history.

Initial 80-point application source: `3eb5cdabdfd26ebdbe3ec9b3ff5e092c79e0f6ce`.
Baseline: `f918475c544405bcdf376e16d86b1bf420185e91`.
Guidance: [Anthropic frontend-design skill](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md).

## Fixed method

Three reviewers independently score the same seven categories in each round.
The median is the majority vote; the highest individual score cannot establish
acceptance. Scores below 60 indicate a blocked task, 60–79 substantial friction,
80–89 usable workflows with minor friction, and 90 or more a polished experience.
The initial acceptance required every category median at least 80 and no
observed workflow blocker. The rubric remained fixed. The user subsequently
raised the acceptance threshold to strictly above 90; the new experiment uses
the same categories and scoring anchors, with a minimum accepted median of 91.

These are heuristic design ratings, not percentages of users or formal
accessibility certification. Round 1 used source and historical captures because
the managed executor was unavailable. Fresh reviewers, who had not authored the
fixes, independently examined current renders in rounds 2 and 3. They did not
consult one another's votes or this score history before their verdicts.
Round 3 reused those three reviewers and the same rubric.

## Round 1: provisional baseline

| Category | A | B | C | Median |
| --- | ---: | ---: | ---: | ---: |
| Visual hierarchy/readability | 78 | 76 | 77 | 77 |
| Navigation/discoverability | 76 | 78 | 83 | 78 |
| First-plan/step-authoring completion | 84 | 84 | 85 | 84 |
| Hardware/environment setup clarity | 75 | 76 | 76 | 76 |
| Space/scroll/resizing | 69 | 72 | 71 | 71 |
| Feedback/error recovery | 74 | 78 | 82 | 78 |
| Keyboard/accessibility | 77 | 75 | 82 | 77 |

All three reviewers identified cramped Preview space and buried Environment
preparation. Other findings included inconsistent keyboard Add, unclear plan
creation choices, a blank unmatched-search state, difficult diagnostics and
labels that disappeared after entry.

## Round 2: current graphical review

Source: `956350455e4992787107695fe7b467440bce1ebd`.
[Capture workflow](https://github.com/josh-hemphill/dotnet-avalonia-hardwaretest-template/actions/runs/41777298838).

| Category | A | B | C | Median |
| --- | ---: | ---: | ---: | ---: |
| Visual hierarchy/readability | 84 | 84 | 85 | 84 |
| Navigation/discoverability | 84 | 83 | 82 | 83 |
| First-plan/step-authoring completion | 82 | 83 | 82 | 82 |
| Hardware/environment setup clarity | 83 | 85 | 83 | 83 |
| Space/scroll/resizing | 78 | 80 | 79 | 79 |
| Feedback/error recovery | 83 | 83 | 83 | 83 |
| Keyboard/accessibility | 81 | 82 | 80 | 81 |

The first implementation improved six categories to the target. Space remained
below 80: expanded source details competed with the board, metadata preceded
the chart, and the compact inspector repeated headings before configuration.
Reviewers also found faint search guidance and collapsed editing findings.

## Round 3: accepted graphical review

[Capture workflow](https://github.com/josh-hemphill/dotnet-avalonia-hardwaretest-template/actions/runs/41780541506).
Reviewer A inspected 42 current images, B inspected 25, and C inspected 26,
covering both themes and both viewport/text configurations.

| Category | A | B | C | Median |
| --- | ---: | ---: | ---: | ---: |
| Visual hierarchy/readability | 85 | 85 | 86 | 85 |
| Navigation/discoverability | 84 | 84 | 82 | 84 |
| First-plan/step-authoring completion | 83 | 84 | 83 | 83 |
| Hardware/environment setup clarity | 83 | 85 | 83 | 83 |
| Space/scroll/resizing | 82 | 85 | 82 | 82 |
| Feedback/error recovery | 85 | 85 | 84 | 85 |
| Keyboard/accessibility | 83 | 84 | 81 | 83 |

High confidence applies to visual hierarchy and space findings. Navigation and
setup ratings have medium-high confidence; keyboard/accessibility and full
first-author workflow ratings have medium to medium-high confidence because
native assistive technology and real hardware were outside this review.

## Resulting behavior

- Preview shows plotted data initially at 960×600/font20, with chart interaction
  still opt-in. Calculation provenance follows the chart in optional details.
- Full source provenance opens in a selectable, scrollable dialog without
  shrinking the board. Recording selection has its own labelled dialog,
  explicit confirmation/cancellation and ownership checks for changed sessions
  or recording catalogs.
- Compact inspectors remove redundant headings and visual breadcrumbs while
  retaining contextual help, chosen text size and internal padding. Empty
  Measure is explicitly identified as a test-steps section.
- Editing-only findings reveal automatically. Manual collapse remains intact
  during unchanged state, and the Issues page owns their vertical scrolling.
- Environment brings preparation/download/import/cancel/progress/error controls
  before reference material. Exact package requirements and manual helpers remain.
- Guidance leads with status and the next action. Reference information is
  disclosed separately, with one viewport and a fixed Close action.
- Keyboard Add opens the same chooser as the visible button. Search has a
  persistent label and a recoverable no-results state without changing the
  insertion target. Rename commits the old field after remembering return focus.
- Plan creation choices, overview task labels and operator/report field labels
  are explicit. Full operation errors and logs can be selected in a dialog.
- Preview source labels publish with the owned recording snapshot rather than
  exposing a different live selection while background processing is pending.

## Evidence and validation

The successful round-3 capture run produced **80 actual Avalonia/Skia PNGs**:
20 states in light/dark at 1280×800/font14 and 960×600/font20. The artifact
`authoring-ui-review` and 16 bounded image-emitter job logs belong to the
workflow linked above. Artifact retention is 14 days; the opt-in workflow
reproduces the evidence after expiry. Screenshots are not committed to this PR.

Useful filenames include:

- `preview-recording-light-960x600-font20.png`: initial chart visibility.
- `preview-source-details-dark-960x600-font20.png`: readable full provenance.
- `preview-recording-picker-light-960x600-font20.png`: confirmation/cancel.
- `sequence-light-960x600-font20.png`: compact Configure visibility.
- `step-palette-dark-960x600-font20.png`: persistent search label.
- `issues-light-960x600-font20.png`: automatic editing-finding disclosure.

At accepted application source, normal Linux UI validation passed **485 tests**,
with **4 intentional capture skips**, no failures; quality checks passed.
The separate real-render invocation passed all four capture cases.
Required full cross-platform checks remain visible on the PR; their final state
is authoritative for merging.

Meaningful regressions cover first-plan naming/insertion/save/reopen, keyboard
Add without immediate mutation, rename focus and text size, search recovery,
selectable provenance and diagnostics, recording cancellation and identity,
initial chart visibility, independent wheel scrolling, unchanged board geometry
during dialogs, snapshot publication, and real finding navigation.

To request captures on a future authoring PR, include the hidden marker
`<!-- authoring-ui-review -->` in its body before a synchronization event.
The workflow can also be dispatched manually after it is available on the default
branch. Ordinary UI tests retain the fast headless backend; local render captures
use `HT_CAPTURE_UI=1` and `HT_CAPTURE_DIRECTORY`.

## Remaining polish after the initial 80-point review

The accepted floor permits minor friction. Reviewers recorded: early truncation
of recording identity in the compact toolbar; a selected-recording description
for sighted keyboard users; verbose technical paths before some recovery actions;
and compact empty-plan prose that pushes local actions below the fold while the
global Add step remains visible. These do not make controls unreachable.
A focused editor or an adjustable/dedicated compact guidance surface would be a
larger layout proposal.

- [#237](https://github.com/josh-hemphill/dotnet-avalonia-hardwaretest-template/issues/237): safe cancellation of long imports requires process ownership and cleanup.
- [#240](https://github.com/josh-hemphill/dotnet-avalonia-hardwaretest-template/issues/240): first-save recovery was elevated here and subsequently addressed in the over-90 experiment below.
- [#238](https://github.com/josh-hemphill/dotnet-avalonia-hardwaretest-template/issues/238): the compact Preview space finding is addressed by this PR.

Native screen-reader behavior, window-manager interaction and physical instrument
execution are not established by headless screenshots or these ratings.


## Over-90 experiment

Three fresh competing reviewers independently inspected the new candidate,
including actual Avalonia/Skia renders in light/dark at 1280×800/font14 and
960×600/font20. They scored the existing seven categories without consulting
one another's votes. Each returned a complete independent vote; the median
remains the majority rule. These are expert heuristic ratings, not measured
percentages of successful users.

| Category | A | B | C | Median |
| --- | ---: | ---: | ---: | ---: |
| Visual hierarchy/readability | 92 | 92 | 92 | 92 |
| Navigation/discoverability | 92 | 92 | 92 | 92 |
| First-plan/step-authoring completion | 94 | 93 | 93 | 93 |
| Hardware/environment setup clarity | 92 | 92 | 91 | 92 |
| Space/scroll/resizing | 91 | 91 | 91 | 91 |
| Feedback/error recovery | 92 | 93 | 92 | 92 |
| Keyboard/accessibility | 91 | 91 | 91 | 91 |

The first candidate did not clear the new target. Reviewers found arbitrary
word wrapping in enlarged navigation, a blank current recipe after switching
source families, compact configuration fields below the fold, buried readiness,
a home-recovery destination without a home-edit action, and another small nested
findings viewport. They also identified inconsistent confirmation styling,
ambiguous insertion context, stale validation descriptions, and unreadable plot
text. These were fixed before the final votes.

### Resulting workflows

- A normal New test plan uses one short title/device form, with an editable
  generated ID. Hardware/template setup and guided voltage setup retain their
  deliberate full forms. Cancellation preserves local input and restores focus.
- A persistent task bar exposes common work. At minimum width with enlarged
  text, secondary tasks move into More; current-route indication and Back remain
  available. F6 switches between sequence selection and configuration.
- The inspector prioritizes actual configuration and pass criteria, uses a
  concise selected recipe caption, and keeps recipe details in the dropdown.
  Switching Acquire ↔ Mean retains the exact current choice without changing
  the draft; deliberate recipe edits and Undo still work.
- Applied instrument selection and the unapplied editor are adjacent. A themed
  review presents the plan, slot, friendly adapter, address and affected steps;
  raw receipt details remain disclosed. Cancel preserves both saved bytes and
  the editable local proposal.
- Environment starts with readiness and a direct, field-focused Change home
  action. Required versions follow; repository settings and manual download/
  import helpers are disclosed when requested or when sourcing fails.
- Issues puts plain recovery actions before technical details. Expanded saved
  findings use the page's single viewport. Dynamic numeric-setting recovery
  resolves a verified field instead of silently falling back to settings.
- Preview identifies its actual cached source. The recording picker shows the
  selected recording's full immutable identity for keyboard users. Plot fonts
  fall back to an installed family only when the default has no usable glyphs.
- Full operation results and errors are selectable in Details; a reserved compact
  footer keeps save feedback from changing the editor geometry.
- A genuinely compiled-only import can establish its first authoring save.
  Compiled/sidecar hash receipts must still match; externally changed artifacts
  and previously saved then deleted source continue through recovery. This
  addresses #240 without treating missing saved source as a new import.

### Evidence and limits

The local capture matrix now contains **128 PNG states** (32 per theme/size
combination), including loaded binding/review, quick-create validation, current
measurement and criterion, lower Environment sources/manual fallback, expanded
real saved-plan findings, and program removal. Screenshots remain local; new
captures are not published through the opt-in PR capture workflow.

Native Linux X11/AT-SPI probes verified actual application/field names and roles,
text values, destination/title initial focus, Tab, validation descriptions,
quick plan persistence, duplicate-ID focus, advanced-form validation, Escape
and return focus. The application now identifies itself as HardwareTest
Authoring. The probe used Xvfb, Openbox and a private accessibility bus; its
window activation step is part of that virtual-desktop harness.

This evidence does not establish spoken live-region announcements, Orca/NVDA
or braille usability, Windows UIA behavior, physical instrument execution, or a
human newcomer study. Those limits are preserved separately from the heuristic
score. The larger import-cancellation work in #237 remains elevated; no cosmetic
Cancel action was added for an operation that lacks ownership/cleanup support.

Validation:

- Full Linux UI run: **508 passed**, four intentional capture skips, no failures.
- Final first-save extension: **five cases passed**, including an imported
  plan whose sidecar was legitimately absent; receipt comparisons still reject
  external additions/removals.
- Five real-render/font cases passed, producing the 128-image matrix.
- Full authoring run: 1,958 passed and four platform/tooling skips; its only
  failure was the old vertical AXAML row expectation after the horizontal
  settings layout. The corrected chrome/recovery/dataset rerun passed all
  **19 cases**, including that exact failed test.
- Canonical repository formatting and dependency vulnerability audit passed.
- The full native form journey passed against application SHA-256
  `ea037984d91524d9d1a3586db3381221cd0d49753f629769f84d02b1e7afa187`.
  The subsequent first-save receipt edge-case change does not change those forms.

The final small Issues action-placement and generic confirmation-theme fixes
were checked in fresh compact renders before handoff. Required cross-platform
CI remains authoritative for merging; the PR is retained as a draft for review.
