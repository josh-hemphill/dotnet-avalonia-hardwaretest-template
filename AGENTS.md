# AGENTS.md

## Cursor Cloud specific instructions

This is a single **.NET 10 Avalonia desktop product** (`HardwareTest`) that runs OpenTAP
test plans in-process with mock instruments by default. There is no separate backend or
database — persistence is file-based under the data directory. Full project/service context
lives in `README.md`, `docs/containers.md`, and `docs/testing.md`; the Deno tasks in
`tools/ci/` are the single source of truth for build/test/publish steps.

### Toolchain (already installed in the VM snapshot)

- **.NET 10 SDK** `10.0.302` (pinned by `global.json`) at `~/.dotnet`.
- **Deno** `2.7.7` at `~/.deno/bin` (drives `tools/ci/`).
- Linux Avalonia/font libs (`libfontconfig1`, `libx11-6`, `libxrandr2`, etc.) for headless E2E and GUI rendering.
- `~/.bashrc` exports `DOTNET_ROOT` and adds `~/.dotnet` + `~/.deno/bin` to `PATH` for interactive shells. Non-interactive scripts that don't source `~/.bashrc` should call the full paths `~/.dotnet/dotnet` and `~/.deno/bin/deno`.

### Non-obvious gotchas

- **A RID is mandatory for every build/test/run/publish** (`-r linux-x64` here). Without it, the `TypstInterop` native library does not restore and the build breaks. Use `linux-x64` on this VM.
- `dotnet format dirs.proj` prints *"Could not format ... only C# and Visual Basic projects"* because `dirs.proj` is an MSBuild traversal project, not a C#/VB project. This matches CI (the Format step is `continue-on-error`). To actually run the formatter, point it at a concrete `*.csproj` (e.g. `dotnet format src/HardwareTest.Core/HardwareTest.Core.csproj --verify-no-changes --no-restore`).
- **`test:host` runs serially** (`-m:1`) because OpenTAP uses global process state; do not parallelize it.
- **One host test is Windows-only and fails on Linux**: `FileRunStoreTests.Invalid_filename_chars_are_sanitized` asserts `:` is stripped from run-directory names. `FileRunStore` sanitizes via `Path.GetInvalidFileNameChars()`, which on Linux does not include `:` (only `/` and NUL), so the char legitimately survives. The primary shipping matrix is `windows-latest`; on Linux this single failure is expected and is **not** an environment problem — do not "fix" it by editing code.
- On Linux, `test:e2e` is **advisory** in CI (`continue-on-error`), though it currently passes here.

### Running the GUI app

The VM has a desktop on `DISPLAY=:1`. Launch the published (or built) binary with mock VISA and a writable data dir:

```bash
export DISPLAY=:1
export HARDWARETEST_USE_MOCK_VISA=true
export HARDWARETEST_DATA_DIRECTORY=/tmp/htdata
~/.dotnet/dotnet run --project src/HardwareTest -c Debug -r linux-x64
# or run the self-contained publish output: artifacts/publish/linux-x64/HardwareTest
```

Hello-world flow: sidebar **Run** → enter DUT serial + technician → **Confirm Session** →
**Run** the *Sample Hardware Suite (Demo)* → click **Continue** on operator prompts →
run finishes **Passed** → **Results** lists the run.
