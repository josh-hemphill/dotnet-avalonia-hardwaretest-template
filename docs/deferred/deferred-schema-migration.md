# Deferred — Schema migration engine

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Status:** Deferred
**Related:** [phase-5-schema-versioning.md](../platform-phases/phase-5-schema-versioning.md) (Done — stamp + gate)

## Goal

Replace “newer schema → read-only / fail loudly” with an explicit, testable migration pipeline for `settings.json`, `ui-state.json`, and run documents when we intentionally bump versions.

## Locked decisions

- Migrations are **forward-only**, pure functions, covered by fixture tests.
- Failed migration leaves originals intact and surfaces an in-panel error (no silent coerce).
- Unknown future schema remains fail-loud until a migrator exists (keep Phase 5 safety).
- No reflection-based serializer binding — source-generated STJ contexts stay required.

## Workstreams

1. `ISchemaMigrator` registry keyed by document kind + fromVersion → toVersion.
2. Chain short hops (v1→v2→v3) rather than one mega-jump where possible.
3. CLI `--migrate-data` dry-run + apply for appliance upgrades.
4. Fixture corpus under `tests/fixtures/schema/`.

## Exit criteria

- [ ] At least one real settings bump ships with a migrator and tests
- [ ] Dry-run reports planned steps without writing
- [ ] Failure leaves prior files unchanged

## Out of scope

- Migrating arbitrary third-party OpenTAP result databases
- Downgrade / reverse migrations

## Dependencies

- Phase 5 stamp + gate (Done)
