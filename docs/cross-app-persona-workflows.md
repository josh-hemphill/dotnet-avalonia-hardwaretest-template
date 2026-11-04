# Cross-application persona workflows

Version 2, proposed before new implementation scoring. This extends, rather than
replaces, [operator contract version 1](operator-persona-workflows.md). The prior
operator and authoring scores do not establish compliance with these additions.
Scope is both applications and their actual compiled-file/recording handoff on
`review/authoring-to-execution`. All existing operator identity, ownership, health,
credential, saved-evidence and accessibility requirements remain binding.

## Personas and decision rule

| Persona | Responsibility and goal |
| --- | --- |
| P1 First-day operator | Prepare the assigned program, DUT and required reference inputs; execute deliberately and find the exact result |
| P2 Repeat operator | Process successive DUTs and deliberately retain or change per-test reference choices without confusing evidence |
| P3 Shift operators | Hand off an idle station, reference context and outstanding problems without transferring old credentials |
| P4 Quality reviewer | Trace verdicts, reference inputs, source evidence and reports; export the exact immutable result |
| P5 Commissioning/reference technician | Commission hardware, bootstrap and maintain named golden references on the bench, diagnose and recover |
| P6 Test author | Create/edit from empty, declare measurements and reference criteria, save/preview/compile and inspect returned recordings |
| P7 Release/integration engineer | Resolve packages, transfer the intended current artifacts, verify bench discovery and close the recording feedback loop |

Golden-reference custody is an explicit task of P5, not a new authentication
role. Engineer presentation controls tool visibility; it does not grant authority.
No enterprise approval system, physical calibration certification or emergency
stop certification is introduced.

Three independent reviewers first challenge and score the definitions against
the existing D1–D7 rubric: realism, preconditions, end-to-end path, recovery,
continuity, inclusive context and testability. Every one of the 21 definition
cells must be at least 91, with no missing critical journey, before scoring the
implementation. Definitions are then frozen; scope changes require a new
definition vote and may not silently remove a failed acceptance branch.

Each reviewer scores every persona against C1 completion/correctness,
C2 discoverability, C3 efficiency/control, C4 recovery, C5 integrity,
C6 access/space and C7 feedback/handoff. **All 147 cells must exceed 90 on the
same combined candidate.** Missing critical evidence or a critical failure caps
its relevant metric at 89. Unknown is not assumed to pass. No average, median or
best reviewer compensates for a failing cell.

Material UI changes receive independent graphical votes for **each app** using
hierarchy/readability, navigation/discoverability, primary task completion,
setup/readiness, space/scroll/resizing, feedback/recovery and keyboard/access.
All **42 reviewer × app × category cells** must exceed 90. Scores are expert
suitability judgments, not measured user-success percentages.

## Shared reference semantics

The initial supported reference is a scalar number derived by an explicit
**arithmetic mean of all samples for one declared metric and producer in that
source acquisition**, including repeat invocations. It is not waveform matching,
per-iteration matching or an arbitrary calibration equation. Capture and authoring
must display this reduction. Missing/nonfinite samples, mixed units or ambiguous
producers cannot silently contribute a value. Stable metric and producer identities
are required; display names alone cannot bind a reference.

Each reference-dependent criterion declares its stable rule/input key, target
metric/producer, unit and supported comparison. Supported inclusive comparisons:

- Absolute: `abs(measuredMean - referenceValue) <= tolerance`, in the metric unit.
- Relative: `100 * abs(measuredMean - referenceValue) / abs(referenceValue) <= tolerance`,
  in percent. A zero reference is invalid for this rule and must block it.

Tolerance must be finite and nonnegative. Unsupported or incomplete declarations
remain editable in a saved draft but cannot silently compile/deploy as a valid
reference criterion. Existing fixed acceptance rules still apply; reference
acceptance is an additional explicit requirement for a production pass.

Named profiles contain keyed scalar values. Each affected criterion shows which
profile/value/unit it uses; independent choices are possible where declarations
differ. An unexplained global selector that silently feeds unrelated tests is
insufficient. No missing selection defaults to zero, latest or previous profile.

Reference compatibility includes stable program/metric/producer identity, units,
compiled plan/sidecar revision and effective station configuration. A changed
artifact pair or relevant configuration requires explicit requalification rather
than silent reuse. The configuration fingerprint includes effective VISA mode,
the program's effective slot names/types/resources, its applied station parameter
overrides and any active debug acquisition/criterion overlay. Normalize and sort
these keyed inputs before hashing. Exclude theme/layout, current DUT/operator,
unrelated programs/settings and reference choices themselves, avoiding circular
compatibility. Simulated/physical VISA provenance is recorded and matched;
mock references cannot silently feed physical VISA execution. This label describes
the VISA backend, not whole-plan physical safety.

Every saved reference owns its numeric values and source metadata, independent
of source-run retention. Identity includes a stable ID/revision and content hash,
human name, source RunId/DUT/operator/time, program/artifact/configuration identity,
scope, reduction, producer/metric/unit and backend provenance. Replacement creates
a new immutable revision or profile identity. Retirement blocks future use while
historical dependent runs retain their own exact snapshots.

The technician may set an optional UTC expiry when saving a profile; unset means
no automatic expiry. A profile is expired when the injected current UTC clock is
greater than or equal to its expiry. Check at selection and again when accepting
Run. Validity at that accepted instant is frozen: expiry/retirement later during
an active run does not redirect or rewrite it. Subsequent runs must revalidate.
Existing station clock-health gates remain effective; no new clock authority is
claimed. The capture UI explains its validity duration and validation errors.

## W1 Author an ordinary or reference-dependent program — P6

Start from an empty plan or an existing supported program. Set a unique program
ID/title, DUT/operator requirements, supported instrument/binding, measurement,
prompt/input and acceptance criterion. For selected tests, declare the reference
input/unit/reduction/comparison/tolerance. Preserve declarations through undo,
save, reopen, compilation and packaging. Save incomplete work without claiming
deployment readiness; issues lead to the exact editable field. Compilation and
sidecar-only saving remain distinguishable.

Preview identifies its explicit recording/mock data and reference inputs; it is
not bench execution or creation of an approved bench reference. Returned recordings
expose their immutable reference provenance. Proposed criterion edits can be
evaluated against explicitly selected recorded values without mutating that run.
Future/corrupt documents and external changes preserve bytes and require recovery.

## W2 Resolve dependencies and hand off current artifacts — P6/P7/P5

On a cold/missing-package environment, inspect supported adapter types and actual
missing dependencies. Use automatic package download when configured, otherwise
the manual helper; opening a URL is not installation success. Download/cancel/
install failures retain edits and expose the next action. Existing package-source,
version, saved-build-snapshot and private SDK namespace protections remain active.

Save/compile the intended revision, validate it, and transfer the exact plan and
sidecar together with an inspectable identity/receipt. A guided manual bench-file
handoff is supported; automatic live deployment is not promised. Refuse stale,
mixed, incomplete or unsupported artifacts and collisions with built-in factory
IDs. The idle operator discovers/reloads the unique program and can inspect the
loaded ID/path/revision. A new disk copy cannot replace an active run's plan.

## W3 Bootstrap and save golden references — P5

Start with an idle commissioned bench and identified golden DUT/operator. Select
an explicit **reference acquisition** route before execution. It must collect
declared producer data without requiring the first missing golden reference.
Dependent reference acceptance is explicitly unevaluated; acquisition cannot
claim a production full-suite/certification pass. Ordinary Run never bypasses
the reference gate. Existing credentials, health, ownership and Stop still apply.

After terminal successful acquisition, inspect the exact source and candidate
values, select covered declared metrics, name the profile and explicitly save.
Selected acquisition can qualify only its actually covered metrics and must say
so; it cannot establish whole-program coverage. Failed/error/cancelled/incomplete
evidence cannot be promoted. Missing/mixed/nonfinite/ambiguous data or unknown
source identity refuses promotion while retaining useful input. Legacy runs
without necessary provenance are not silently upgraded.

Save is atomic and reports the actual saved identity, never a false success.
Restart/reload discovers the same named profiles and values; duplicate names
remain distinguishable. Source-run retention does not destroy a saved profile.
Create a replacement with new immutable identity, or retire a profile explicitly.
Disk/read-only/schema failures preserve prior good data and useful edits.

## W4 Select references and accept a run — P1/P2/P3

Choose the assigned program, required per-test references and DUT/operator context.
Each choice exposes its distinguishing identity, exact value/unit, reduction,
source and affected equation/tolerance. Show incompatible/unavailable reasons and
the capture/technician correction route. Required missing/corrupt/future/retired/
expired/incompatible inputs block ordinary execution. No silent substitution.

Changing reference context while idle invalidates necessary confirmation. At
accepted Run, revalidate and freeze exact selected values, rules and provenance.
Delayed UI work rechecks program/session ownership; selection, edit, replacement,
retirement or file changes cannot redirect active execution. Stop stays reachable
across pages. Terminal output belongs to this frozen context, and it survives
restart. Shift handoff exposes retained reference context explicitly while old
credentials are cleared; it cannot transfer a live run.

## W5 Execute reference criteria and inspect evidence — P1/P2/P4

The actual declared measurements consume the frozen numeric references. Results
combine existing fixed acceptance with the declared reference acceptance; a failing
reference criterion cannot produce a persisted production pass. Missing current
samples or unit/producer mismatch fails honestly and cannot reuse old samples.
Repeat/selected scope is explicit and cannot be mistaken for a full-suite pass.

Results, generated reports and export identify exact reference IDs/revisions/
hashes/source and the values/units/effective rules used, alongside measured values,
scope and verdict. Preserve those bytes and interpretations after source retention,
reference retirement/replacement, program edits and subsequent runs. Existing
destination/signing/cancellation/evidence guards remain active.

## W6 Return evidence and iterate — P4/P6/P7

Export the exact operator run and import it into the matching authoring workspace.
Select it by actual identity, inspect data and frozen reference provenance, preview
proposed edits, then save/compile/validate and hand off a changed revision. Reload
only while idle, explicitly requalify incompatible references, execute again and
verify earlier evidence is unchanged. Missing/mismatched recordings and unsupported
schemas retain current work and explain recovery. Recording selection never changes
the bench's execution reference choices.

## Required joined evidence and graphical coverage

A single evidence chain must use the **same newly authored unique-ID program**:
create/edit → save/compile → transfer exact artifacts → real operator discovery
→ bootstrap/capture/save/restart/select → execute → persist/report/export → import
that same run into authoring → edit/recompile/redeploy and recover compatibility.
Unrelated app-local smoke tests do not establish this handoff.

Numeric oracle: the same authored measurement with compatible profile A produces
the expected pass and profile B the expected fail. Verify persisted outcome,
effective numbers/rules and report/export provenance. Then replace/retire the
profile and edit the program; old run/report/export evidence must remain unchanged.
Boundary tests cover first capture, selected coverage, zero-relative denominator,
units/producers, restart, stale callbacks, mid-run mutation, failed writes,
corruption/future schema and source retention.

Both apps require fresh actual rendered evidence in light/dark, 1280×800/font14
and 900×600/font20, including dialogs, required-field recovery and reference rows.
Use actual keyboard flows, persistent labels, native distinguishing names and
natural return focus. Charts retain explicit scroll ownership. Keep images and
native logs local. Identify which claims are GUI, service/VM, native API or manual.
Windows UIA, spoken output, physical devices/credentials/calibration/safety and
human newcomer studies remain separately unverified; they cannot be inferred
from mock tests or reviewer percentages.
