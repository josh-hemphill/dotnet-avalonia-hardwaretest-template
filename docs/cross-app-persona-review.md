# Cross-application persona review evidence

Contract: [cross-app-persona-workflows.md](cross-app-persona-workflows.md), version 2.
Branch: `review/authoring-to-execution`. Prior separate app votes are historical;
they did not include golden-unit references or a joined authoring/bench journey.

## Definition review

Three independent reviewers first proposed and challenged the added workflows.
They converged on keeping the five operator personas and adding author and
release/integration engineer; golden-reference custody is an explicit P5 task.
The initial definition round was held for ambiguous expiry and configuration
compatibility. Both were made explicit before acceptance or implementation scoring.

| Reviewer | D1 | D2 | D3 | D4 | D5 | D6 | D7 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| A | 95 | 95 | 96 | 95 | 96 | 95 | 95 |
| B | 95 | 95 | 95 | 94 | 96 | 94 | 95 |
| C | 95 | 93 | 96 | 95 | 96 | 94 | 94 |

All 21 final definition scores exceed 90. The amended contract is frozen before
implementation scoring. The implementation gate requires all 147 persona cells
and all 42 separate-app graphical cells above 90 on the same candidate, with no
critical evidence gap. Reviewer judgments are not measured user-success rates.

## Combined baseline

Baseline commit: `1b8e0c4c72aac5a4484e1abbcecaa0fe0cf38eaf`. It contains both
authoring #239 and operator #241, but has no golden-reference store, promotion,
selector, reference-dependent runtime acceptance or frozen provenance. The earlier
19 authoring and 15 operator checks are app-local smoke checks, not evidence of
the same newly authored plan crossing the complete handoff. Independent baseline
matrices and implementation/evidence rounds are recorded below when completed.

## Scope and evidence rules

Use actual rendered views for new workflow and graphical claims. Use deterministic
filesystem/VM/runtime tests for corruption, expiry, immutable snapshots, failed
saves and ownership races; identify these separately. A complete joined test must
compile the same newly authored plan, transfer it, discover/execute it through the
operator, persist/report/export it, and return that exact run to authoring.
The profile A/pass versus profile B/fail oracle must verify actual persisted
outcomes and numeric provenance, not only a selector or status label.

Screenshots/native logs remain local. No prior native binary or app-local score
is relabeled as proof of the new combined candidate. Windows UIA, spoken output,
physical instruments/credentials/calibration/safety and newcomer studies remain
outside the stated mock-station evidence.
