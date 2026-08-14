# Phase 20 — CI honesty & supply chain

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 19](phase-19-immediate-correctness.md) (format gate already points at `HardwareTest.slnx`)
**Unblocks:** trustworthy release artifacts before unattended / LTS talk
**Status:** Done
**Also absorbs:** Round-2 CI follow-ups still open in [review-post-phase-15.md](review-post-phase-15.md#ci-and-supply-chain); round-3 R3-11

## Goal

Make CI **mean what it prints**: restores are reproducible, coverage numbers come from suites that can actually collect, Actions are pinned, workflows cannot leak extra permissions, and publish metadata does not embed wall-clock `UtcNow`.

## Why this exists

Phase 1 made CI run. Phase 7 added Linux. The format gate was inert until Phase 19. Remaining honesty gaps still let a green check hide floating NuGet restores, Coverlet poisoning OpenTAP host tests (`BadImageFormatException`), advisory Linux E2E, and non-deterministic `InformationalVersion`.

## Locked decisions

- **Lockfiles** (`packages.lock.json` per project, `RestorePackagesWithLockFile`, `RestoreLockedMode` in CI). Commit them. Cache keys that already mention lockfiles must start hitting files.
- **Do not attach Coverlet to OpenTAP host tests.** Coverage floors stay on Core (and other Avalonia-free / process-safe assemblies). Host suite runs **without** collectors.
- **Pin Actions by full SHA** (with version comments). Floating major tags (`@v4`) are not acceptable for a hardware product CI.
- **Workflow `permissions:` least privilege**, `timeout-minutes` on every job, `concurrency:` cancel-in-progress on PRs.
- **`InformationalVersion` must be deterministic** given commit + version prefix. No `DateTime.UtcNow` in the stamp. Build time can live in a separate non-version field if operators need it.
- Linux E2E becomes **blocking** only after it is green in CI for a documented stretch; until then keep advisory **and** say so in the job name. Do not silently `continue-on-error` without the word “advisory”.
- Add a **vulnerability scan** (`dotnet list package --vulnerable` or equivalent) that fails on known-severity threshold. Do not invent a second SBOM product in this phase.
- `.dockerignore` (or equivalent) so publish/container contexts do not ship tests, `artifacts/`, or crash dossiers.
- Keep one Deno task catalog; if a new `format` / `audit` task is added, update the CI catalog assertion in the same commit.

## Workstreams

### A — Restore reproducibility

- Enable lock files on all `.csproj` (or `Directory.Packages.props` + locks).
- CI `dotnet restore --locked-mode`.
- Document how to refresh locks after a package bump: `env -u CI dotnet restore dirs.proj --force-evaluate` (and the gen-sample-plan project). Commit every `packages.lock.json`. See [containers.md](../containers.md#nuget-lockfiles).

### B — Coverage split

- Host (`test:host`) without Coverlet.
- Coverage job only on Core (existing floors) plus any new safe assemblies.
- Quarantine / delete the XPlat collector from process-global OpenTAP tests.

### C — Workflow hardening

- SHA-pin `actions/checkout`, `setup-dotnet`, `setup-deno`, `upload-artifact`.
- `permissions: { contents: read }` default; publish job adds `actions: write` only if artifact upload requires it.
- `timeout-minutes` (e.g. 30 test / 20 publish).
- `concurrency:` per ref for `pull_request`.

### D — Build identity & scan

- Remove `UtcNow` from `InformationalVersion`; keep commit SHA + package version.
- `dotnet list package --vulnerable --include-transitive` (or `dotnet nuget why` once it covers this) as a CI step.
- `.dockerignore` matching publish layout.

### E — Linux E2E policy

- Either make `test:e2e` on `ubuntu-latest` blocking, or rename the step to **E2E smoke (advisory)** and track the blocker (headless flakiness) in this plan’s exit criteria — do not leave a quiet `continue-on-error`.

## Exit criteria

- [x] `packages.lock.json` exists and CI restore uses locked mode
- [x] Host tests do not load Coverlet; coverage floors still gate Core
- [x] Actions pinned by SHA; jobs have permissions, timeouts, concurrency
- [x] Informational version is reproducible for a given commit
- [x] Vulnerability scan runs in CI
- [x] Linux E2E is either blocking-green or explicitly advisory in the step name
- [x] Catalog assertion stays in sync with `tools/ci/main.ts`

## Out of scope

- Signing / Authenticode / Notarization
- Moving off GitHub Actions
- Making Mixins / UI assemblies coverage-gated
- OpenTAP worker isolation ([Phase 23](phase-23-safety-opentap-worker.md)) — that also fixes host-test poisoning, but coverage split must not wait on it

## Related

- [phase-1-repo-gates.md](phase-1-repo-gates.md)
- [phase-7-containers-local-ci.md](phase-7-containers-local-ci.md)
