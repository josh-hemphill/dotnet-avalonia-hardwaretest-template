# Phase 25 — Clock discipline

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 11](phase-11-session-activity-stale.md) (idle/stale already shipped on wall clock)
**Status:** Done
**Also absorbs:** Round-3 R3-16; round-2 “promote before unattended” note

## Goal

Make idle/stale, run history ordering, and retention **trustworthy on benches with drifted RTCs** by injecting a clock, detecting skew, and warning in-panel. Do not block Run solely on skew in v1.

## Why this exists

Every idle/stale decision, run ordering, and retention prune used `DateTimeOffset.UtcNow`. Unattended hardware will lie about “4 hours idle” after a CMOS drift or a VM pause. Round 2 already recommended promoting this out of deferred before the first unattended deployment.

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

- [x] Skew above threshold surfaces an in-panel warning with measured delta
- [x] Idle/stale and retention tests use an injected clock
- [x] Appliance doc lists required time sync unit
- [x] No `DateTime.UtcNow` / `DateTimeOffset.UtcNow` in idle/retention/run-complete paths (except the clock implementation)

## Out of scope

- Rewriting historical run timestamps
- Cryptographic timestamp authorities
- Schema migration engine ([deferred-schema-migration.md](../deferred/deferred-schema-migration.md) — when the first real bump happens, migrators should be pure `JsonNode` functions and tests should not depend on clock skew)
- Blocking Run on skew
- Waiting on NTP in Safety Stop / worker kill

## Landed

- `IClock` / `SystemClock` (`TimeProvider.GetUtcNow`) is registered in Core DI and threaded through idle (`OperatorSession`), retention (`RunRetentionService`), run-complete stamps (`OpenTapRunContext`, worker client, `RunExecutionViewModel`, dangling-run `CompletedAt`), and the Run session strip relative-time copy.
- Startup `ClockSkewDetector` compares the injected clock to optional NTP (`NtpHost`, 500ms timeout) or `{DataDirectory}/clock-last-good.json`. Backward jumps vs last-known-good warn; expected forward progress while powered off does not. Missing reference does not throw or block Run.
- Skew above `ClockSkewWarnThresholdMinutes` (default 5) publishes a dismissible **Warning** on the Phase 17 shell strip with the measured delta. Settings exposes the threshold and optional NTP host. Run is not blocked.
- Safety Stop / worker kill do not call NTP or the skew detector.
- Tests use `FakeClock` for idle/stale and retention; architecture forbids wall-clock `UtcNow` on those paths and NTP waits on Stop/kill files.

## Related

- [deferred-clock-discipline.md](../deferred/deferred-clock-discipline.md) (canonical deferred text; this phase is the scheduled promotion)
- [phase-11-session-activity-stale.md](phase-11-session-activity-stale.md)
