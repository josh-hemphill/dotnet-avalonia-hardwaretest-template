# Deferred — Clock discipline

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Status:** Promoted — scheduled as [Phase 25](../platform-phases/phase-25-clock-discipline.md). This file stays as the deferred-track original; implement against the phase plan.

## Goal

Detect and mitigate wall-clock skew so run history ordering and idle/stale timers remain trustworthy on benches with drifted RTCs.

## Locked decisions

- Prefer **detect + warn** before enforcing NTP write on sealed appliances.
- Timestamps in records remain `DateTimeOffset` with explicit Offset when available.
- Do not block Run solely on skew in v1 — soft-warn in-panel; Engineer decides quarantine.
- No third-party time SaaS requirement; local NTP / domain time is enough.

## Workstreams

1. Startup skew check vs optional reference (NTP query or last-known good file).
2. Settings / Home banner when skew exceeds threshold.
3. Document appliance NTP/chrony expectations in [appliance-linux.md](../appliance-linux.md).
4. Tests: injectable clock for idle stale + retention ordering.

## Exit criteria

- [ ] Skew above threshold surfaces an in-panel warning with measured delta
- [ ] Idle/stale and retention behavior documented under skewed clocks
- [ ] Appliance doc lists required time sync unit

## Out of scope

- Rewriting historical run timestamps
- Cryptographic timestamp authorities

## Dependencies

- Operator session idle ([Phase 11](../platform-phases/phase-11-session-activity-stale.md)) should use injectable clock seams this plan also needs
