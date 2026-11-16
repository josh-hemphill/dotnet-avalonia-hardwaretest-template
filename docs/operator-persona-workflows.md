# Operator persona workflows

## Contract and decision rule

Version 1, defined before implementation scoring. Scope: the standalone operator
application and its supported mock-station GUI workflows, building on PR #241.
The authoring application is outside this review. These are task personas,
not claims of user research or access-control roles. A site supplies the approved
program, physical work instructions and trained operators. Engineer presentation
reveals tools; it does not authenticate or authorize anyone. Existing badge,
attestation, deployment-owned setting and bench-ownership policies remain binding.

Three independent definition reviewers propose and challenge this contract.
Definitions pass only when **each reviewer scores every D metric at least 91**
and identifies no missing critical journey. Only then do the scoring reviewers
assess the app. Each scores all seven C metrics for **each of five personas**.
Implementation passes only when **all 105 reviewer × persona × metric cells are
at least 91 on the same candidate**, with every critical acceptance evidenced.
No median, overall average or best reviewer can compensate for a failing cell.
An observed critical failure or missing critical evidence prevents acceptance
and caps its relevant metric at 89. A 90 is still below the target.

Scores express expert suitability judgments, not measured user-success rates.
0–59: blocked or incorrect; 60–79: substantial friction; 80–89: unresolved gap;
90: borderline; 91–100: evidenced completion with only nonessential polish left.
Unknown behavior is recorded as unknown rather than assumed to pass. Regression
evidence may combine real rendered GUI journeys and deterministic service/VM
checks; the evidence ledger must say which one proves each branch.

## Shared interaction and integrity requirements

- **X1 Context:** verify the selected program's stable identity (even if display
  names match), DUT, technician and effective simulated/physical VISA mode before
  starting. VISA mode describes the instrument backend, not whole-plan safety or
  proof of physical operation. Confirmation and scanner Enter never auto-start.
- **X2 Access:** critical controls have persistent labels, meaningful native names
  and keyboard focus. Light/dark, 1280×800/font 14 and 900×600/font 20 preserve
  critical actions, explanations and the focused field. Charts do not take page
  scrolling unless interaction is deliberately enabled. No pointer-only tooltip
  may be the sole source of a critical instruction.
- **X3 Ownership:** incompatible DUT/program/session changes are refused during a
  run or required prompt; cooperative Stop remains reachable across navigation.
  Stop is not a certified physical emergency stop. Wait for terminal completion
  before changing operator or DUT. No in-flight ownership transfer is promised.
- **X4 Evidence:** saved runs retain their original RunId, SessionId, program,
  DUT, operator, verdict and attempts. A new run cannot overwrite prior evidence.
  Display clearing and session changes do not delete saved runs. Full-suite and
  selected-step attempts remain distinguishable. Signing/cancellation cannot
  redirect an action to another run, artifact or destination.

## P1: First-day operator

A physically trained operator knows the assigned DUT and work instructions but
has never used this app. Goal: finish the assigned test and identify its result
without code, hidden engineer menus or app-specific verbal coaching.

### P1.1 Prepare the assigned job

- **Start/trigger:** idle, commissioned station; site supplies the assigned
  program and DUT. Open Home for the first job.
- **Route:** Home → Run → choose assigned program → verify stable program ID and
  VISA mode → fill/scanner-enter required serial, part, revision and technician,
  or present a badge if policy requires one → Confirm → inspect ready context.
- **Acceptance:** every required field remains labeled after entry; missing
  fields explain and focus the actual control while retaining unrelated values.
  Trim scanner suffixes. Confirm enables/focuses Run without executing anything.
- **Recovery:** correct a mistaken program before Run; an empty/unloadable
  catalog directs the operator to a station technician/approved program rather
  than requiring plan authoring. Readiness/health/policy blocks explain the next
  step and remain effective after navigation. Unavailable badge is not replaced
  by typed identity when policy requires credential presence.
- **Output:** confirmed, correctly attributed idle session, or an explicit block
  and escalation route with no execution. Evidence: GUI preparation and recovery
  plus existing gate/credential tests; X1–X3.

### P1.2 Execute and finish

- **Start:** confirmed P1.1 session. Explicitly activate Run.
- **Route:** observe progress → read required instructions → enter/acknowledge
  prompts → Continue → terminal verdict → View saved result → inspect exact run.
- **Acceptance:** invalid numeric/required input cannot advance and retains
  valid entries; explanation/focus/Continue/Stop fit after resize. Passed, Failed
  and stopped outcomes are distinct. The saved record matches this program,
  DUT and technician; a completion action opens this exact RunId.
- **Recovery:** correct input; cooperatively stop an interrupted test; inspect a
  failed/stopped saved run before deciding whether another run is appropriate.
  Never imply idle means pass. Output: persisted terminal record; X2–X4.

## P2: Experienced repeat operator

A trained operator processes successive DUTs on the same station and needs
fast deliberate repetition without confusing prior results or current identity.

### P2.1 Same DUT or next DUT

- **Start/trigger:** previous run terminal; same program remains selected.
- **Route:** inspect last result → either deliberately rerun the same DUT,
  reconfirming Same DUT when site/idle policy requires, or Change session for a
  new DUT → re-enter required identity → Confirm → explicitly Run.
- **Acceptance:** a visible action and explanation distinguish session change,
  Same DUT and clearing displayed results. New-DUT entry requires confirmation;
  previous saved runs remain. Required re-confirmation cannot be bypassed by
  navigation or keyboard. Distinct runs have distinct RunIds and correct context.
- **Recovery:** stale warning offers Same DUT or Change session; invalid input
  returns to its field. No requirement to retain a badge across a new session.
  Output: correctly attributed successive run(s); X1–X4.

### P2.2 Diagnose, stop and clear the working display

- **Start:** failed/terminal run, or a running test that needs interruption.
- **Route:** inspect failed step/attempts → deliberately choose full Run versus
  specific Run Selected if available → stop cooperatively if needed → await
  terminal state → inspect saved evidence → Clear run if a clean display helps.
- **Acceptance:** selected-step scope is visibly identified; it cannot be
  mistaken for a full-suite pass. Stop works away from Run. Clear run keeps the
  program/confirmed session and saved records but clears displayed attempts.
  A new accepted run removes only its stale completion notice, preserving newer
  warnings and critical notices. Output: retained failure/attempt evidence;
  X2–X4. Physical emergency-stop behavior remains a separate site procedure.

## P3: Incoming and outgoing shift operators

Two people share one station sequentially. Goal: leave the previous job's outcome
available and attribute new work to the incoming person without inheriting an
old credential. This is not scheduling or live-run user transfer.

### P3.1 Hand off an idle station

- **Start/trigger:** departing person's run terminal; if active, finish or Stop
  and wait. Identify the previous saved result and any unresolved failure.
- **Route:** inspect current context/previous result → Change session → verify
  old DUT, technician and credential cleared → incoming person identifies/badges
  and enters the same or a new DUT → Confirm → explicitly start the next run.
- **Acceptance:** session identity changes; program remains selected; saved
  departing record is immutable and searchable. Incoming run records incoming
  identity. Neither Clear run nor Same DUT is presented as technician handoff.
- **Recovery:** changing while running is refused. A pending outgoing badge
  response, including late success/failure after cancellation, cannot populate
  the new session or overwrite its errors/busy state. A fresh incoming capture
  remains possible. Output: clean new session and retained old record; X1–X4.

### P3.2 Resume after idle or a context interruption

- **Start:** session has soft-warning/stale/every-run reconfirmation, or program
  changes while identity capture is waiting.
- **Route:** read why Run is blocked → Same DUT only for the same operator/job,
  otherwise Change session → reconfirm required context → explicitly Run.
- **Acceptance:** expiry and program boundaries invalidate stale badge work;
  there is no auto-resume or silent credential inheritance. Valid context and
  saved evidence remain inspectable. Output: deliberate new/reconfirmed session.

## P4: Quality reviewer or supervisor

An evidence reviewer needs to explain a saved outcome and deliver the intended
artifacts. This does not confer batch-release authority or invent new RBAC.

### P4.1 Locate and understand an exact record

- **Start/trigger:** know DUT/program/operator/time or RunId; may have multiple
  runs for the same DUT by different operators. Open Results independently of Run.
- **Route:** search/filter → open exact run → inspect identity, verdict, failure,
  attempts/scope and available reports → close/reopen or refine filters.
- **Acceptance:** full identity/time/verdict and failure context are inspectable;
  full versus selected attempts and working versus issued/verified reports are
  distinct. No matches differs from empty history; reset restores records.
  Selection/filter/focus are retained across detail closing and error recovery.
  Output: the intended exact record without modifying evidence; X2/X4.

### P4.2 Deliver the chosen evidence

- **Start:** P4.1 record open; two different export destinations exist.
- **Route:** choose report/destination → export/copy/print under existing site
  signing policy → handle any presence/attestation → inspect completion receipt.
- **Acceptance:** choosing the non-first destination actually writes the exact
  selected run.json and correct working/issued PDF/attestation files there.
  Refresh and delayed signing cannot silently switch the run or destination.
  Success identifies the actual destination; original issued/source bytes stay
  unchanged. A signer credential is not itself proof that the test passed.
- **Recovery:** removed destination requires a fresh explicit choice and retry;
  do not fall back silently. No target/write failure/missing PDF/failed presence/
  cancellation gives actionable non-success and retains context. Newer schema
  cannot be mutated/signed. Switching run/artifact invalidates pending actions.
  Regeneration affects working reports, not issued originals. Output: exact
  delivered package or clear recoverable refusal; X2/X4.

## P5: Commissioning or maintenance technician

A site-designated technician configures the station for a known program and
needs saved, verifiable context. Engineer presentation is a tool visibility
setting; real policy, file permissions and run ownership still apply.

### P5.1 Commission the intended program and slot

- **Start/trigger:** Run/readiness identifies an unbound/unavailable slot. Work
  only while the bench is idle. Enable engineer presentation if needed.
- **Route:** open Instruments for the exact program/slot → discover VISA/OpenTAP
  or enter a manual address → select resource → Apply → explicitly query identity
  where supported → Save overrides → refresh/reload saved bindings → Go to Run
  for the original program → reevaluate readiness.
- **Acceptance:** same-name programs remain distinguishable by stable ID. Target,
  resource and discovered-vs-verified status are explicit. Apply is unsaved;
  Save survives reload. Source plan/sidecar bytes remain unchanged; only intended
  program/slot override and identity evidence change, even when programs share
  an address. Address presence alone is not physical identity certification.
- **Recovery:** no discovery, invalid address, unsupported/failed IDN, busy bench
  or Save failure preserves useful input and offers a correction/retry. Query
  leases release after failure. Return navigation cannot bypass health gates.
  Output: persisted correct binding and readiness, or an honest block; X1–X4.

### P5.2 Diagnose a blocked or faulty station

- **Start:** stale/failed station health, deployment-owned setting, or a captured
  recoverable crash. Do not claim a binding fix proves calibration.
- **Route:** inspect readiness/health → inspect effective settings and source →
  correct permitted settings or escalate owned values → collect diagnostic or
  support evidence → retry only after the relevant condition is resolved.
- **Acceptance:** read-only values explain ownership; save feedback is accurate
  and updates on the UI thread. Support export remains available when OS-folder
  browsing is disabled. Existing freshness/credential/run-ownership gates remain.
  Output: actionable diagnosis/support handoff, not an unsupported ready claim.

## Fixed scoring rubrics

| Definition metric | What must be specified |
| --- | --- |
| D1 Persona realism | Distinct responsibility, goals and experience |
| D2 Preconditions | Trigger, station state, inputs and policy prerequisites |
| D3 End-to-end path | Decisions, actions and observable terminal output |
| D4 Recovery | Failures, interruption and safe recovery branches |
| D5 Continuity | Identity, scope, evidence, permissions and ownership |
| D6 Inclusive context | Keyboard, themes, viewport/text scale and environment |
| D7 Testability | Falsifiable acceptance, evidence mapping and honest limits |

| App metric, scored for each persona | Evidence assessed |
| --- | --- |
| C1 Completion/correctness | Critical end-to-end outcomes actually complete |
| C2 Discoverability | Entry, context and next action make sense without coaching |
| C3 Efficiency/control | Appropriate effort, deliberate actions and continuity |
| C4 Recovery | Errors preserve useful input and enforce existing guards |
| C5 Integrity | Correct identity, scope, evidence, policy and destination |
| C6 Access/space | Keyboard/focus, enlarged text, compact layout and scroll |
| C7 Feedback/handoff | Readiness, outcome confidence and clear next/resume action |

## Evidence and scope boundaries

Use production-shell simulated-instrument journeys for graphical end-to-end
outcomes and actual keyboard/pointer actions. Use deterministic delayed-broker,
export-filesystem, deployment-policy, failure and saved-configuration tests for
boundary conditions that cannot honestly be inferred from a screenshot. Every
critical acceptance above must be linked in the review evidence ledger to such
checks, or explicitly remain a score-blocking gap. Reuse previous verified tests
when behavior is unchanged, and add tests for newly discovered integrity gaps.

For material UI changes, obtain fresh rendered evidence and three independent UI
votes against the existing seven graphical categories, again requiring every
reviewer/category >90 on the final shared candidate. Keep screenshots/logs local.

Linux native metadata/keyboard checks are separate from headless rendering.
Neither proves Windows UIA, spoken announcements, physical instrument execution,
physical safety, actual PIN-pad hardware or newcomer performance. These require
separate platform/site/user studies and cannot be claimed from this review.
Shift scheduling, MES integration, active-run ownership transfer, new enterprise
RBAC, report batch-release authority and operator-side plan authoring are not
requirements of these defined operator workflows.
