# Architecture options: standards-based mission planning on the HardwareTest appliance

This is an options report, not a committed implementation plan. It asks how the same sealed Linux appliance that already runs locked OpenTAP programs could also **provision** a unit and **plan / verify unmanned movement**, with operator continuity between those programs.

Related: [README.md](../README.md) (layering), [adapting.md](adapting.md) (locked programs and sidecars), [appliance-linux.md](appliance-linux.md) (sealed image), [testing.md](testing.md) (session contracts).

## 1. What we already have

The product is an Avalonia operator shell over a killable OpenTAP worker. Continuity today is already a **kinded program + Core document + pre-run gate** pattern, not a single mega-plan.

| Piece | Role today | Why it matters for this report |
| --- | --- | --- |
| Locked `.TapPlan` + `{planId}.program.json` | Author in TUI/Editor; shell does not edit plans | New work should stay locked-program, not grow an in-app plan editor |
| `programKind`: `dut` \| `stationHealth` | Distinguishes unit programs from bench cal | Natural extension point for `provision` / `missionVerify` |
| `requireStationHealth` + `{DataDirectory}/station-health/{profileId}.json` | Freshness gate before DUT Run | Template for “must be provisioned” / “mission loaded” gates |
| `SuiteRunRecord` | Ordered list of `TestRunRecord` under one suite | Continuity between programs without merging them into one TapPlan |
| `HardwareTest.Core` | Avalonia-free **and** OpenTAP-free: identity, clock, attestation, runs, safety | Shared kernel for every program kind |
| Focused `IOpenTap*` surfaces | Plan / run / station / catalog; Features must not take `IOpenTapSession` | New hosts should add focused surfaces, not fatten the aggregate |
| `OpenTapWorkerClient` | UI façade over a killable worker; Abort does not call `TapThread.Abort`; kill runs `ISafetyController.SafeIdle` | Long-lived C2 / DDS / JAUS loops must not share that worker |
| `IBenchOperationCoordinator` | Fail-closed exclusive lock (`Run`, `ModeSwap`, `IdQuery`) | Provisioning and mission load must join this lock |
| `ISafetyController.SafeIdle` | Hardware interlock seam, Core-owned | Mission abort and worker death already have a common idle path |
| Operator session + DUT serial + optional PIV attestation | One DUT confirm per session; Typst status/certification | Platform identity and signed artifacts can span test / provision / mission |
| Appliance layout | Read-only `app/` + writable `HARDWARETEST_DATA_DIRECTORY` | Mission files, keys, and load records belong under data, not the baked tree |

Hard constraints that any option must keep:

- Core stays Avalonia-free and OpenTAP-free.
- OpenTAP Host/Worker stay Avalonia-free. Do not call `TapThread.Abort` in the UI process.
- Feature ViewModels take focused session surfaces, not the aggregating `IOpenTapSession`.
- Operator flow stays in-panel on `MainWindow`.
- Pause/interaction is per run context, not process-global.
- Safety Stop / worker kill must not wait on NTP.
- NativeAOT is not a product gate for the OpenTAP host (plugins + reflection).

## 2. Three jobs that look similar and are not

Operators will see one appliance and one DUT. Internally these are different control loops.

```
┌─────────────────────────────────────────────────────────────────┐
│  Operator session (DUT / platform identity, idle, credential)   │
└─────────────┬───────────────────────┬───────────────────────────┘
              │                       │
              ▼                       ▼
   Sequential, abortable,        Spatial / temporal,
   pass/fail, instrumented       long-lived, graph-shaped
              │                       │
              ▼                       ▼
   OpenTAP worker                 Mission / C2 host
   (TapPlan execute)              (route, spool, teleop)
              │                       │
              └──────────┬────────────┘
                         ▼
              Core documents + gates + SafeIdle
```

| Job | Shape | Pass/fail? | Fits a `.TapPlan`? | Live C2 loop? |
| --- | --- | --- | --- | --- |
| Hardware / software test | Setup → measure → cleanup | Yes (scalar/passband) | Yes — this is the product | No |
| Provisioning | Ordered loads: image, keys, identity, config | Partial: each step can verdict | **Sequence yes; protocols no** | Sometimes (FDO/SZTP state machines) |
| Provisioning **verification** | Identity, hashes, DevID, self-test | Yes | Yes | No |
| Mission **planning** | Map, route, constraints, contingencies | Review, not a test verdict | Poor — nest-depth-3 groups are not a route graph | Planner UI / services |
| Mission **load + verify** | Push artifact, confirm vehicle accept, checksum | Yes | Yes | Short handshake |
| Unmanned **movement execution** | Spool waypoints, teleop, abort | Operational, not a bench verdict | Only for short, abortable **motion tests** | Yes — JAUS/UCI/4586/MAVLink |

The continuity the user asked for is: **same identity, same safety, same run store, ordered gates** — not “one TapPlan that also draws a map.”

## 3. Recommended option (hybrid)

**Keep OpenTAP as the sequential executor. Keep Core as the continuity kernel. Keep mission planning as a separate host that exchanges standard artifacts. Bind them with kinded programs and freshness gates, the same way station health already works.**

Do **not** encode a NATO route in `.TapPlan` XML. Do **not** stand up a second appliance just to provision. Do **not** run a STANAG 4586 / JAUS / OMS C2 loop inside the OpenTAP worker.

### 3.1 Target layers

```
Avalonia Features (Home / Run / Results / Settings
                   + optional Plan / Provision pages)
        │  focused surfaces only
        ▼
┌───────────────────┐   ┌────────────────────┐   ┌─────────────────┐
│ IOpenTap*         │   │ IProvisionSession  │   │ IMissionSession │
│ (existing worker) │   │ (often a façade    │   │ (separate       │
│                   │   │  over OpenTAP +    │   │  killable       │
│                   │   │  protocol adapters)│   │  process)       │
└─────────┬─────────┘   └─────────┬──────────┘   └────────┬────────┘
          │                       │                       │
          └───────────────┬───────┴───────────┬───────────┘
                          ▼                   ▼
                 HardwareTest.Core      Standard artifacts
                 identity, clock,       (CRD / UCI / JAUS /
                 gates, runs, suites,   MAVLink / FDO voucher)
                 attestation, SafeIdle, IBenchOperationCoordinator
```

`IProvisionSession` can start as a **kinded OpenTAP program** (`programKind: provision`) plus Core `ProvisionRecord`. Promote it to its own surface only if FDO/SZTP/ARINC loaders need a long-lived protocol daemon that must outlive `TestPlan.Execute`.

`IMissionSession` should **not** be an `IOpenTap*` alias. Planning, map, and vehicle C2 are a different process isolation class (same reason the UI process does not own `TapThread`).

### 3.2 Continuity contract (extend station health, do not invent a new pattern)

Station health already proves the model:

1. A program of kind `stationHealth` runs and publishes scalars.
2. Core writes `{DataDirectory}/station-health/{profileId}.json`.
3. A DUT sidecar opts in with `requireStationHealth` + `warn`|`block` + max age.
4. Run evaluates the gate with `IClock` before execute.

Apply the same three-part contract to provision and mission:

| Kind | Producer program | Core document | Consumer gate on later programs |
| --- | --- | --- | --- |
| `stationHealth` (exists) | Bench cal TapPlan | `StationHealthRecord` | `requireStationHealth` |
| `provision` | Flash / enroll / config TapPlan (and/or FDO adapter) | `ProvisionRecord` | `requireProvisioned` |
| `dut` (exists) | Unit test TapPlan | `TestRunRecord` | (history / compare; does not gate) |
| `missionVerify` | Load + accept + checksum TapPlan | `MissionLoadRecord` | `requireMissionLoaded` |

Suggested sidecar fields (names only; not implemented here):

```json
{
  "programKind": "dut",
  "requireStationHealth": true,
  "stationHealthGate": "block",
  "requireProvisioned": true,
  "provisionGate": "block",
  "provisionMaxAgeHours": 24,
  "requireMissionLoaded": false
}
```

A `provision` program must not require itself; a `missionVerify` program must not require a loaded mission. That is the same rule as “do not put `requireStationHealth` on a `stationHealth` program.”

**Suite continuity:** `SuiteRunRecord.PlanRuns` already chains programs. A commissioning suite is an ordered catalog, not a nested TapPlan:

```
stationHealth → provision → dut (ATE) → missionVerify → (optional motion rehearsal)
```

Each child still has its own `run.json`, Typst artifacts, and optional PIV attestation. The suite PDF is the operator-facing continuity report.

**Artifact continuity:** store content-addressed files under `{DataDirectory}/missions/{platformId}/` and `{DataDirectory}/provision/{platformId}/`. Stamp `sha256`, standard id, and schema version onto `MissionLoadRecord` / `ProvisionRecord` and copy those hashes into `TestRunRecord.Variables` (or a typed list when the run schema next bumps). Results then answer “which mission flew on which serial after which provision.”

**Resource continuity:** extend `BenchOperation` so a mission load or FDO handshake cannot overlap a DUT run or VISA mode swap. Safety Stop remains global: `IOpenTapRunSession.Abort(safetyStop: true)` and `IMissionSession.Abort` both call `SafeIdle`.

**Identity continuity:** today’s DUT serial is the operator session. For unmanned platforms, treat serial as the **platform id** and attach standard identities beside it (IEEE 802.1AR DevID, STANAG 4586 vehicle id, JAUS subsystem id) without replacing the confirm strip.

### 3.3 What stays in the test program vs what needs a separate program

| Work | Stay in OpenTAP test program? | Separate program / host? | Why |
| --- | --- | --- | --- |
| Electrical / functional ATE | Yes (`dut`) | No | Existing product |
| Station cal / health | Yes (`stationHealth`) | No | Existing gate |
| Firmware / FPGA / parameter load (UDS, ARINC 615A, JTAG) | Sequence in `provision` TapPlan | Loader plugin, not a new UI product | Sequential, abortable, needs instruments |
| Identity enroll (IDevID → LDevID, PIV attest of the load) | Verify in OpenTAP; enroll may be a step | FDO/SZTP daemon if the protocol cannot finish inside `Execute` | State machine vs test step |
| Post-provision self-test | Prefer a `dut` program gated on `requireProvisioned` | Optional `provision` cleanup measure | Keeps verdicts in the test language we already ship |
| Route / task planning | No | `IMissionSession` + standard file | Map/graph; TUI nest-depth-3 is the wrong editor |
| Upload mission to vehicle and confirm | Yes (`missionVerify`) | Vehicle adapter plugin | This **is** a test: hash, accept, NAK, timeout |
| Teleop / in-mission C2 | No | Mission host / GCS | Live loop; worker kill semantics differ |
| Short motion proof (move 1 m, return, estop) | Yes, as a `dut` or `missionVerify` child | JAUS/MAVLink **step**, not a planner | Abortable, pass/fail, SafeIdle on fail |

## 4. Other options (rejected as defaults, useful as variants)

### Option A — Everything is a TapPlan

Add OpenTAP steps for waypoints, geofences, JAUS Mission Spooler, FDO TO2, and draw none of that in a new page.

- **Fits the repo** in the narrow sense: one catalog, one worker, one Run board.
- **Breaks the product rules in spirit:** nest depth > 3 is already a contract warning; a route is a graph; TUI is the authoring tool and is a bad map. TapThread / PluginManager remain process-global, so a blocked C2 receive path contaminates ATE.
- **Use only for:** throwaway demos of “send three MAVLink MISSION_ITEM_INT then measure current.”

### Option B — Second product / second appliance

A GCS (QGroundControl, vendor UCS, OpenUxAS) plus this bench, joined only by USB sticks or MES.

- **Standards-native** immediately (the GCS already speaks 4586/MAVLink).
- **Loses the continuity the user asked for:** two operator sessions, two clocks, two attestation stories, no `requireProvisioned` before taxi.
- **Use only if:** airworthiness or classification forces the planner off the ATE image. Even then, keep `MissionLoadRecord` import so the bench can still gate and certify.

### Option C — Hybrid (recommended, §3)

Standard artifacts + OpenTAP sequences + Core gates + optional mission worker.

### Option D — MOSA “units of replaceability” from day one

Treat OpenTAP, the provision adapters, and the mission host as OMS-style UoRs talking UCI (or JAUS, or UCS) on a bus, with Core as the isolator.

- **Right long-term alignment** if the DUT **is** a MOSA vehicle (OMS/UCI, FACE, SOSA, UMAA).
- **Too much bus for this template** until a vehicle program demands it. Core JSON documents + hashes are a sufficient isolator for a bench. Revisit D when a real UCI/JAUS stack must run on the same box as ATE without going through files.

**Default: C. Borrow D’s vocabulary (UoR, standard message, isolator) without standing up DDS on the operator UI.**

## 5. Standards that actually help

Pick **one artifact family per domain**, plus a test-result interchange. Do not implement every row.

### 5.1 Test and verification (stay with OpenTAP; interchange out)

| Standard | What it gives | Fit |
| --- | --- | --- |
| OpenTAP locked plan + our sidecar | Authoritative executable sequence | Keep as runtime |
| IEEE 1671 ATML family (esp. 1671.1 Test Description, 1671.3 UUT Description, 1671.6 Test Station) | Portable description of *what* is tested, not the Keysight sequencer | Optional **export**; do not replace `.TapPlan` with OSA-RTS/TestStand |
| IEEE 1636 / 1636.1 SIMICA Test Results | Maintenance/ATS result interchange | Optional ResultListener beside `run.json` / CSV |
| IEEE 1641 Signal & Test Definition | Signal-oriented TPS (OSA-RTS world) | Only if a customer TPS arrives as 1641; wrap, do not rebase the host |
| IVI / VISA / SCPI | Bench I/O | Already owned by Core `IVisaBroker` |
| ISO/IEC 17025, cal certificates | Lab quality, not a file format | Station health metrics already carry offset/age |

OpenTAP should remain the executor. ATML/SIMICA are **egress** for MES/QA, similar in spirit to `ExportOpenTapResults` CSV.

### 5.2 Provisioning and identity

| Standard | Domain | OpenTAP vs separate |
| --- | --- | --- |
| IEEE 802.1AR Secure Device Identity (IDevID / LDevID) | Device identity for later C2 and attestation | Verify in OpenTAP; issue LDevID via a provision step/HSM |
| IETF RFC 8572 SZTP (+ RFC 8366 vouchers, BRSKI RFC 8995) | Network gear zero-touch | Separate bootstrap server; OpenTAP can **assert** image/config committed |
| FIDO Device Onboard (FDO) | IoT late-binding ownership voucher | Protocol daemon; OpenTAP records voucher hash + TO2 result |
| IETF RATS (RFC 9334), TCG DICE / TPM | Remote attestation of firmware | Map to existing PIV/`RequireAttestationBeforeExport` story; do not persist keys |
| IETF SUIT (RFC 9019 / 9124) | Signed IoT firmware manifests | Artifact format under `provision/`; OpenTAP verifies signature then calls the loader |
| DMTF SPDM / PLDM / Redfish | Server/BMC class | Only if the DUT is that class |
| ISO 14229 UDS (+ ISO 15765 / 13400) | Automotive ECU flash/diag | OpenTAP steps over the existing instrument/adapter pattern |
| ARINC 615A / 665 | Avionics data loader / loadable software | OpenTAP sequence around a 615A loader plugin |
| IEC 62443 (esp. 4-1/4-2) | Industrial component security process | Policy for how we provision; not a runtime |
| SPDX / CycloneDX SBOM | What was loaded | Attach to `ProvisionRecord` |
| IEEE 1149.1 / 1687 JTAG | Board programming | Instrument plugin, `provision` plan |

**Rule:** if it is “send this signed blob, wait, read back identity,” it is an OpenTAP program of kind `provision`. If it is “own the device through a multi-message ownership transfer while the operator walks away,” it is a daemon invoked *by* that program.

### 5.3 Unmanned / robotic mission planning and movement

Air, ground, and maritime do not share one file format. Choose by vehicle program.

**Air / UCS**

| Standard | Role |
| --- | --- |
| NATO STANAG 4586 (AEP-84) UCS | **Primary NATO C2 interoperability.** CUCS ↔ VSM ↔ DLI; mission planning is a CUCS function. Common Route Definition (CRD) XML + Custom UAV Tags is the practical mission-file handshake between a planner and a GCS. LOI I–V (payload data → launch/recovery). |
| STANAG 7085, 7023, 4545, 4607, 4609 | Data link and ISR payloads — not planning, but often required beside 4586 |
| USAF OMS + UCI | MOSA mission bus and mission-level C2 messages (tasking, routes, status). UCI is schema-defined; encodings are not XML-only. Prefer when the air vehicle is OMS-compliant (CCA/NGAD-class). |
| The Open Group FACE | Portable avionics software environment; companion to OMS, not a route file |
| ISO 21384 (UAS) | Civil operational procedures / C2 safety — policy, not a planner API |
| ASTM F38 (e.g. F3269, F3411 Remote ID, F3442 DAA) | Civil UAS safety/identity |
| MAVLink mission protocol (`MISSION_ITEM_INT`, geofence, rally) | De facto small-UAS file/stream. Useful on the bench; **not** a NATO interchange. |
| AFRL OpenUxAS / LMCP | Research/autonomy mission management; optional adapter |

**Ground robots**

| Standard | Role |
| --- | --- |
| SAE AS-4 JAUS | Cross-domain unmanned SOA. **AS6009** Mobility, **AS5710** Core, **AS6062** Mission Spooler (store/manage/execute task lists), **AS6057** Manipulator, **AS6111** maritime extras. |
| US Army RS JPO IOP (JAUS profile) | What DoD UGVs actually certify against — prefer IOP subsets over “all of JAUS” |
| ROS 2 + OMG DDS | Common research/industry runtime; not a defense interchange. Fine *behind* a JAUS/UCI adapter. |
| ISO 22166-201 | Service-robot modularity — weak for tactical UGV C2 |
| IEEE 1872 / 1873 | Robot ontology / map data — research interchange, not a spooler |

**Maritime**

| Standard | Role |
| --- | --- |
| Navy UMAA (PMS 406) | **On-board** autonomy services (DDS), not the shore C2 element. Use when the DUT is a USV/UUV autonomy stack. |
| JAUS AS6111 + AS6009 | Cross-domain mobility plus maritime platform services |
| UCS architecture (UxS Control Segment, MDE) | Framework UMAA already cites |

**Movement algorithms** (RRT, A*, behavior trees, PDDL) are not interchange standards. Keep them inside the mission host. Persist **results** as 4586 CRD, UCI mission, JAUS spooler tasks, or MAVLink mission items.

### 5.4 Cross-cutting MOSA (vocabulary, not a rewrite)

Title 10 MOSA and the 2024 tri-service memo name OMS/UCI, SOSA, FACE, VICTORY, WOSA, AMS GRA. For this appliance they mean:

- **Stable interfaces** we already practice (focused session surfaces, Core isolator, plugin packs).
- **Government-purpose artifacts** (CRD, UCI, JAUS JSIDL, TapPackage, SBOMs).
- **Do not** put a VPX/SOSA payload computer requirement on the operator UI image.

## 6. Concrete mapping onto this repo

### 6.1 Catalog and contract

Today `ProgramKinds` is `dut` | `stationHealth` (`src/HardwareTest.Core/StationHealth/StationHealthRecord.cs`). `plans/opentap/program.schema.json` and `PlanContractSidecar` reject unknown kinds.

A later implementation would:

- Extend `ProgramKinds` and the JSON schema enum.
- Add gate fields next to the station-health keys (unknown keys are already a sidecar warning).
- Teach `PlanContractValidator` the same “producer must not require itself” rule.
- Keep `HardwareTest.PlanValidate --strict` as the bake gate.

Mission **plans** are not TapPlans. They are extra files referenced by the `missionVerify` sidecar (path or hash), analogous to Typst templates under `{DataDirectory}/reports/`.

### 6.2 New Core documents (sketch)

Follow `StationHealthRecord` / `SchemaVersions` / goldens under `tests/fixtures/schema/`.

```text
ProvisionRecord   schemaVersion, platformId, measuredAt, verdict,
                  programId, runId, artifactSha256, standard (sztp|fdo|uds|arinc615a|suit),
                  identity (serial, idevid, ldevid), sbomPath

MissionLoadRecord schemaVersion, platformId, loadedAt, verdict,
                  programId, runId, artifactSha256,
                  standard (stanag4586-crd|uci|jaus-spooler|mavlink|umaa),
                  loi or service set, vehicleAccept, rehearsal (none|sim|live)
```

Bump `TestRunRecord` only when hashes deserve first-class fields; until then `Variables` is enough for a prototype.

### 6.3 Session surfaces

Keep Features off `IOpenTapSession`. If a Plan page appears:

- `IMissionSession`: load artifact, validate schema, show route summary, export, abort.
- Reuse `IOpenTapRunSession` for `missionVerify` execute (it is a TapPlan).
- Do not add `LoadMissionDemoProgramAsync` onto `IOpenTapPlanSession` unless it is literally a TapPlan factory (the station-health demo is the precedent).

Architecture tests should gain one rule: Core still has zero OpenTAP and zero JAUS/UCI package references; adapters live in plugin projects.

### 6.4 Workers and safety

| Process | Owns | Kill behavior |
| --- | --- | --- |
| Avalonia UI | Features, prompts, plots | Never `TapThread.Abort` |
| `HardwareTest.OpenTap.Worker` | TapPlan execute, VISA via broker | Cooperative abort, then kill-timeout → `SafeIdle` |
| Future mission worker | DDS/JAUS/4586/MAVLink, map services | Own watchdog; on death → `SafeIdle`; must not share PluginManager |

Short motion tests that are OpenTAP steps still run in the OpenTAP worker and must call vehicle **hold/estop** in `Safe Shutdown` (the existing cleanup contract with `selectionIncludesCleanup`).

### 6.5 UI

Default left nav stays Home / Run / Results / Settings. Engineer/debug already unlocks Inspect and Instruments.

- **Provisioning:** a kinded program on the Run board is enough. Extra chrome only if FDO/SZTP needs a progress surface that is not a step tree.
- **Planning:** a Plan page is Engineer/debug (or a second locked kiosk mode), not operator Run. Operators execute `missionVerify` like any other program.
- Do not put a map on the compact 900×600 Run board. The operator chrome contracts (touch floor, in-panel prompts, no second window) still apply if teleop confirmations are required.

### 6.6 Appliance

Bake adapters as TapPackages or plugin dirs (existing offline install). Mission artifacts and keys live under the writable data volume. Time sync remains a systemd/chrony concern; mission timestamps must use `IClock` like idle/retention.

## 7. Example flows

### Commissioning a UGV (JAUS IOP)

1. Confirm platform serial (existing session).
2. Run `stationHealth` if the sidecar requires it.
3. Run `provision`: UDS or vendor flash → write LDevID → publish identity scalars → Core `ProvisionRecord`.
4. Run `dut` ATE (power, radios, estop loop). Gate: provision fresh + health fresh.
5. Engineer Plan page authors or imports an IOP mission (JAUS Mission Spooler task list). File lands under `data/missions/{serial}/`.
6. Run `missionVerify`: push spooler list, read back, hash match, optional 1 m mobility step (`AS6009`), Safe Shutdown = hold.
7. Suite PDF + optional IEEE 1636.1 export for the logbook.

### NATO air vehicle (STANAG 4586)

Same chain, but the artifact is CRD XML (core route + Custom UAV Tags). `missionVerify` talks to the VSM/CUCS adapter, not to JAUS. LOI on the bench is typically III/IV (payload + AV control without launch) unless the site is a range.

### Small UAS without 4586

Same chain with a MAVLink `.waypoints` / mission protocol dump. Treat MAVLink as an adapter behind `MissionLoadRecord.standard = mavlink`, so a later 4586 vehicle does not fork Core.

## 8. Risks

- **C2 in the OpenTAP worker** — hardest failure mode. A blocked receive thread plus process-global PluginManager takes down ATE. Isolation is the point of Option C.
- **Fake interoperability** — wrapping a proprietary GCS log as “STANAG 4586” without CRD/LOI testing. Record the standard id and schema version; validate with `PlanValidate`-style checks on artifacts.
- **Operator vs engineer** — planning UX will violate Run-board chrome if forced into the same page.
- **Classification / airworthiness** — Option B (planner off-box) may be mandatory; keep the gate records anyway.
- **ARM / NativeAOT** — still out of scope; mission stacks (DDS, map) are heavier than TypstInterop and will fight AOT the same way plugins do.
- **Safety Stop vs vehicle estop** — `SafeIdle` is a seam. A no-op adapter that only stops VISA outputs is not a robot interlock. Product benches must register a real adapter before live motion programs are catalogued.

## 9. If we implemented later (technical slices, not a schedule)

These are dependency-ordered slices that match how this repo already ships work. They are not an execution commitment.

1. **Kinds + documents + gates** in Core and the sidecar schema (`provision`, `missionVerify`, records, `require*` fields). No UI. Architecture + host contract tests.
2. **`provision` TapPlan pack** (identity write/read, hash, Safe Shutdown) using existing Basic/library steps where possible.
3. **Artifact store + `missionVerify` TapPlan** that consumes a file and publishes accept/hash scalars.
4. **One vehicle adapter** (pick the real program: JAUS IOP, 4586 CRD, or MAVLink) in a plugin that does not reference Avalonia or `Ivi.Visa`.
5. **Optional Engineer Plan page** + mission worker only after an adapter exists; Features inject `IMissionSession`.
6. **Optional ATML/1636.1 ResultListener** for MES, independent of planning.

Until a vehicle standard is chosen, slices 1–3 already give “the embedded system runs standardized tests and provisioning/verification with continuity.” Slice 4 is where unmanned movement standards enter without rewriting the shell.
