# Stack: Phase M + Phase 26

**Parent plans:** [phase-m-series-envelope-timing.md](phase-m-series-envelope-timing.md) · [phase-26-station-health-gating.md](../platform-phases/phase-26-station-health-gating.md)
**Workflow:** [.cursor/skills/stacked-pr-workflow/SKILL.md](../../.cursor/skills/stacked-pr-workflow/SKILL.md)
**Base:** `latest`

## Goal

When this stack is merged, a plan can publish a limited `Sample` series on a plan-owned `ElapsedMs` clock with `Event` marks, fail when any sample leaves the band, and show those marks on a timing strip. A separate `stationHealth` program can write a station-scoped cal record; a DUT sidecar can warn or block Run when that record is stale. OpenTAP still executes every step.

## Stack graph

```
latest
  ← Area 0  cursor/timing-health-plans-0331     (docs + this stack + skill)
  ← Area 1  cursor/series-contract-0331         (schema 3 + host Event/ElapsedMs/Sample limits)
  ← Area 2  cursor/series-steps-0331            (compliance steps + envelope-sweep demo)
  ← Area 3  cursor/series-timing-ui-0331        (timing strip + chart markers)
  ← Area 4  cursor/station-health-store-0331    (store + health step + sidecar kind)
  ← Area 5  cursor/station-health-gate-0331     (warn/block Run + Settings one-liner)
```

Area 3 and Area 4 may overlap after Area 2 is pushed (little shared files). Area 5 waits for Area 4.

## Conflict map

| Surface | Areas | Rule |
| --- | --- | --- |
| `TestRunModels` / `SchemaVersions` / `AppJsonContext` | 1 | Stabilize in 1; later areas only consume |
| `ProgressResultListener` / `OpenTapPresentation` | 1, 2 | 2 only adds validator + uses publish columns |
| `Steps.cs` | 2 | Health step is a **new file** in Area 4 |
| `ProgramCatalog` / sidecar / `program.schema.json` | 2, 4 | 2 adds envelope-sweep factory only; 4 adds sidecar fields + health factory — rebase 4 onto 2 |
| `LivePresentation*` / plot / RoleMap | 3 | 1 must not add a dangling `timing` role |
| `RunExecutionViewModel` / Settings | 5 | Copy `Events` onto the run record in Area 1 (persist contract) |
| `PresentationDisplayRoles.Timing` | 3 only | Phase L: role + widget same change |

---

### Area 0: Plans + stacked-PR skill

- Goal: Phase M/26 docs, this stack, and the stacked-PR skill are in-repo.
- Depends on: `latest`
- Out of scope: runtime code
- Likely files: `docs/**`, `.cursor/skills/stacked-pr-workflow/**`
- Tests: none
- Conflicts with: nothing

---

### Area 1: Publish contract + schema 3

- Goal: Sample rows can carry limits + `ElapsedMs`; Event rows persist; `ResultSource` exists; v2 runs identity-upgrade to 3.
- Depends on: Area 0
- Out of scope: new DisplayRole, timing widget, compliance Fail, health store, GPIB steps
- Likely files:
  - `src/HardwareTest.Core/Runs/TestRunModels.cs`
  - `src/HardwareTest.Core/Serialization/SchemaVersions.cs`
  - `src/HardwareTest.Core/Serialization/SchemaUpgradeRegistry.cs`
  - `src/HardwareTest.Core/Serialization/AppJsonContext.cs`
  - `src/HardwareTest.OpenTap.Host/OpenTapPresentation.cs`
  - `src/HardwareTest.OpenTap.Host/OpenTapModels.cs`
  - `src/HardwareTest.OpenTap.Host/OpenTapProgressResultListener.cs`
  - `src/HardwareTest.OpenTap.Host/OpenTapRunContext.cs`
  - `src/HardwareTest.OpenTap.Host/OpenTapRunRecording.cs`
  - `src/HardwareTest/Features/RunTest/RunExecutionViewModel.cs` (copy `Events`)
  - `tests/fixtures/schema/run-v2.json` (optional), schema tests
  - `tests/HardwareTest.Tests/OpenTap/OpenTapHostTests.cs`
  - `docs/adapting.md` schema table
- Public surface:

```csharp
public static class SampleResultSources
{
    public const string Measured = "measured";
    public const string Cached = "cached";
}

public sealed class StoredEvent
{
    public string Name { get; set; } = "";
    public double ElapsedMs { get; set; }
    public string? Label { get; set; }
    public double? Value { get; set; }
    public string StepPath { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
}

// StoredSample += ElapsedMs?, ResultSource?
// TestRunRecord += List<StoredEvent> Events
// SchemaVersions.TestRunRecord = 3
// identity upgrade 2 → 3

public sealed record MeasurementEventMark(
    string Name, double ElapsedMs, string? Label, double? Value, string? StepPath);

// MeasurementSampleEvent += ElapsedMs?
// OpenTapProgress += MeasurementEventMark? Event
// OpenTapRunSummary += List<StoredEvent> Events

static void OpenTapPresentation.ApplySample(..., double? limitLow, double? limitHigh, double? elapsedMs)
static StoredEvent? OpenTapPresentation.TryReadEvent(...)
```

- Pseudo-code:

```
PublishSamples:
  read Channel, Index, Value, optional LimitLow/High, ElapsedMs (NaN → null)
  ApplySample(..., limitLow, limitHigh, elapsedMs)
  FromStored includes ElapsedMs

PublishEvents (table "Event"):
  read Name, ElapsedMs, Label, optional Value
  append StoredEvent; report OpenTapProgress.Event (not coalesced)

SchemaUpgradeRegistry: identity 2→3
FileRunStore load v1/v2: Events empty, ElapsedMs null — still current after upgrade
```

- Tests:
  - Upgrade registry 2→3; v1 fixture still loads; new save stamps 3
  - Host: three-column Sample still works
  - Host: Sample with limits + ElapsedMs round-trip on `StoredSample`
  - Host: Event table stored and on summary
- Risks: worker `OpenTapRunSummary` JSON must include `events`; recording DTO must copy `ElapsedMs` / Event
- Conflicts with: Area 2+ consume only

---

### Area 2: Series compliance steps + demo

- Goal: A demo step can walk mock bits, publish Sample+Event on one clock, and fail `allSamples` when a point is out of band.
- Depends on: Area 1
- Out of scope: timing strip, `timing` role, health, real GPIB UI
- Likely files: `Plugins.Basic` (acquire + new steps), `BitSweepAcquireStep` / `PublishSeriesComplianceStep` (new files preferred), `EnvelopeSweepDemoProgramFactory`, `ProgramCatalog`, `PlanContractValidator`, host tests
- Public surface:

```csharp
public static class SeriesComplianceModes
{
    public const string None = "none";
    public const string AllSamples = "allSamples";
    public const string Dwell = "dwell";
}

// AcquireVoltageStep += LimitLow?, LimitHigh?, SeriesCompliance, DwellLimitMs, FailWhenOutOfBand
// BitSweepAcquireStep: BitCount, IntervalMs, Channel, limits, compliance, publishes Event per bit
// PublishSeriesComplianceStep: publishes series.inband.pct / excursion.max / outband.ms
```

- Pseudo-code:

```
acquire loop i:
  v = ReadVoltage()
  elapsed = i * IntervalMs
  Publish Sample(..., LimitLow, LimitHigh, elapsed)
  if compliance==allSamples && out of [lo,hi] && FailWhenOutOfBand: Fail (keep publishing)
  if compliance==dwell: track contiguous outband ms; Fail if > DwellLimitMs

BitSweep i:
  Publish Event(Name=cfg, ElapsedMs, Label=bit{i}, Value=1<<i)
  then same acquire-one
```

- Tests: allSamples Fail; dwell Fail; FailWhenOutOfBand=false still Pass; Event count = bit count; validator warns compliance without limits
- Conflicts with: Area 4 (`ProgramCatalog`) — rebase 4 onto 2

---

### Area 3: Timing chrome

- Goal: Chart shows event ticks + out-of-band spans; `TimingStripView` + `timing` role ship together; elapsed/event label on Chart toolbar.
- Depends on: Area 2
- Out of scope: health gate, Analyze mode, bit-field editor
- Likely files: `PresentationRoleMap`, `PresentationTileViewModel`, `LivePresentationViewModel*.cs`, `MeasurementPlotView`, new `TimingStripView`, Results AXAML, ViewModel tests
- Public surface:

```csharp
PresentationDisplayRoles.Timing = "timing"
PresentationTileKind.Timing
LivePresentationViewModel: Events[], ApplyEvent(mark), ChartElapsedText, ChartEventLabel
MeasurementPlotView.SetEvents / SetOutOfBandSpans
```

- Pseudo-code:

```
ApplySample: if ElapsedMs set, Append uses elapsed/1000 else timestamp delta
ApplyEvent: store mark; refresh strip
IsOutOfBand span: contiguous samples outside limits → (t0,t1)
timing role → strip tile, not gauge
unknown role still text
```

- Tests: Phase16-style chrome; event marks; OOB attention; timing maps to strip; unknown degrades
- Conflicts with: Area 4 (low)

---

### Area 4: Station health store + step

- Goal: `stationHealth` program runs without DUT serial, publishes age + cal Scalars, writes `{DataDirectory}/station-health/{profileId}.json`.
- Depends on: Area 2 (catalog); consumes Area 1 `ResultSource`
- Out of scope: Run gate, Settings chrome beyond persist
- Likely files: `HardwareTest.Core/StationHealth/*`, `ReportStationHealthStep.cs` (new), `StationHealthDemoProgramFactory`, `ProgramSidecar` + `program.schema.json` + `PlanContractSidecar` known keys, schema fixture
- Public surface:

```csharp
public sealed class StationHealthRecord { SchemaVersion, ProfileId, MeasuredAt, Source, Verdict, ProgramId, RunId, Metrics[], MaxAgeHours }
interface IStationHealthStore { TryRead(profileId); WriteAsync(record); }
// sidecar: programKind, stationHealthProfileId (require/gate fields parsed but unused until Area 5)
```

- Pseudo-code:

```
terminal stationHealth run → WriteAsync from published Scalars + clock
programKind stationHealth → RequireSerial default false
unknown programKind → contract error
```

- Tests: store round-trip; health run writes; DUT serial not required; unknown kind errors
- Conflicts with: Area 5

---

### Area 5: Optional Run gate

- Goal: DUT sidecar `requireStationHealth` + `warn`|`block`; missing/failed/old record is stale; `IClock`; Settings one-liner.
- Depends on: Area 4
- Out of scope: skip/replay steps, DUT history as cache, Cal page, block on clock skew
- Likely files: `IStationHealthGate`, `RunExecutionViewModel`, `ProgramRequirements`/`ProgramHealthGate`, Settings VM, `AppSettings` override keys, architecture wall-clock rule
- Public surface:

```csharp
Evaluate(program, clock) → { Off|Ok|Warn|Block, Message, Age, Verdict }
// BlockStart on Block; strip Warning on Warn
// HARDWARETEST_STATION_HEALTH_GATE=off|warn|block
```

- Tests: FakeClock stale → warn/block; override off; no sidecar keys → no gate; health program itself not gated
- Conflicts with: none after 4
