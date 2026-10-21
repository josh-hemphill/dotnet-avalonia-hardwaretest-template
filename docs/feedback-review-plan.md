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

- **Sections:** the plan schema has Setup, Measure and Cleanup in a fixed order.
  Section targeting and editing fit that model. Arbitrary named or reordered
  sections would require schema/compiler and migration design.
- **Deferred addresses:** maintain the existing library validation of physical
  bindings. Make the hardware-free draft workflow explicit. A first-class
  unbound physical slot would require a separate persistence/readiness contract.
- **Clear run:** clear the current board and iteration evidence only while idle;
  keep persisted Results, selected program and confirmed DUT/session identity.
  Deleting history or changing the DUT remains a separate action.

## Implemented behavior

- Existing tabs retain their focus and selection behavior; Back and Alt+Left
  return through section history.
- Add & edit steps now offers Setup, Measure and Cleanup targeting. Right-click
  or Shift+F10 opens step actions. Dragging reorders siblings through the same
  dependency, opaque-node and loop-scope guards as keyboard movement, with one Undo.
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

Local results:

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
