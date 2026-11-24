# HardwareTest test-set starter

Keep the test workspace, procedure tables, and optional DUT plugin source in this repository. The `hardwaretest-release.json` included in the starter archive pins an exact HardwareTest release and SHA-256 for each supported app archive.

## Get the matching apps

Find this starter and the matching app archive in [HardwareTest Releases](https://github.com/josh-hemphill/dotnet-avalonia-hardwaretest-template/releases). Choose Windows x64 or Linux x64. Compare the archive SHA-256 with the corresponding entry in `hardwaretest-release.json` before extracting it. On Linux use `tar -xf` to preserve executable permissions. Keep the extracted app tree together. Operator is at its root and Authoring is in `authoring/`.

## Run your first test

1. Open the starter's `workspace/` in Authoring. Go to **Environment → Prepare environment**, then **Validate saved plans**. Preview the sample or edit its threshold and **Save and check draft**.
2. With the Operator idle, use **Build → Prepare bench files** and select the extracted Operator's `Programs/` folder. Keep the generated files together and reload programs in Operator.
3. Use a separate station data directory with Mock VISA enabled. On Linux from the app folder, run `HARDWARETEST_USE_MOCK_VISA=true ./HardwareTest --data-directory /absolute/path/to/station-data`. On Windows PowerShell set `$env:HARDWARETEST_USE_MOCK_VISA = "true"` before launching `HardwareTest.exe --data-directory C:\path\to\station-data`.
4. Select **Starter Voltage Test (Demo)**, confirm the demo DUT and technician, and run the program. Open Results and export evidence for that run.

Create your own plan or edit the sample in Authoring. Give your test-set package a distinct name and version before shipping it. Commit the workspace source and the assets its plans read. Keep generated output and production station data outside the test-set source.

## Build and deploy

The included Linux GitHub Actions workflow downloads the pinned app archive, checks its digest and build identity, prepares an isolated OpenTAP home, validates saved plans, and creates a program `.TapPackage`. Run the same operations with Authoring's `--bootstrap`, `--validate --strict`, and `--pack` CLI. Packing uses the .NET SDK pinned in `global.json`.

Build optional DUT plugins separately and import the approved TapPackage into Authoring's **Environment** before validating. On a real bench install the matching plugin packages, stage the plan with its sidecar and required assets, and qualify the station with its physical hardware. Use the full program package or qualified staging for table-backed plans. [Offline bench delivery](https://github.com/josh-hemphill/dotnet-avalonia-hardwaretest-template/blob/latest/docs/offline-bench-delivery.md)
