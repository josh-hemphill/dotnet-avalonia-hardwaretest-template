# Phase 3 — Configuration & environment model

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 1](phase-1-repo-gates.md)
**Unblocks:** [Phase 4](phase-4-build-info.md) (shares the diagnostics surface), [Phase 6](phase-6-crash-reporting.md) (dossier records effective config), [Phase 7](phase-7-containers-local-ci.md) (containers configure by env only)
**Status:** Done

## Goal

One predictable layering order for configuration, full environment-variable coverage, and — the part that actually saves debugging time — **provenance**: for every effective setting, where the value came from.

## Why

Today [`SettingsStore`](../../src/HardwareTest.Core/Settings/SettingsStore.cs) reads `settings.json` and nothing else. There is exactly one environment variable in the codebase (`HARDWARETEST_OPENTAP_PLUGIN_DIRS`), and [appliance-linux.md](../appliance-linux.md) already promises a `DataDirectory` env override that does not exist. When an operator's bench misbehaves, there is no way to answer "what is this app actually configured with, and who set it" short of reading a JSON file over their shoulder — and on a sealed appliance you may not be able to.

## Locked rules

- **Precedence, low to high:** built-in defaults → `settings.json` → environment variables → command-line arguments.
- **Env alone must be sufficient.** A sealed or read-only install configures entirely by environment; a missing or unwritable `settings.json` is a degraded state, not a fatal one.
- **No `Microsoft.Extensions.Configuration`.** Its binder is reflection-based and this repo sets `JsonSerializerIsReflectionEnabledByDefault=false` deliberately. Hand-write the binder — it also gives provenance for free, which the framework binder does not.
- **Never log or display a value that could be a secret** without going through the same redaction policy [Phase 6](phase-6-crash-reporting.md) uses.

## Work items

1. **Naming convention** — `HARDWARETEST_` prefix, screaming snake case, `__` for nesting and `__{n}` for list indices:

   | Setting | Variable |
   | --- | --- |
   | `DataDirectory` | `HARDWARETEST_DATA_DIRECTORY` |
   | `UseMockVisa` | `HARDWARETEST_USE_MOCK_VISA` |
   | `LogMinimumLevel` | `HARDWARETEST_LOG_MINIMUM_LEVEL` |
   | `IsEngineerDebugMode` | `HARDWARETEST_ENGINEER_DEBUG` |
   | `OpenTapPluginDirectories` | `HARDWARETEST_OPENTAP_PLUGIN_DIRS` *(keep existing name and separator semantics)* |

   The existing plugin-dirs variable keeps its name — it is already documented in [adapting.md](../adapting.md#2-plugins) and baked into appliance instructions. Note the inconsistency in the table rather than breaking it.

2. **Two-stage resolution.** [`Program.cs`](../../src/HardwareTest/App/Program.cs) needs `DataDirectory` and `LogMinimumLevel` *before* logging exists, but those are themselves configurable. Split it:

   - **Stage 1 (bootstrap):** resolve root directory and log level from env + command line only. No file I/O beyond `Directory.CreateDirectory`. Logging starts here.
   - **Stage 2 (full):** load `settings.json` from the now-known root, then re-apply env and command-line overlays on top. Log the stage-1 → stage-2 delta at Debug.

   This ordering is the subtle part of the phase; write it down in the code as well as here.

3. **Explicit binder.** A hand-written `AppSettingsEnvironmentBinder` with one entry per overridable setting: variable name, parser, target setter. Parse failures are **warnings that keep the previous value**, never silent zeroes and never a startup crash — a typo in a bench provisioning script must not brick the station.

4. **Command-line arguments** — `--data-directory`, `--log-level`, `--mock-visa`, `--settings <path>`, mapped through the same binder table so args and env can never disagree about what a setting means.

5. **Provenance record.** Alongside `AppSettings`, produce an `IReadOnlyList<SettingProvenance>`:

   ```
   SettingProvenance { Key, EffectiveValue, Source, RawValue, SourceDetail }
   Source ∈ Default | SettingsFile | Environment | CommandLine
   ```

   `SourceDetail` carries the file path or variable name. This is the deliverable that makes the phase worth doing — everything else is plumbing.

6. **Surfaces:**
   - **Settings → Diagnostics** in-panel table (key, effective value, source), read-only, with copy-all. Shares the card layout with [Phase 4](phase-4-build-info.md).
   - **Startup log** at Debug: one line per non-default setting.
   - **`--print-config`**: dump effective config + provenance to stdout and exit `0`. Invaluable for supporting a locked bench over the phone, and it is how [Phase 7](phase-7-containers-local-ci.md) asserts container configuration without launching a UI.
   - **`--validate-plan`**: validate a `.TapPlan` (or a directory of plans) against the Run-board contract and exit without launching a UI. See [adapting.md §4](../adapting.md#4-plan-contract-run-board).

7. **Write-back semantics.** Saving from the Settings page writes `settings.json` only. A value currently overridden by env or command line must show as overridden and **not** be silently persisted — otherwise the override becomes permanent the first time someone touches an unrelated toggle. Mark those rows read-only in the UI with the reason.

8. **Degraded persistence.** `SaveAppSettingsAsync` currently calls `File.Create` with no guard. Handle read-only and full-disk by surfacing a status message and continuing in memory.

9. **Docs** — a configuration reference table in [adapting.md](../adapting.md), and correct [appliance-linux.md](../appliance-linux.md), which currently promises env-based `DataDirectory` and a `session/` resume file that does not exist.

## Tests

- Precedence: file value beaten by env, env beaten by command line, for one string / one bool / one int / one list.
- Malformed env value keeps the prior value and records a warning.
- Missing `settings.json` yields all-`Default` provenance and does not throw.
- Read-only `settings.json` degrades without a crash.
- `--print-config` output contains every key in `AppSettings`.
- Stage-1 log level actually takes effect before stage 2 runs.

## Exit criteria

- [x] Every `AppSettings` member is settable by environment variable.
- [x] Settings → Diagnostics shows the source of every effective value.
- [x] `--print-config` works on a machine with no `settings.json`.
- [x] An env-overridden setting cannot be silently overwritten by a UI save.

## Out of scope

- Per-station config profiles or a config server.
- Secret storage / credential management (nothing in `AppSettings` is a secret today — revisit if that changes).
- Hot reload of `settings.json` while running.
