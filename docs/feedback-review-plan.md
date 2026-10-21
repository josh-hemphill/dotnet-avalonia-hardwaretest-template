# Authoring and operator feedback review

Review branch: `fix/authoring-operator-feedback`.

## Implementation plan and ownership

| Area | Owner | Scope |
| --- | --- | --- |
| Navigation and sequence | Sequence subagent | Back history, section targeting, guarded drag reorder and context actions |
| Hardware and readiness | Hardware subagent | Slot/binding vocabulary, typed binding guidance, address deferral, templates, packages and readable findings |
| Operator clearing | Operator subagent | Clear transient run and iteration state while keeping saved Results and session identity |
| Loading feedback | Loading subagent | Visible async workspace loading, startup and program selection feedback, lifecycle guards |
| Shared presentation and integration | Primary agent | Control styling, stable save feedback, guidance structure, validation and review handoff |
| Named section persistence | Section persistence subagent | Versioned document migration, lossless compilation and plan import |
| Named section editing | Section editing subagent | Add, rename, move, unwrap, undo and dependency guards |
| Named section presentation | Section UI subagent | Discoverable actions, collapse, keyboard interactions and layout coverage |
| Section consumers | Section traversal subagent | Preview, readiness, hardware, dependency and build traversal |
| Section execution | Section runtime subagent | Formula scope across sections with repeat iteration isolation |

Each owner inspects existing contracts, implements the change and adds behavioral
regression coverage. The primary agent reviews the combined changes and runs the
affected suites before committing the review branch.

## UI design instructions and review

Source: Anthropic's current
[Frontend Design skill](https://github.com/anthropics/skills/blob/main/skills/frontend-design/SKILL.md),
read from upstream `main` for this work. Apply its plan / review / build / critique
workflow to the existing Avalonia desktop product.

Audience: engineers authoring instrument test plans and operators running them.
The existing workstation themes establish the visual identity. Retain Inter and
the existing named tokens rather than introducing a competing palette:

| Token | Light | Dark |
| --- | --- | --- |
| Canvas | `#F3F5F8` | `#151B25` |
| Surface | `#FFFFFF` | `#202938` |
| Line | `#DEE4EC` | `#354357` |
| Accent | `#245ECA` | `#7EA8FF` |
| Muted | `#5C697C` | `#AFBDD1` |
| Error | `#B42318` | `#FFAAA3` |

Use the existing 22px route / 18px panel / inherited body scale, with semibold
section headings and left-aligned labels. Prefer concise descriptions with
bounded line lengths. Labels identify fields; instructions explain the next
action. Retain native keyboard focus and theme behavior.

```text
Back    Workspace                         Save all / Check
Programs | Program / Hardware / Issues / Environment / Build
         | Section heading
         | Short explanation
         | Labeled controls and relevant actions
Status   | Fixed save-result and error lane
```

Review against the brief: avoid a general dashboard redesign. Keep the existing
tabs and program rail. Use hierarchy within the hardware workflow to resolve its
density, and a fixed feedback lane in the existing status area to prevent save-induced editor movement.
Expanders and dropdowns stretch within their parent width and use consistent
padding. A loading state communicates work without invented percentage progress.

## Review decisions

- **Sections (required):** Setup and Cleanup remain fixed lifecycle anchors.
  Engineers can create named sections inside Measure to organize device systems,
  rename and reorder them, collapse their contents, and move tests between them.
  Removing a section keeps its tests. Sections organize execution without adding
  a formula scope boundary; repeat loops retain their isolation.
  Source documents migrate from version 1 to version 2 on save. The independent
  workspace schema remains version 1. Compiled groups carry an authoring-section
  marker so imported opaque groups retain their existing behavior.
  New sections start at the Measure root; Move to section can nest them. Dragging
  reorders siblings, while cross-section moves use the explicit destination picker.
  Legacy unmarked top-level imported groups retain the previous flattening behavior;
  named sections created here preserve their names and structure on round trip.
  Deploy compiled section plans with the updated Basic plugin from this branch:
  older runtime builds do not support formula inputs crossing section boundaries.
- **Deferred addresses:** maintain the existing library validation of physical
  bindings. Make the hardware-free draft workflow explicit. A first-class
  unbound physical slot would require a separate persistence/readiness contract.
- **Clear run:** clear the current board and iteration evidence only while idle;
  keep persisted Results, selected program and confirmed DUT/session identity.
  Deleting history or changing the DUT remains a separate action.

## Implemented behavior

- Existing tabs retain their focus and selection behavior; Back and Alt+Left
  return through section history.
- Sections & steps offers named section creation and Setup, Measure and Cleanup
  targeting. Selected sections expose rename, collapse and reorder actions; a
  destination picker moves existing tests into or between sections. Empty sections
  accept new recipes. Right-click or Shift+F10 opens step actions. Dragging
  reorders siblings through the same dependency, opaque-node and loop-scope guards
  as keyboard movement, with one Undo.
- Hardware views distinguish the slot name from its binding, label type/address
  and package state, explain supported adapters, and link into Environment for
  recovery. Templates explicitly copy values into programs rather than create
  shared live bindings. Address-deferral help explains the hardware-free draft path.
- Environment separates adapter readiness, VISA readiness, required/installed
  versions and bundled/offline package sourcing. Build findings separate their
  blocking state, code, message and resolution guidance.
- Theme-aware dropdowns and expanders use consistent padding and stretch within
  route width. Guided help separates status, next actions and preferences.
- Save results and errors occupy a fixed lane within the existing status area.
  Long result rows use ellipsis and full-text tooltips; error details scroll.
  Success and failure notifications preserve editor bounds.
- Workspace import and recording scans for program switching run away from the
  UI thread. An accessible indeterminate loading overlay blocks editing and
  keyboard commands. Failed or obsolete loads preserve the existing draft.
- Operator **Clear run** clears the current board, charts, attempts and iterations
  while idle, preserving saved Results, the program and confirmed DUT. Running,
  paused, safety shutdown, prompt and loading states block this action.

## Validation and limitations

Release builds use the installed .NET 10 SDK. Local validation runs the projects'
compiled xUnit v3 executables directly because the sandbox blocks VSTest's TCP
listener; this uses the same test assemblies and assertions. Build with `-m:1
-p:UseSharedCompilation=false` to avoid sandbox compiler-server restrictions.

Rendered Light/Dark snapshots were inspected with a temporary Skia/headless
capture harness, which was removed before the final build. Regression coverage
includes enlarged text and 960×600 layouts, stable save/error geometry, collapse
width, keyboard navigation, pointer drag with Undo, async load ownership/failure,
and operator clear guards and history preservation.

OpenTAP imports expose indeterminate progress and do not support safe cancellation
inside an import. Closing waits for the active load. Cancellable imports would
require additional worker/process ownership design. Windows/native desktop
interaction remains part of normal CI and human review.

Initial feedback implementation results (before named sections):

| Suite | Result |
| --- | --- |
| Authoring UI | 409 passed |
| Final save/error and collapse checks | 7 passed |
| Operator ViewModels | 561 passed |
| Architecture | 45 passed |
| Authoring core broad run (integration category excluded) | 1,791 passed; two test assumptions corrected afterward |
| Updated authoring accessibility and sequence tests | 19 passed on rerun |

The two corrected assumptions were the synchronous program selection binding and
a dependency fixture using Mean GTE, which acquires independently. The latter now
uses a Formula consuming the Acquire output. No product guard was relaxed.

## Named-section review walkthrough

1. Open a program and choose **Sections & steps**. Enter a system name such as
   Power supply, then choose **Add section**. Setup and Cleanup stay at the ends.
2. Select the empty section and add an Acquire recipe. Add a second section, then
   select an existing test and use **Destination section / Move to section**.
3. Rename a section in its inspector; reorder it with Move up/down or sibling drag.
   Dependency checks prevent a consumer from moving before its producer.
4. Collapse a section with its arrow or Left; expand with Right or Expand all
   sections. Remove a section and verify its tests remain in sequence. Undo restores
   the grouping and selection.
5. Save and reopen, then compile the plan. Verify section names/order and tests
   survive, including empty and nested sections. Existing version-1 source documents
   remain readable; saving produces version 2, which older app builds cannot edit.

No decision about whether to support custom sections remains open. The retained
lifecycle policy is fixed Setup/Measure/Cleanup ordering with editable steps inside
those phases; sections organize Measure. Destructive deletion of a section together
with its tests is deliberately absent; removal preserves tests.

Named-section follow-up validation:

- Final focused source/compiler, traversal, editing, runtime, sequence and
  accessibility checks: **116 passed**. This includes source v1 migration,
  unsupported-version preservation, named/empty/nested compiled round trips,
  cross-section formulas, repeat isolation, bounded iteration metadata,
  a 120-test plan, and undo/redo selection/name editing.
- Full Authoring UI suite: **411 passed**; final section/navigation checks after
  the isolated Alt+Left modifier fix: **4 passed**.
- Broad authoring core run (integration category excluded): **1,815 passed** and
  two outdated assertions failed (old removal wording and XAML key-handler wiring).
  Both were corrected to the intended copy and tunnel registration and passed in
  the final **116-test** run above. No product guard was relaxed.
- Architecture suite: **45 passed**; Release builds have no warnings or errors.
- Light/Dark section layouts and a 960×600 viewport were rendered and inspected.
  The temporary screenshot harness was removed before final builds.
