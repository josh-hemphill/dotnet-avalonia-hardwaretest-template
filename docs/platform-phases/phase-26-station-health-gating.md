# Phase 26 — Station health & optional Run gating

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 17](phase-17-shell-notification-strip.md) (strip), [Phase 25](phase-25-clock-discipline.md) (`IClock`), [Phase 10](phase-10-export-storage-chrome.md) (existing Run gates)
**Unblocks:** daily self-cal / instrument health as a first-class station record; DUT programs that can warn or block when that record is stale
**Status:** Planned

## Goal

Support **station health** programs that report (not re-run) daily self-calibration and similar bench checks, and let a DUT program **optionally gate Run** on that record’s freshness and verdict.

This is **not** a host skip engine. OpenTAP still executes the health step. The step queries the instrument or a local store, publishes band-first Scalars (value + age), and writes a station-scoped record the DUT gate can read.

Today the closest pieces are the wrong tool:

- [DUT history](../../src/HardwareTest.Core/Runs/DutHistoryService.cs) and Compare with previous run after a DUT execute; they never skip and they key off DUT serial + plan.
- Run Selected / `Enabled` can omit steps — that is not a freshness policy.
- Storage critical and DUT session already gate [`RunExecutionViewModel`](../../src/HardwareTest/Features/RunTest/RunExecutionViewModel.cs); there is no health input.
- `{planId}.program.json` has no health fields ([`program.schema.json`](../../plans/opentap/program.schema.json)).

## Locked decisions

- **Execute, then report.** A health step always runs. It may be cheap (query `CAL:STAT?` / read a file). The shell must not inject `StoredSample`s and mark the OpenTAP step NotExecuted.
- **Station-scoped store, not run history.** Persist `{DataDirectory}/station-health/{profileId}.json` via `IStationHealthStore`. Retention may prune `runs/`; health must survive that. Default `profileId` = station id or `"default"`.
- **Age uses `IClock`.** Do not call `DateTimeOffset.UtcNow` on the freshness path (same architecture rule as idle/retention).
- **Cache is provenance, not a DisplayRole.** Keep `scalar` / `passband`. Optional `StoredSample.ResultSource` = `measured` | `cached` on schema 3 (shared bump with [Phase M](../opentap-phases/phase-m-series-envelope-timing.md) if they land together; otherwise a later additive bump). Gauges may show “as of HH:MM” when `MeasuredAt` is set. Do not add a `cached` role.
- **Gate is sidecar-opt-in.** Default off. Missing sidecar keys = no health gate (today’s behavior).
- **Two enforcement levels:** `warn` (shell strip, Run allowed) and `block` (same pattern as storage critical: `CanStartRun` false + inline tip). Clock skew stays warn-only and is not reused as a health signal.
- **Missing record = stale** when the gate is on. Do not treat “never calibrated” as Pass.
- **No Cal page / no second Window.** Health is a catalog program + strip + Settings one-liner. Operators run it from Run like any other program.
- **Health programs are not DUT programs.** Sidecar `programKind`: `dut` (default) | `stationHealth`. `stationHealth` defaults `requireSerial` to false (bench, not unit). DUT confirm stays available if a site sets `requireSerial: true`.
- **Do not use DUT history as the skip/cache.** Drift ≠ freshness.

## Sidecar + settings

Extend `{planId}.program.json` ([`ProgramSidecar`](../../src/HardwareTest.OpenTap.Host/ProgramCatalog.cs), [`program.schema.json`](../../plans/opentap/program.schema.json)):

| Field | Where | Meaning |
| --- | --- | --- |
| `programKind` | health or DUT sidecar | `dut` (default) \| `stationHealth` |
| `requireStationHealth` | **DUT** sidecar | When true, evaluate the gate before Run |
| `stationHealthMaxAgeHours` | DUT sidecar | Freshness window (default **24** when require is true) |
| `stationHealthGate` | DUT sidecar | `warn` \| `block` (default `warn`) |
| `stationHealthProfileId` | either | Store key; default `default` |

Station overrides (Engineer/Debug, `settings.json`, env/CLI — Phase 3 binder):

| Setting | Env | Purpose |
| --- | --- | --- |
| `StationHealthProfileId` | `HARDWARETEST_STATION_HEALTH_PROFILE_ID` | Default profile when sidecar omits it |
| `StationHealthGateOverride` | `HARDWARETEST_STATION_HEALTH_GATE` | Optional station pin: `off` \| `warn` \| `block` (empty = follow sidecar) |

Settings UI: last health verdict, measured-at, age, **Run station health** (loads the `stationHealth` catalog entry if present). No new top-level nav item (operator nav stays Home / Run / Results / Settings).

`PlanContractValidator`: unknown `programKind` / `stationHealthGate` is an error; `requireStationHealth` on a `stationHealth` program warns (nonsensical). `--strict` still fails a missing sidecar.

## Health record

```text
StationHealthRecord
  schemaVersion: 1
  profileId
  measuredAt          // IClock
  source              // queried | recalled
  verdict             // Pass | Fail | Error
  programId, runId    // optional trace
  metrics[]           // name, value, unit, limitLow, limitHigh
  maxAgeHours         // advertised by the health program (informational)
```

`IStationHealthStore.TryRead(profileId)` / `WriteAsync(record)`. Writer is the Host at the end of a terminal **stationHealth** run (Pass or Fail). DUT runs do not overwrite the store.

Freshness: `age = clock.UtcNow - measuredAt`; stale when `verdict != Pass` **or** `age > maxAgeHours` from the **DUT** sidecar (DUT policy wins over the record’s advertised max age).

## Workstreams

### A — Store + model

1. `StationHealthRecord` + `SchemaVersions.StationHealthRecord = 1` + golden fixture under `tests/fixtures/schema/`.
2. `IStationHealthStore` / `FileStationHealthStore` in Core (Avalonia-free, OpenTAP-free).
3. Architecture: freshness and persist paths use `IClock`; no wall-clock `UtcNow`.

### B — Health program + step

1. Basic demo: `ReportStationHealthStep` — reads mock cal (or `{DataDirectory}/station-health/mock.json` in tests), publishes:
   - `cal.dc.offset` (passband + spec)
   - `cal.age.hours` (`scalar`, `LimitHigh` = max age)
   Fail when out of band. `FailWhenOutOfBand` default true.
2. Factory + sidecar `station-health.program.json`: `programKind: stationHealth`, `requireSerial: false`, `reportKinds: ["status"]`.
3. Host: on terminal stationHealth run, write `IStationHealthStore`. Presentation mixin on the leaves (unique `ChannelKey`s).
4. Product path (docs only): replace the mock query with InstrumentComponents / custom GPIB self-cal query inside a product step. Same publish + store write.

### C — Optional DUT gate

1. Catalog: parse new sidecar fields onto `ProgramCatalogEntry` / `ProgramRequirements` (or a sibling `ProgramHealthGate` so session confirm stays separate).
2. `IStationHealthGate.Evaluate(program, clock)` → `{ Level: Off|Ok|Warn|Block, Message, Age, Verdict }`.
3. [`RunExecutionViewModel`](../../src/HardwareTest/Features/RunTest/RunExecutionViewModel.cs): after storage + session checks, if Block → `BlockStart` and return; if Warn → publish strip Warning (dismissible) and continue.
4. `CanStartRunTip` names the reason (“Station health stale (29 h). Run Station health or wait for a passing cal.”).
5. Shell strip source precedence unchanged (Critical > Error > Warning > Info). Block is Error-level and non-startable; it is not a competing Home card.
6. Run Selected on a DUT program uses the same gate (health is station-wide). Running the health program itself never evaluates the DUT gate.

### D — Chrome + reports

1. Settings: last record one-liner + age (Engineer can copy path). Hidden when no record and no health program in the catalog.
2. Results for a health run: normal Band gauges (age + cal metrics). Optional “as of” when `ResultSource=cached`.
3. Status PDF: include health metrics like any other Scalar. Certification on a **DUT** run may footnote “station health: Pass, 4 h old” when a record exists — do not fail Typst when the store is empty and the gate is off.
4. No new `reportKinds` value.

### E — Tests + docs

- Store round-trip; FakeClock ages a record past the window → Warn/Block.
- Missing record + `requireStationHealth` → stale.
- `stationHealthGateOverride=off` disables a sidecar block (station pin).
- Health program Run does not require DUT serial by default.
- DUT Run is not blocked when sidecar omits the keys.
- Catalog self-check includes sidecar field validation.
- Update [adapting.md](../adapting.md) (sidecar table + “do not skip via Enabled”), [testing.md](../testing.md), [opentap-platform.md](../opentap-platform.md) parameter/session notes.

## Exit criteria

- [ ] A `stationHealth` program can run without DUT serial, publish age + cal Scalars, and write `IStationHealthStore`
- [ ] DUT sidecar `requireStationHealth` + `warn` surfaces a strip warning and still allows Run
- [ ] `block` prevents Run with an inline tip; override `off` restores Run
- [ ] Missing or failed health is stale when the gate is on
- [ ] Age uses `IClock`; architecture forbids wall-clock on the freshness path
- [ ] No new Window, nav item, or `cached` DisplayRole
- [ ] Schema fixture + host / ViewModel tests green

## Out of scope

- Host-side “reuse last Pass and skip the step”
- Using DUT history or Compare with previous as the gate
- In-app SCPI map / cal procedure editor
- Blocking Run on clock skew (Phase 25 stays warn-only)
- Multi-station sync / MES push of health records
- Automatic background cal at idle (unsafe without a dedicated safety review)

## Related

- Prior UX note: cached self-cal should be a cheap **executed** step + store + optional gate, not a skip
- [Phase 10](phase-10-export-storage-chrome.md) — storage BlockStart pattern to copy
- [Phase 17](phase-17-shell-notification-strip.md) — strip, not a Run hero row
- [Phase 25](phase-25-clock-discipline.md) — `IClock` / FakeClock
- [Phase M](../opentap-phases/phase-m-series-envelope-timing.md) — series envelope (independent; may share TestRunRecord schema 3 if scheduled together)
