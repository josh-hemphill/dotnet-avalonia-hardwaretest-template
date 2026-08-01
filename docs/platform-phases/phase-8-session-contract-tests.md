# Phase 8 — Session contract test suite

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 1](phase-1-repo-gates.md)
**Unblocks:** [Phase 9](phase-9-runboard-decomposition.md) — a large refactor wants this underneath it
**Status:** Done

## Goal

Run one suite of behavioral assertions against **both** `IOpenTapSession` implementations, so the fake that backs most of the test suite cannot quietly diverge from the real session it stands in for.

## Why

[`IOpenTapSession`](../../src/HardwareTest.OpenTap.Host/OpenTapSession.cs) is 29 members across plan loading, run control, station overrides, parameter access, package listing, and device discovery. [`FakeOpenTapSession`](../../tests/HardwareTest.ViewModels.Tests/Fakes/Fakes.cs) mirrors it in over 1,100 lines, including ~140 lines of hand-written `EnumerateParameters` heuristics.

97 of the ViewModel tests run against that fake. The compiler catches an *added* member, but nothing catches **behavioral** drift: the fake returning `true` where the real session returns `false`, ordering differences, or a property that fires `PropertyChanged` in one and not the other. Every such divergence is a test suite that passes while the product is broken — the most expensive kind of green.

[testing.md](../testing.md) already asserts both share the contract. This phase makes that claim testable.

## Locked rules

- **Contract tests assert the contract, not the implementation.** No sample-plan-specific values, no fake-only affordances. If an assertion cannot hold for both, it belongs in a suite-specific test.
- **`LoadPlanShapeAsync` stays off the interface.** It is deliberately test-only on both concrete types; do not promote it to make this phase easier.
- Keep the real-session variant in the existing `OpenTapSerial` collection — OpenTAP plan execution does not parallelize.
- The fake variant must stay fast. If the contract suite makes ViewModel tests slow, it will get skipped.

## Work items

1. **Shared project** `tests/HardwareTest.Session.Contracts` referencing only `HardwareTest.OpenTap.Host` (for the interface) and xunit. Contains an abstract `OpenTapSessionContractTests` with an abstract factory:

   ```csharp
   protected abstract Task<IOpenTapSession> CreateLoadedSessionAsync(ContractPlan plan);
   ```

   `ContractPlan` is a small enum-free descriptor (`Simple`, `WithLoop`, `WithInteraction`) that each side maps to its own plan source — the real session to a factory plan, the fake to a canned tree. This is the seam that makes one suite serve two very different implementations.

2. **Two concrete subclasses**: one in `HardwareTest.Tests` over the real `OpenTapSession` with `MockDmmInstrument`, one in `HardwareTest.ViewModels.Tests` over `FakeOpenTapSession`. Both test projects already have the necessary references.

3. **Contract areas** — start narrow and grow only when a drift bug is found:

   | Area | Assertions |
   | --- | --- |
   | Initial state | Unloaded session reports empty plan name/path, empty tree, not awaiting operator |
   | Load | `LoadedPlanName`/`LoadedPlanPath` set; tree non-empty; step paths unique; `SafeShutdown` present |
   | Run | Progress is monotonic; a terminal verdict always arrives; `PropertyChanged` fires for the documented properties |
   | Pause / resume | Pause is observable; resume completes the run; no deadlock |
   | Abort | Reaches a terminal state within a bounded time; a second abort is a no-op, not a throw |
   | Run selection | Selection mask keeps `SafeShutdown` enabled |
   | Interaction | Request sets `IsAwaitingOperator` and a non-null `PendingInteraction`; `Resume(response)` clears both |
   | Parameters | `EnumerateParameters` returns stable member keys; set-then-get round-trips; unknown key returns `false` rather than throwing |
   | Catalog | `ListPluginDirectories` / `ListInstalledPackages` / `ListDiscoveredDeviceAddresses` return non-null, never throw |

   The "returns false rather than throwing" assertions matter more than they look — that is precisely the kind of thing a fake gets wrong.

4. **Interface drift ratchet.** Check in an approved-surface snapshot (`IOpenTapSession.approved.txt`) listing every member signature, asserted by a test. Changing the interface then requires updating the snapshot in the same commit, which puts the contract suite in the reviewer's diff. Cheap, crude, and effective.

5. **Wire into CI** as part of the existing host and ViewModel steps; note the suite in [testing.md](../testing.md) with guidance on choosing between a contract test and a suite-specific test.

## Tests

The phase is tests. Its own success criterion is a deliberate mutation: make `FakeOpenTapSession.TrySetParameter` return `true` for an unknown key and confirm the contract suite goes red.

## Exit criteria

- [x] One suite executes against both implementations.
- [x] A behavioral divergence introduced on purpose is caught.
- [x] Adding an `IOpenTapSession` member without updating the approved snapshot fails CI.
- [x] The fake variant adds under two seconds to the ViewModel suite.

## Out of scope

- Splitting `IOpenTapSession` into smaller interfaces. Pin behavior first; a split is a later decision, and this suite is what will make it survivable.
- Removing the legacy `TrySetAcquireSettings` / `TrySetMeanGteThreshold` adapters.
- Contract-testing anything mid-run that requires real instrument timing.
