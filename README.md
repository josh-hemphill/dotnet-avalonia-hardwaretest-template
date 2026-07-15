# Avalonia Hardware Test Template

NativeAOT-ready Avalonia 12 desktop template for **declarative hardware test plans**, **IVI VISA I/O with traced messaging**, **ScottPlot live plots**, and **Typst PDF reports**.

## Layout

```
src/
  HardwareTest/           # Avalonia exe — App / Features / Widgets
  HardwareTest.Core/      # Avalonia-free: logging, settings, VISA, plans, engine, runs, reporting
tests/
  HardwareTest.Tests/              # Core unit + plan regression fixtures
  HardwareTest.ViewModels.Tests/   # ViewModel unit tests (fakes)
  HardwareTest.E2E.Tests/          # Avalonia Headless UI flows
templates/
  plans/                  # sample JSON plans (also embedded)
  reports/                # Typst templates (also embedded)
dirs.proj                 # Microsoft.Build.Traversal entry
```

**Hard separation:** `HardwareTest.Core` has zero Avalonia references. Features call Core services via explicit DI in `App/Composition.cs`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (this repo pins via `global.json`)
- On Windows, [Desktop development with C++](https://learn.microsoft.com/cpp/windows/overview-of-windows-programming) workload for NativeAOT publish
- Optional: vendor VISA runtime (NI-VISA / Keysight IO Libraries) for real instruments — mock VISA is the default

## Build & run

```bash
dotnet build dirs.proj
dotnet run --project src/HardwareTest -c Debug -r win-x64
```

> **RID required:** TypstInterop only restores its native library when a runtime identifier is set (`-r win-x64`, `linux-x64`, or `osx-arm64`). Building the app without a RID emits `TYPST0001` and PDF generation will fail with `DllNotFoundException`.

```bash
# Always pass a RID so Typst native assets restore (required for report tests)
dotnet test tests/HardwareTest.Tests -r win-x64
dotnet test tests/HardwareTest.ViewModels.Tests -r win-x64
dotnet test tests/HardwareTest.E2E.Tests -r win-x64
```

## Testing

Four layers:

| Layer | Project | Focus |
|---|---|---|
| Core unit | `HardwareTest.Tests` | VISA gate/mock/acquire, engine cancel/assert/error, plans, runs, Typst |
| ViewModel unit | `HardwareTest.ViewModels.Tests` | Run/Results/Settings/MainWindow VMs behind fakes |
| Headless E2E | `HardwareTest.E2E.Tests` | Avalonia Headless: nav, run→finish, results, report preview |
| Regression + CI | plan fixtures + GitHub Actions | Declared plan outcomes + coverage floors + PublishAot smoke |

**RID:** always use `-r win-x64` (or another declared RID) when testing Typst PDF generation. Without a RID, TypstInterop native binaries are missing and `GeneratePdfAsync` fails.

**Coverage:** Core tests collect Coverlet (`tests/coverage.runsettings`). `tests/check-coverage.py` enforces floors — Core ≥ 70%, `Engine` + `Hardware` ≥ 80%:

```bash
dotnet test tests/HardwareTest.Tests -r win-x64 --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings --results-directory artifacts/coverage
python tests/check-coverage.py artifacts/coverage/**/coverage.cobertura.xml
```

**Plan fixtures** live under `tests/HardwareTest.Tests/Fixtures/Plans/` and are executed by `PlanRegressionTests` (pass/fail/cancel/variable assert + loader rejection).

CI (`.github/workflows/ci.yml`) builds/tests on `windows-latest` with `win-x64`, then runs a PublishAot smoke publish of the app.

## NativeAOT publish

```bash
dotnet publish src/HardwareTest -c Release -r win-x64
```

Other RIDs declared on the app project: `linux-x64`, `osx-arm64`.

### AoT / trim checklist

- Compiled bindings (`AvaloniaUseCompiledBindingsByDefault`) + `x:DataType` on views
- Explicit `ViewLocator` factory map (no `Activator` / reflection ViewLocator)
- Explicit DI registration (no assembly scanning)
- STJ source generation; `JsonSerializerIsReflectionEnabledByDefault=false`
- Serilog sinks configured in code (not reflection `ReadFrom.Configuration`)
- ScottPlot: prefer `Signal` / mutate buffers; throttle UI refresh
- VISA: single-threaded session gate + tracing decorator; prefer mock until vendor runtime is present

## Architecture notes

| Area | Location |
|---|---|
| Serilog + Activities | `HardwareTest.Core/Logging` |
| `settings.json` / `ui-state.json` | `%AppData%/HardwareTest/` via `SettingsStore` |
| VISA wrap + mock | `HardwareTest.Core/Hardware` |
| Declarative plans + engine | `HardwareTest.Core/Plans`, `Engine` |
| Run persistence | `%AppData%/HardwareTest/runs/<runId>/` |
| Typst reports | `HardwareTest.Core/Reporting` + `templates/reports` |
| UI features | `HardwareTest/Features/*` |
| Plot / PDF widgets | `HardwareTest/Widgets/*` |

### Sample UX

1. Launch restores window geometry, **last monitor**, theme preference, and last page from `ui-state.json` / `settings.json`
2. **Instruments** → Discover resources, save the registry, and bind logical **roles** (e.g. `dmm`) to registry instrument ids for this station
3. **Run** → Add sample / Open suite JSON into the **right-side suite list** → **Auto** (default) advances suite→suite after success (stops on Failed/Error/Cancelled)
4. Always-visible nav footer: **Pause** / **Resume** / **Safety Stop** (abort + suite `safeShutdown` with VISA priority)
5. Main area shows per-test progress/status; **Details** for sparse log + plot (plot UI throttled by `PlotRefreshHz`)
6. On completion, suite/plan `run.json` + Typst `report.pdf` (plot PNGs beside on-disk `main.typ`; soft-fallback if embeds fail)
7. **Results** / **Report Preview** reopen and print the PDF

### Suites

Suites are single JSON files that **embed full plans inline** (`templates/plans/sample-suite.json`). Default execution is **sequential**; set `"executionMode": "Parallel"` to overlap plan tasks (VISA I/O remains gated).

Prefer **roles** (`"resource": "dmm"`) plus suite/plan `"instruments": { "dmm": "instr0" }` and station bindings in settings — not hard-coded VISA strings. Literal addresses and registry ids still resolve for back-compat.

**Safe shutdown:** declare suite-level `"safeShutdown": [ …steps… ]` (inherited by all plans; a plan may override). Safety Stop / cancel / failure runs those steps before disposing the session.

**Analyze plugins:** `{ "type": "Analyze", "algorithm": "mean-gte", "channel": "VDC", "value": 0.0 }` calls an in-process `IAnalyzeAlgorithm`. Ship complex math later as C# plugins or a MATLAB/Python host — suite JSON only references algorithm ids.

The Run page treats the right-hand list as a prescribed suite sequence for the lab workflow.

### Theme & platform settings

- `ThemePreference`: `System` (default), `Light`, or `Dark`
- `PlotRefreshHz`: caps Run UI/plot refresh rate (samples are still fully recorded)
- Windows Event Log options appear only on Windows; Syslog options only on Unix

### Extending

- **New screen:** add `Features/<Name>/<Name>View(.axaml)` + `ViewModel`, register in `Composition.cs`, add to `ViewLocator` + `MainWindowViewModel.NavigationItems`
- **New plan step:** add a `PlanStep` derived type with `[JsonDerivedType]`, handle it in `TestEngine`, update sample JSON
- **New analyze algorithm:** implement `IAnalyzeAlgorithm`, register in DI
- **New suite:** add an embedded JSON under `templates/plans/` with inline `plans[]`
- **New report:** edit / embed another `.typ` under `templates/reports`

### Run-to-run comparison

`IRunComparisonService` is stubbed for a later iteration.

## License

Template scaffolding — adapt for your product.
