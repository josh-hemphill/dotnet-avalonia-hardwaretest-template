# Advanced Fixture Procedure UI and workflow review

Review instructions come from the [Council skill](https://github.com/josh-hemphill/skills/blob/a293dc9327c526cafa137e702818e43f56ae1985/skills/council/SKILL.md).

The prior review exercised both prerequisite gates, two configuration orders, invalid and duplicate input, cancellation at the prompt and after a configuration activated, and a successful rerun. It matched result rows in storage and reports and exercised one rendered 900 × 600 desktop workflow. The UI review extends those checks to discoverability, competing operator and engineer actions, keyboard input, viewport behavior, and failure triage.

## Proposed rubric UX v2

The artifact is the staged Advanced Fixture Procedure's Run and Results experience, including the handoff from engineer inspection to operator execution and from a run to its result evidence. Each round uses a frozen source manifest. Inspect rendered frames and actual interactions in Linux headless Avalonia at 900 × 600, 1280 × 800 and 1600 × 900. Capture setup, an active prompt and results at each size, including a dark prompt and Results view. Native touch hardware, production benches, other platforms, and assistive-technology certification are outside the assessed evidence.

| ID | Category | Evidence required for 9.2 |
| --- | --- | --- |
| NAV | Task discovery and context | Walk through the clean-session UI using its visible labels. Identify required fields, select the program, start the full procedure, and locate the exact attempt in Results among multiple runs. Use the step jump from an active prompt and verify that it preserves the current request and any entered response. Check operator and engineer entry points. Reviewer walkthroughs establish discoverability evidence. |
| FLOW | Workflow variations and conflict handling | Exercise nondefault ordering, pending versus completed work, stale or invalid input, fast successive prompts, a narrow selection, cancellation and both prerequisite failures. Verify one response advances one request, completed choices cannot run again, the narrow scope explains prerequisites, interruptions leave no unsafe downstream work, and a new full run can succeed. Use staged traces for safety and a controlled interaction fixture for any stress case the plan cannot reproduce. Record operator and engineer conflicts and their resolutions. |
| INPUT | Input accessibility and layout | Inspect compact, medium and wide screens with keyboard and pointer input. Check arrival focus, Tab and Shift+Tab, a validation or incomplete response, and Enter dispatch. Confirm required fields have visible labels and scrollable content does not obstruct Continue, step navigation or Stop. Inspect dark-mode prompt and results. Inspect actual hit points and focus where the app exposes them. |
| EVID | Failure triage and review handoff | Use a history with a pass, a mixed-verdict failure and an interrupted or blocked run. Open the intended run and locate a failing score with its value, unit, limits and verdict plus a sibling from the same execution. Exercise a report or raw-evidence action for the selected run. An engineer follows the supported table inspection and edit path to match a route or correction to staged test provenance. CLI inspection is accepted. Document which parts of the example can be viewed and edited in HardwareTest Authoring. |

Use one decimal place. In each category 0–3 fails its purpose, 4–6 has substantial deficiencies, 7–8 has material gaps, 9.0–9.1 has smaller deficits, 9.2 meets the stated anchor, and 9.3–10 requires additional demonstrated quality. A required UNVERIFIED assessment makes the category incomplete.

Three independent `gpt-6.2-sol` reviewers at high effort review all categories. Two approvals adopt the rubric after receiving all ballots. The final category grade is the median and requires at least two pass votes. All categories and mandatory checks must pass on the same frozen source. Test representative variation combinations and record additional anticipated conflicts separately from executed evidence.

All three v1 members requested clearer evidence. Version 2 defines the review walkthrough, state and input checks, history variants, and the engineer handoff.

UX v2 was approved by A, B and C. The engineer handoff distinguishes the native raw-step inspector from the supported source and table editing path. Review A framed the evidence criterion, B the contexts, and C the fit between observations and rubric definitions.

## Baseline findings

The first UX round reviewed application manifest `a035336b49bc5147b03ec78e974bca2c8a76036d8699a580900dd8882d7051af`. The baseline missed all four grades.

| Category | A | B | C | Median |
| --- | ---: | ---: | ---: | ---: |
| NAV | 8.7 | 8.7 | 8.0 | 8.7 |
| FLOW | 9.0 | 9.0 | 9.0 | 9.0 |
| INPUT | 8.7 | 8.5 | 8.0 | 8.5 |
| EVID | 8.5 | 8.7 | 7.8 | 8.5 |

The compact history hid run identity and outcome. A blocked attempt's notice competed with the next run's active prompt. The failed run's empty attempt ledger hid its stored step failures, and the failed score appeared after reports, history, and many passing measurements. The baseline also left some keyboard, handoff, and evidence actions unverified.

## Workflow variants

| Workflow conflict | Check and expected behavior |
| --- | --- |
| Starting quickly versus using the correct session | A clean session identifies the DUT and technician. The selected program and run identity carry through to Results. |
| Engineer retry versus fixture readiness | A narrow selection produces no measurements and explains how to recover. Both independently injected prerequisite failures block dependent work. |
| Flexibility at the bench versus bypassing configurations | Nondefault order succeeds. A missing selection leaves the request open. The UI lists remaining choices and shows progress. The Worker rejects invalid or repeated values supplied directly. |
| Reviewing another page versus acknowledging a prompt | The shell action brings the operator to the waiting request. Returning to the prompt requires a separate confirmation. Show this step preserves the selected response. |
| Fast input versus a changed request | A controlled response test proves an earlier request's draft cannot be sent to its successor. The Worker verifier exercises successive real prompts and invalid and duplicate retries. |
| Stopping versus counting partial work | The staged UI exercises Stop Run at a waiting handoff. Worker evidence also stops during an active configuration. Started and completed events distinguish interrupted work, and a full rerun succeeds. |
| Dense evidence versus finding a failed measurement | A history with pass, fail, cancel and blocked outcomes allows selection by visible run ID. The failed row exposes both limits, and the reviewer can find a passing sibling and open the selected run's PDF. |
| Changing a table versus changing procedure structure | The engineer inspects and changes the JSON routes, limits and calibration outside Authoring. The exact run records its case routes and table provenance. Native Authoring imports a single raw node, preserving the 42 nested steps and table pointer. Its inspector exposes the type and read-only XML. |

These checks use representative combinations across the staged Linux simulator and headless desktop. Unexercised variations include shift handoff between two operators, hardware timing and physical touch, and a native Authoring save-and-package flow for the custom procedure. Native support for editing the fixture dependency tree, matrix and calibration tables, and custom analyses remains a separate design gap.

## Round 2

Manifest `f0a298bc79b2273bdd2d34d44bc995bfc1a167ae5adfdf629ebe935bc087835c`.

| Category | A | B | C | Median | Pass votes |
| --- | ---: | ---: | ---: | ---: | ---: |
| NAV | 9.3 | 9.3 | 9.3 | 9.3 | 3/3 |
| FLOW | 9.0 | 9.1 | 9.0 | 9.0 | 0/3 |
| INPUT | 9.2 | 9.2 | 9.2 | 9.2 | 3/3 |
| EVID | 9.1 | 9.3 | 9.4 | 9.3 | 2/3 |

The compact history, prompt controls, and opened-run evidence passed their walkthroughs. All three members found that live harness rows could still show Pending during configuration selection, after the Worker had passed those rows. A separately found that an earlier failed attempt could appear in the headline of a passing rerun. The next revision keeps pending UI updates per step and labels earlier failures in Results.

## Round 3

Manifest `8fa0ee98a9bbbd761b353b1b26abd13ab847ac09a049adc1a617c56c051e7b63`.

| Category | A | B | C | Median | Pass votes |
| --- | ---: | ---: | ---: | ---: | ---: |
| NAV | 9.1 | 9.1 | 9.1 | 9.1 | 0/3 |
| FLOW | 9.1 | 9.2 | 9.3 | 9.2 | 2/3 |
| INPUT | 9.2 | 9.2 | 9.2 | 9.2 | 3/3 |
| EVID | 9.4 | 9.3 | 9.4 | 9.4 | 3/3 |

The stage now showed all four harness steps passed during configuration, and Results labeled prior failed attempts separately from a current pass. The wide frame still had an initial prompt ownership defect. A configuration prompt briefly named an earlier attenuation measurement as its waiting step. The next revision carries the producing step's stable ID in the request. The staged test checks the initial owner, injects late measurement telemetry, and exercises Show this step with a selected answer.

## Authoring boundary

The staged procedure was imported into an inspection workspace using the example plugin. The Authoring application presented one raw procedure node. Its Advanced inspector showed the read-only XML, including the 42 nested steps and the external table directory. The example has no native editors for its dependency rules, fixture choices, route and calibration tables, or custom analysis.

Saving an inspection copy through Authoring wrapped the custom root in a generic measurement group. The saved plan failed the fixture's preflight because the fixture root expects to be directly under the TapPlan. Use the example source and OpenTAP Editor for procedure structure. Use the JSON tables, the CLI inspect command, and the example staging path for routing, calibration, references, and limits.

## Round 4

Manifest `3ad993e1a15e639b5a75e60110e5d6215d291d884bf10dccebc301b26b4a1049`.

| Category | A | B | C | Median | Pass votes |
| --- | ---: | ---: | ---: | ---: | ---: |
| NAV | 9.4 | 9.3 | 9.4 | 9.4 | 3/3 |
| FLOW | 9.4 | 9.2 | 9.3 | 9.3 | 3/3 |
| INPUT | 9.1 | 9.2 | 9.2 | 9.2 | 2/3 |
| EVID | 9.4 | 9.3 | 9.4 | 9.4 | 3/3 |

All three members confirmed the actual configuration step owned the waiting card and the step jump. A objected that the inline validation remained visible after the operator selected a valid option. B and C also observed the delayed feedback but judged the interaction usable. The next revision clears the validation message when the choice is corrected and wraps the report preview toolbar so its external viewer action fits at 900 pixels.

## Rounds 5 and 6

Round 5 passed after the prompt cleared obsolete validation and the compact report toolbar wrapped. The separate architecture check found a feature file above its 600-line limit. Round 6 adjusted the formatting of one existing guard and reran all viewport scenarios. The final manifest is `1f2d46b334f5596d29c6c5cfb6a2343d2fe988d95bc083d6f42a5063efb1a4c9`.

| Category | Target | A | B | C | Median | Pass votes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| NAV | 9.2 | 9.3 | 9.3 | 9.4 | 9.3 | 3/3 |
| FLOW | 9.2 | 9.4 | 9.3 | 9.3 | 9.3 | 3/3 |
| INPUT | 9.2 | 9.4 | 9.3 | 9.4 | 9.4 | 3/3 |
| EVID | 9.2 | 9.4 | 9.3 | 9.4 | 9.4 | 3/3 |

All mandatory criteria passed. The final staged desktop checks passed at 900 × 600 light, 1280 × 800 light with engineer mode, and 1600 × 900 dark. The targeted regressions passed 70 UI checks and 24 host checks. The final architecture suite passed all 45 checks. Strict plan validation found zero errors or warnings.

- A confirmed the request owner, retained choice, correction feedback, and exact opened run and PDF in the refreshed frames.
- B checked the completed harness and fixture order, Stop and Review behavior, and the matching stored mixed-run evidence.
- C checked pointer and keyboard paths, compact prompt and report layouts, and the preserved Authoring node and failed copy resave.

During a pending handoff, an in-flight history entry can display Unknown until the attempt finishes. The Review prompt and Stop controls remain available. The tested scope is the staged Linux simulator and representative headless desktop scenarios.
