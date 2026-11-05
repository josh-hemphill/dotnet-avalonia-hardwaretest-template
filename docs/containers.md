# Containers — local CI parity and appliance rails

Podman is the documented path (OCI-compatible; Docker works for build/run). Quadlets over compose. Node.js tasks in [`tools/ci/`](../tools/ci/) are the single source of truth for CI steps — GitHub Actions and a laptop both call them.

Related: [appliance-linux.md](appliance-linux.md) (publish layout), [testing.md](testing.md) (which suite to run).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned in `global.json`) for native runs
- [Node.js](https://nodejs.org/) 26.x for the task runner
- Optional: [Podman](https://podman.io/) for the Linux matrix / image rails

## Native tasks (Windows or Linux host)

From the repo root:

```bash
npm --prefix tools/ci run list
npm --prefix tools/ci run build -- --rid win-x64          # or linux-x64
npm --prefix tools/ci run format
npm --prefix tools/ci run test:arch -- --rid win-x64
npm --prefix tools/ci run test:host -- --rid win-x64
npm --prefix tools/ci run test:vm -- --rid win-x64
npm --prefix tools/ci run test:e2e -- --rid win-x64
npm --prefix tools/ci run coverage -- --rid win-x64
npm --prefix tools/ci run audit
npm --prefix tools/ci run publish -- --rid win-x64
npm --prefix tools/ci run verify -- --rid win-x64
npm --prefix tools/ci run all -- --rid win-x64            # full host matrix
```

The `.ts` scripts run directly with Node 26’s unflagged type stripping: `node tools/ci/main.ts list`. No `npm install`, transpiler, or runtime dependencies are needed. Keep scripts in erasable TypeScript (no enums or parameter properties), use explicit `.ts` import extensions, and use `import type` for type-only imports. Node strips types without type-checking.

RID defaults to the host (`win-x64` on Windows, `linux-x64` on Linux x64). Coverage floors live in TypeScript (`tools/ci/lib/coverage.ts`); there is no Python dependency.

Unit tests for the coverage port and task catalog:

```bash
npm --prefix tools/ci run test
```

### Platform honesty

| Surface | Windows host | Linux container / ubuntu CI |
| --- | --- | --- |
| `build` / `test:arch` / `test:host` / `test:vm` / `publish` / `audit` / `coverage` | Required | Required (`linux-x64`) |
| `test:e2e` (Avalonia headless) | Required | **Advisory** (`E2E smoke (advisory on Linux)` + `continue-on-error`) |
| Event Log sink, `win-x64` TypstInterop, WinExe | Covered | Not testable |

## CI image (`Containerfile.ci`)

Base: .NET 10.0.302 SDK (noble amd64), **pinned by digest**. Adds Node.js, fontconfig/Skia-ish deps for headless Avalonia, non-root `ciuser`, NuGet cache under `/home/ciuser/.nuget/packages`.

```bash
podman build -t localhost/hardwaretest-ci:latest -f Containerfile.ci .
podman run --rm \
  -v "$PWD":/src:Z \
  -v hardwaretest-nuget:/home/ciuser/.nuget/packages:Z \
  -w /src \
  localhost/hardwaretest-ci:latest \
  npm --prefix tools/ci run all -- --rid linux-x64
```

`verify` inside that run asserts `--version` and `--print-config` (DataDirectory overlay provenance) against the published `linux-x64` binary.

## NuGet lockfiles

Each project has a committed `packages.lock.json`. `Directory.Build.props` sets `RestorePackagesWithLockFile=true` and `RestoreLockedMode=true` when `CI` is set, so GitHub Actions restore cannot float.

To refresh locks after a package bump (CI **unset**):

```bash
env -u CI dotnet restore dirs.proj --force-evaluate
env -u CI dotnet restore tools/gen-sample-plan/gen-sample-plan.csproj --force-evaluate
```

Commit every updated `packages.lock.json` in the same change as the package version bump.

`.dockerignore` keeps `bin/`, `obj/`, `artifacts/`, tests results, and crash dossiers out of image build contexts.

## Quadlets (`deploy/quadlets/`)

| Unit | Role |
| --- | --- |
| `hardwaretest-ci.container` + `hardwaretest-nuget.volume` | One-shot Linux CI matrix |
| `hardwaretest-appliance.container` + `hardwaretest-appliance-data.volume` | Placeholder only — no kiosk, no baked app |

User install sketch:

```bash
mkdir -p ~/.config/containers/systemd
cp deploy/quadlets/hardwaretest-ci.container \
   deploy/quadlets/hardwaretest-nuget.volume \
   ~/.config/containers/systemd/
# Edit the Volume= bind in hardwaretest-ci.container to your checkout path.
systemctl --user daemon-reload
systemctl --user start hardwaretest-ci.service
```

`podman kube play` against a generated pod YAML is optional; prefer the quadlet units or the plain `podman run` above. If your Podman is too old for Quadlet, use `podman run` (compose is a last resort and is not checked in).

## Appliance stub (`Containerfile.appliance`)

Rails for the future sealed image: directory layout comments, `HARDWARETEST_DATA_DIRECTORY`, stub CMD. **Not published** to a registry. Build locally to prove the file stays valid:

```bash
podman build -t localhost/hardwaretest-appliance:stub -f Containerfile.appliance .
podman run --rm localhost/hardwaretest-appliance:stub
```

Product layout and publish flags: [appliance-linux.md](appliance-linux.md). No `session/` resume file exists — operator session state is in-process only.

## GitHub Actions

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) is a thin runner over the Node.js tasks:

- **windows-latest** — primary shipping matrix (`win-x64`), including required E2E and coverage.
- **ubuntu-latest** — `linux-x64` build / host / VM / arch / coverage / publish / verify; E2E is **advisory** (step name says so; `continue-on-error`).
- Actions are pinned by commit SHA. Workflow default token is `contents: read`; artifact-upload jobs add `actions: write`. Jobs have `timeout-minutes` and PR concurrency cancel-in-progress.
- Publish artifacts retain self-contained outputs for demo pulls.
- Format is a Node.js catalog task (`main.ts format`); both jobs assert the same catalog and dump expected/actual on drift.
- `setup-dotnet` pins `global.json` (`10.0.302`). Artifact uploads fail if publish output is missing.
- A catalog assert step fails if a Node.js task is renamed without updating the workflow.
- `audit` fails the job when `dotnet list package --vulnerable --include-transitive` reports known vulns.
