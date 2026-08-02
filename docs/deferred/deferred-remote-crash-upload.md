# Deferred — Remote crash upload

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Status:** Deferred
**Related:** [phase-6-crash-reporting.md](../platform-phases/phase-6-crash-reporting.md) (Done — local dossiers)

## Goal

Add an **optional** uploader that ships local crash dossiers to an operator-controlled endpoint, without embedding third-party crash SDKs or breaking offline benches.

## Locked decisions

- Local dossier format stays stable; upload is additive ([Phase 6](../platform-phases/phase-6-crash-reporting.md)).
- Default remains **offline** — upload is opt-in via settings/env.
- Redaction settings (`RedactIdentifiersInDiagnostics`) apply before upload.
- No new modal dialogs; upload status is in-panel (Home / Settings).
- Do not upload mid-run; wait until safe-stop bookkeeping completes.

## Workstreams

1. `ICrashUploadService` with pluggable HTTP endpoint + auth header from env.
2. Queue unreviewed dossiers; retry with backoff; mark uploaded in dossier metadata.
3. Settings: enable, endpoint (Engineer), last upload Status.
4. Tests: Fake HTTP handler; redaction asserted on payload.

## Exit criteria

- [ ] Opt-in upload sends dossier files to the configured endpoint
- [ ] Offline / failed upload never blocks app start or Run
- [ ] Redaction honored

## Out of scope

- Third-party SaaS SDKs (Sentry, AppCenter, etc.) as hard dependencies
- Live telemetry breadcrumbs beyond crash dossiers

## Dependencies

- Phase 6 local dossiers (Done)
