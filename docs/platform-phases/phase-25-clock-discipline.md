# Phase 25 — Clock discipline

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 11](phase-11-session-activity-stale.md) (idle/stale already shipped on wall clock)
**Status:** Planned — **promoted** from [deferred-clock-discipline.md](../deferred/deferred-clock-discipline.md)
**Also absorbs:** Round-3 R3-16; round-2 “promote before unattended” note

## Goal

Make idle/stale, run history ordering, and retention **trustworthy on benches with drifted RTCs** by injecting a clock, detecting skew, and warning in-panel. Do not block Run solely on skew in v1.

## Why this exists

Every idle/stale decision, run ordering, and retention prune uses `DateTimeOffset.UtcNow`. Unattended hardware will lie about “4 hours idle” after a CMOS drift or a VM pause. Round 2 already recommended promoting this out of deferred before the first unattended deployment.

## Locked decisions

- Prefer **detect + warn** before enforcing NTP write on sealed appliances.
- Timestamps in records remain `DateTimeOffset` with explicit Offset when available.
- Do not block Run solely on skew in v1 — soft-warn in-panel; Engineer decides quarantine.
- No third-party time SaaS; local NTP / domain time is enough.
- **`TimeProvider` (or equivalent) is injected** into idle, retention, and run-store timestamping. Tests must not use wall clock.
- Safety Stop / worker kill paths must not wait on NTP.

## Workstreams

1. Introduce `TimeProvider` (or `IClock`) in Core; thread it through `OperatorSessionIdle`, retention, and `FileRunStore` timestamps.
2. Startup skew check vs optional reference (NTP query or last-known-good file).
3. Settings / Home / shell-strip banner when skew exceeds threshold.
4. Document appliance NTP/chrony expectations in [appliance-linux.md](../appliance-linux.md).
5. Tests: injectable clock for idle stale + retention ordering.

## Exit criteria

- [ ] Skew above threshold surfaces an in-panel warning with measured delta
- [ ] Idle/stale and retention tests use an injected clock
- [ ] Appliance doc lists required time sync unit
- [ ] No `DateTime.UtcNow` / `DateTimeOffset.UtcNow` in idle/retention/run-complete paths (except the clock implementation)

## Out of scope

- Rewriting historical run timestamps
- Cryptographic timestamp authorities
- Schema migration engine ([deferred-schema-migration.md](../deferred/deferred-schema-migration.md) — when the first real bump happens, migrators should be pure `JsonNode` functions and tests should not depend on clock skew)

## Related

- [deferred-clock-discipline.md](../deferred/deferred-clock-discipline.md) (canonical deferred text; this phase is the scheduled promotion)
- [phase-11-session-activity-stale.md](phase-11-session-activity-stale.md)
