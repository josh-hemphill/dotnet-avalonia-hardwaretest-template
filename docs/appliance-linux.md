# Linux appliance packaging

Self-contained **non-NativeAOT** publish of the Avalonia OpenTAP host for sealed x64 Linux benches (Wayland-capable).

Image **rails** (Containerfile + quadlet stubs, no bake/kiosk yet): [containers.md](containers.md), [`Containerfile.appliance`](../Containerfile.appliance).

## Layout

After publish (example RID `linux-x64`):

```
appliance/
  app/                 # read-only publish output (Avalonia + OpenTAP + plugins)
    HardwareTest
    Programs/          # locked .TapPlan programs (sample.TapPlan)
    (optional) Plugins/  # third-party OpenTAP DLLs; add path to OpenTapPluginDirectories
  data/                # writable root (map HARDWARETEST_DATA_DIRECTORY here)
    settings.json      # optional; env/CLI alone is enough on a sealed image
    logs/              # Serilog files
    runs/              # run folders + Typst PDFs
    reports/           # optional Typst overrides (see adapting.md)
```

There is **no** on-disk `session/` resume file — operator session / DUT confirmation is in-process only (sticky strip + idle timeout). Restarts require re-confirming the DUT.

Set `HARDWARETEST_DATA_DIRECTORY` (or `--data-directory`) to the writable root so `runs/`, `logs/`, `reports/`, and station overlays live outside the read-only app tree. `settings.json` is optional — environment variables cover every `AppSettings` member (see [adapting.md §10](adapting.md#10-configuration-reference)). Register extra OpenTAP plugin folders via `OpenTapPluginDirectories` or `HARDWARETEST_OPENTAP_PLUGIN_DIRS`. Dump effective config without UI: `HardwareTest --print-config`. Productization steps: [adapting.md](adapting.md).

## Offline OpenTAP packages

Install packages **outside** the Avalonia UI (no feed browser in-app):

1. During image bake or provisioning, run `tap package install <Package.TapPackage>` into the OpenTAP/`Packages` tree under `app/`, **or** copy unpacked package folders (with `package.xml`) under `app/Plugins/` (or another folder listed in `OpenTapPluginDirectories`).
2. On the bench, open **Settings → OpenTAP packages & plugins** and click **Refresh** to verify name, version, and path.
3. **Open folder** may fail on a locked/read-only `app/` tree — that is expected; **Copy path** still works for SSH/docs.

## Publish

```bash
dotnet publish src/HardwareTest -c Release -r linux-x64 --self-contained -p:PublishAot=false -o ./artifacts/appliance/app
# or: deno run -A tools/ci/main.ts publish --rid linux-x64
```

Windows primary smoke (CI `publish-win`):

```bash
dotnet publish src/HardwareTest -c Release -r win-x64 --self-contained -p:PublishAot=false
```

## Time sync

Sealed benches must run a local time-sync unit so idle/stale, run ordering, and retention stay trustworthy. The app **detects and warns**; it does not write the RTC.

- **Linux:** `chrony` (preferred) or `systemd-timesyncd`, pointed at the site NTP / domain time server. Enable and start the unit in the image (for example `chronyd.service` or `systemd-timesyncd.service`).
- **Windows:** `w32time` (Windows Time) against the domain or a local NTP host.
- Optional app-level check: set `NtpHost` / `HARDWARETEST_NTP_HOST` to the same local server. Empty skips NTP and uses `{DataDirectory}/clock-last-good.json` (backward-jump detection only). Timeout is 500ms; **Safety Stop never waits on NTP**.
- Skew above `ClockSkewWarnThresholdMinutes` (default 5) shows on the shell strip and does not block Run.

## Smart card readers (chip and tap)

Real badges use PC/SC (`pcscd` + CCID). Mock badges (`UseMockOperatorCredential`, default on) do not need a reader.

- **Linux:** install `pcscd` and `libccid` (or the vendor CCID package), enable `pcscd.service`, and confirm `pcsc_scan` sees the reader. Contactless readers usually appear with `contactless`, `NFC`, or `PICC` in the PC/SC name.
- **Windows:** Winscard is built in; install the reader’s CCID driver if the OS does not.
- **macOS:** PCSC.framework. Signing on-card is not implemented yet; with `AllowPresenceInLieuOfSigning` (default on) a tap or insert still records that the responsible party was present at export time.
- Do not persist PIN or private keys. There is no on-disk card session.

## Smoke checklist

1. Confirm DUT serial on Run (sticky session strip).
2. Run **Sample Hardware Suite** (embedded or `Programs/sample.TapPlan`).
3. Open Results — run record includes `DutSerial`.
4. Generate / preview Typst report — serial present in report data.
5. Safety Stop / Pause still visible in PaneFooter and abort OpenTAP cleanly.
6. Optional: with mock credential, tap on session confirm fills technician; export with `RequireAttestationBeforeExport` writes `certification.attestation.json`.

## Notes

- NativeAOT is **not** a product gate for the OpenTAP host (plugins + reflection).
- Optional ReadyToRun can be enabled later once the host is stable on target images.
- ARM builds are out of scope for this phase.
- Kiosk session, systemd units, and image bake automation remain deferred — see [containers.md](containers.md) for the stub rails only.
