# Linux appliance packaging

Self-contained **non-NativeAOT** publish of the Avalonia OpenTAP host for sealed x64 Linux benches (Wayland-capable).

## Layout

After publish (example RID `linux-x64`):

```
appliance/
  app/                 # read-only publish output (Avalonia + OpenTAP + plugins)
    HardwareTest
    Programs/          # locked .TapPlan programs (sample.TapPlan)
    (optional) Plugins/  # third-party OpenTAP DLLs; add path to OpenTapPluginDirectories
  station/             # writable: station bindings / overlays
  logs/                # Serilog files
  runs/                # run folders + Typst PDFs
  reports/             # optional Typst overrides (see adapting.md)
  session/             # optional Operator Session resume file
```

Map `AppSettings.DataDirectory` (or host env) to the writable root so `runs/`, `logs/`, `reports/`, and station overlays are outside the read-only app tree. Register extra OpenTAP plugin folders via `OpenTapPluginDirectories` or `HARDWARETEST_OPENTAP_PLUGIN_DIRS`. Productization steps: [adapting.md](adapting.md).

## Publish

```bash
dotnet publish src/HardwareTest -c Release -r linux-x64 --self-contained -p:PublishAot=false -o ./artifacts/appliance/app
```

Windows primary smoke (CI `publish-appliance`):

```bash
dotnet publish src/HardwareTest -c Release -r win-x64 --self-contained -p:PublishAot=false
```

## Smoke checklist

1. Confirm DUT serial on Run (sticky session strip).
2. Run **Sample Hardware Suite** (embedded or `Programs/sample.TapPlan`).
3. Open Results — run record includes `DutSerial`.
4. Generate / preview Typst report — serial present in report data.
5. Safety Stop / Pause still visible in PaneFooter and abort OpenTAP cleanly.

## Notes

- NativeAOT is **not** a product gate for the OpenTAP host (plugins + reflection).
- Optional ReadyToRun can be enabled later once the host is stable on target images.
- ARM builds are out of scope for this phase.
