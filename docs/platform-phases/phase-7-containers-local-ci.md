# Phase 7 — Containerized local CI + appliance image track

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 1](phase-1-repo-gates.md) (CI must run before it is worth reproducing)
**Unblocks:** Linux appliance work; contributors verifying the full matrix before pushing
**Status:** Done

## Goal

One task definition that GitHub Actions and a developer's laptop both execute, runnable natively on Windows or inside a container. The container work simultaneously proves the `linux-x64` path that the long-term appliance depends on, and establishes the image rails those appliance images will later ride.

## Two birds, stated plainly

- **Bird one:** devs can opt into running the CI matrix locally instead of push-and-pray.
- **Bird two:** every local Linux container run is free evidence that the `linux-x64` publish still works, long before the appliance is a deliverable.



## Locked rules

- **Podman, not Docker. Quadlets, not compose** (per project tooling standards, but subject to easy availability in environments). Files stay OCI-standard so Docker users are not blocked, but the documented path is Podman.
- **Deno for the task runner**  (again per project tooling standards, but subject to easy availability in environments)**.** Shell scripting stays inside the GitHub Actions YAML only; everything reusable is TypeScript.
- **The workflow calls the same tasks a developer calls.** If CI and local can drift, they will. The YAML becomes a thin runner.
- **Be honest about the split.** The immediate product is Windows. A Linux container cannot run the `win-x64` matrix, and Windows-only surfaces (Event Log sink, `win-x64` TypstInterop native, WinExe publish) are not testable there. Containers cover the Linux matrix; Windows devs run the same tasks natively.



## Work items



### 1. Task runner (`tools/ci/`)

A Deno CLI exposing one task per CI step, each independently runnable:


| Task        | Does                                                          |
| ----------- | ------------------------------------------------------------- |
| `build`     | `dotnet build dirs.proj -c Release -r {rid}`                  |
| `test:host` | OpenTAP host + fixtures, with coverage collection             |
| `test:vm`   | ViewModels suite                                              |
| `test:e2e`  | Avalonia headless E2E                                         |
| `test:arch` | Architecture suite ([Phase 2](phase-2-architecture-tests.md)) |
| `coverage`  | Parse Cobertura, enforce floors                               |
| `publish`   | Self-contained publish for a given RID                        |
| `all`       | The full matrix for the current platform                      |


RID is a parameter defaulting to the host platform, so the same invocation works on both sides.

Port `tests/check-coverage.py` to TypeScript as part of `coverage`. It is the only Python dependency in the repo, and removing it keeps the CI image to the .NET SDK plus Deno.

### 2. `Containerfile.ci`

.NET 10 SDK base plus Deno and the native dependencies the Linux test matrix needs (Skia/fontconfig for headless Avalonia, the TypstInterop native for `linux-x64`). Non-root user. NuGet cache mounted as a volume so repeat runs are fast.

Pin the base image by digest — a CI image that silently changes underneath you is worse than no CI image.

### 3. Quadlet units (`deploy/quadlets/`)

`hardwaretest-ci.container` for the one-shot test run, plus a `.volume` for the NuGet cache. Document `podman kube play` / `systemctl --user` usage in a new `docs/containers.md`. (Quadlet support is newer, if we have trouble being able to use a new enough version of Podman, we'll fall back to using compose files)

### 4. GitHub Actions rework

- Keep `windows-latest` as the primary job — this is the shipping platform. It calls the Deno tasks directly.
- Add an `ubuntu-latest` job running `build`, `test:host`, `test:vm`, `test:arch`, and `publish` for `linux-x64`, replacing the deferred comment at the bottom of `[ci.yml](../../.github/workflows/ci.yml)`.
- Decide explicitly whether Linux E2E (headless Avalonia) is required or advisory at first; start advisory.
- Publish artifacts with retention so a demo build can be pulled from a run.



### 5. Verify the container matches CI

A `verify` task that runs `--print-config` ([Phase 3](phase-3-configuration-model.md)) and the version banner ([Phase 4](phase-4-build-info.md)) inside the container and asserts the expected RID and configuration source. This is what stops "works in CI, not in the container" from becoming a recurring afternoon.

### 6. Appliance image stub (rails only, no product)

A `Containerfile.appliance` skeleton and a placeholder quadlet, built but **not** published, wired so the [appliance layout](../appliance-linux.md) can be filled in later without re-litigating tooling choices. Explicitly a stub: no kiosk session, no systemd service, no image bake.

While here, correct [appliance-linux.md](../appliance-linux.md), which promises an env-based `DataDirectory` ([Phase 3](phase-3-configuration-model.md) delivers it) and a `session/` resume file that does not exist.

## Tests

- `deno task all` on a clean clone reproduces the CI result on Windows.
- The container image builds and runs the Linux matrix green from a clean checkout.
- The coverage port reports the same percentages as `check-coverage.py` on a checked-in Cobertura fixture.
- CI fails if a Deno task is renamed without updating the workflow (task list is asserted, not assumed).



## Exit criteria

- [x] A contributor can run the full local matrix with one command and no .NET-version archaeology.
- [x] `linux-x64` build, tests, and publish are green in CI.
- [x] No Python dependency remains.
- [x] `docs/containers.md` covers local use, CI parity, and the appliance stub's intent.



## Out of scope

- Publishing images to a registry.
- Appliance OS integration: kiosk, systemd services, image bake, provisioning.
- ARM64 or macOS matrices.
- Running the `win-x64` matrix in a container.

