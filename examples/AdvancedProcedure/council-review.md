# Advanced fixture procedure Council review

The instructions are the [Council skill at revision a293dc93](https://github.com/josh-hemphill/skills/blob/a293dc9327c526cafa137e702818e43f56ae1985/skills/council/SKILL.md). The application baseline is `f918475c544405bcdf376e16d86b1bf420185e91`. Each grading round records a separate content manifest for the workspace changes and relevant generated inputs.

## Rubric v2

The artifact is the staged Advanced Fixture Procedure, package and data layout, supporting Worker, Run UI, storage and report changes, and reproducible validation instructions. The intended users are operator, test engineer, and plugin integrator. Review the workspace diff and new files, execute or inspect revision-bound Linux evidence, and record observed behaviors separately from source findings. The example is simulation-only. Linux pseudo-terminal tests exercise the physical transport path without a board. Physical fixture validation and execution on Windows or macOS lie outside this grade.

| ID | Category | Required evidence and 9.2 anchor | Target |
| --- | --- | --- | --- |
| OP | Operator workflow and prerequisite control | Use Worker events and a headless Avalonia interaction check. Confirm the separate supplies-off harness and DUT handoffs. Count each prerequisite once in a full run. Inject station and DUT prerequisite failures independently and check reset and downstream suppression. Exercise two orders of three configurations, invalid and repeated choices, cancel and successful rerun, and guidance for an unsafe narrower selection. |
| EN | Procedure breadth and engineer workflow | Inventory rails and consecutive harness checks, serial commands and basic waveforms, differential settling and spectral profiling, loaded clamp and sensor timing, calibrated reciprocal AC ladders, counters, pulse and data decoding, and selectable fixtures. Trace representative analog and digital result rows back to capture and configured inputs. Inspect effective routes, limits, reference traces and corrections. Exercise a table edit through inspection and a staged run and reject a malformed join before measurement. |
| IN | Plugin and distribution | Using the documented staging layout, load the serialized plan in a separate Worker, bind the mock serial address, and locate the tables from another working directory. Run a Linux pseudo-terminal exchange covering ASCII LF/CRLF, deadline, oversize replies, failure invalidation, cancellation and close/reopen. Verify the copy and reuse instructions. |
| EV | Evidence, UI bounds, and reports | Match multiple result rows from one step from producer CSV to saved evidence and both PDFs. Check value, unit, bounds, per-row verdict, and producer UTC timestamp alongside raw point indices and execution identity. Check bounded scores near both endpoints, equality on both endpoints and values outside both limits, distinct same-step UI passbands, and mixed-verdict report rendering. Verify visible PDF column layout and documented numeric display precision. |

Every category uses the same target `>= 9.2/10` with one decimal place. Each category's evidence above is mandatory. A 0–3 fails its purpose. A 4–6 has substantial defects. A 7–8 works on the happy path with material gaps. A 9 has a remaining small defect against the anchor. A 9.2 satisfies every category check. Scores 9.3–9.9 need documented strengths beyond that anchor. A 10 has no material deficiencies within scope. Use UNVERIFIED for required evidence that cannot be assessed. A required unverified assessment makes the category incomplete.

Three independent reviewers use `gpt-6.2-sol` at high reasoning effort and inspect the same frozen artifact. All three submit ballots. Two approvals adopt the rubric. The median grade and at least two threshold-derived pass votes are required. All categories must pass on the same revision, with accepted evidence for every mandatory check.

## Rubric ballots

All three v1 members requested amendments. Version 2 adds explicit source identity and clearer evidence for both prerequisites, independent serial interoperability, table modification, bounded mixed verdicts, and report layout. It retains the Council skill's treatment of required unverified evidence.

Version 2 was adopted by a 2–1 vote. B and C approved its evidence requirements. A requested that OP explicitly require reset on cancellation. The adopted grading checks include a cancelled configuration with no completion event, confirmed fixture reset, and a successful rerun.

## Round 1

Artifact manifest `5a299b11a2f85f1bb3efc67ec1da5614e3697aa9280d0e1ad92f836eea733d84`. All three reviewers verified source integrity and ran or independently inspected staged evidence.

| Category | A | B | C | Median | Pass votes | Result |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| OP | 9.0 | 9.0 | 9.2 | 9.0 | 1/3 | Fail |
| EN | 9.4 | 9.2 | 9.2 | 9.2 | 3/3 | Pass |
| IN | 9.4 | 9.3 | 9.3 | 9.3 | 3/3 | Pass |
| EV | 9.4 | 9.3 | 9.0 | 9.3 | 2/3 | Fail on mandatory layout check |

A found that the worker's recovery instruction was missing from the Run banner. B found that fixture routing had no simulated state to clear and that configuration acceptance was indistinguishable from completion. C verified that six-digit isolation resistance values merged with the lower limit in both PDFs, which overrides the numerical EV majority. Round 2 changes address these findings and check every evaluated PDF row.

## Round 2

Artifact manifest `a035336b49bc5147b03ec78e974bca2c8a76036d8699a580900dd8882d7051af`. Each member verified this manifest against the application baseline and independently evaluated every rubric category.

| Category | Target | A | B | C | Median | Pass votes | Result |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| OP | 9.2 | 9.5 | 9.4 | 9.4 | 9.4 | 3/3 | Pass |
| EN | 9.2 | 9.4 | 9.2 | 9.2 | 9.2 | 3/3 | Pass |
| IN | 9.2 | 9.4 | 9.3 | 9.3 | 9.3 | 3/3 | Pass |
| EV | 9.2 | 9.5 | 9.4 | 9.4 | 9.4 | 3/3 | Pass |

All mandatory checks passed. There were no unverified categories or remaining dissenting findings.

- A reproduced in-flight cancellation and recovery, a calibration change that shifted the measured reciprocal AC delta by −0.005 dB, explicit serial rebinding, and the repaired isolation rows in both PDFs.
- B verified the visible 900×600 operator guidance, a table edit that propagated to the staged run, the recalculated AC correction and decoded payload, explicit serial rebinding, and CSV, JSON and PDF parity.
- C independently computed the spectral residual and decoded payload, checked both cancellation traces and their successful rerun, and rendered and checked the previously overlapping PDF rows.

Strict plan validation returned zero errors and warnings. Each report run produced 177 evaluated rows and 4,600 capture points. The pass run and mixed-verdict run each generated both kinds of PDF. All 177 report rows matched their saved results at documented display precision in each of the four PDFs. The narrowest measured numeric column gap was 3.19 pt.

The staged desktop test exercised normal operator mode, visible recovery guidance, both fixture handoffs, invalid and duplicate config inputs, reverse ordering, and score bounds. The Worker verifier additionally exercised independent prerequisite failures, cancellation while prompting and during a configuration, and inclusive and just-outside score limits. The standalone serial probe exercised the physical byte-stream code path over a Linux pseudo-terminal. The review scope was Linux simulation and pseudo-terminal transport.
