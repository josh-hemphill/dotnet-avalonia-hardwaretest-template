# Ship a test program to an offline bench

The authoring app can collect the selected program and its required packages into
one checked folder. The bench can inspect it without a connection and prepare a
separate operator installation. The original application and station data stay
available unchanged.

## Prepare on the authoring workstation

1. Save and compile the selected program, then open **Build → Prepare bench files**.
2. Select **Include packages for an offline bench** and the destination bench's
   platform: Windows x64, Linux x64 or Linux ARM64.
3. Optionally choose a **Local package folder** containing trusted `.TapPackage`
   or `.zip` archives. Same-platform prepared authoring packages can also supply
   dependencies. For another platform, provide its matching original archives.
4. Enable **Download missing packages for this bundle** if repository access is
   available. Otherwise leave it off and use local archives. Missing packages and
   conflicting versions identify the requirement to correct; the options remain
   available for a retry.
5. Choose a transfer destination and **Prepare offline bundle**. Copy the complete
   generated folder to removable media. It contains the compiled plan/settings
   pair, receipt, required package archives, exact dependency lock, checksums,
   instructions and check/install helpers. Unrelated optional packages are omitted.

Required packages include both the saved workspace requirements and package
dependencies recorded in the compiled plan, plus their transitive dependencies.
The resolver keeps every version constraint; a package folder containing a newer
version does not override a conflicting requirement or the operator's engine
baseline.

The offline bench must already have the matching operator release shown in the
bundle details. Obtain that published release and any OS/vendor-driver prerequisites
before disconnecting a cold bench. This folder is not an OS or standalone operator
installer. Other package installation actions or external prerequisites need the
station deployment process. The engine's exact Linux executable-permission
declaration is applied as file permissions; its known Windows in-place upgrader
is not invoked for the verified same-version fresh copy. No commands or arbitrary
package actions run.

Use the complete published operator release, including its worker, OpenTAP engine
and matching `OpenTap.Package.dll` SDK. The SDK assembly version must match the
loaded engine; package names or a matching display version alone are insufficient.
Checking also compares protected runtime payload with the selected operator's
exact release bytes. A missing or mismatched SDK requires restoring that release,
not copying an arbitrary SDK DLL into the installation. The offline bundle cannot
upgrade or downgrade the operator's engine or its protected built-in components.

## Check on the disconnected bench

Open **Settings → Offline bench bundle**, select the transferred folder and choose
**Check bundle**. Review the program, target, exact packages and full inspection
details. Checking does not install packages or qualify physical hardware.

Alternatively, from the bundle folder use a helper with the absolute path to the
matching operator executable:

```sh
sh check-offline-bundle.sh /opt/current-operator/HardwareTest
```

On Windows use `check-offline-bundle.cmd C:\CurrentOperator\HardwareTest.exe`.
Helpers verify the release capability and associated application hashes before
passing new options to it; an older app is refused. Linux helpers require
`sha256sum`; Windows helpers use PowerShell without changing execution policy.
If a helper is blocked, use the matching current app's Settings workflow to
inspect and copy the explicit installation command. Hashes detect content changes;
obtain the operator and packages from trusted publishers.

## Prepare and activate a separate copy

Choose two **new, nonexistent absolute folders**: one for the application and one
for fresh station data. In Settings, enter these under **Installation instructions**
and copy the installation command. It performs the same operation as:

```sh
sh install-offline-bundle.sh /opt/current-operator/HardwareTest /opt/new-operator /var/lib/new-bench-data
```

On Windows use `install-offline-bundle.cmd` with the operator path and both new
folders. The helper checks again, clones the compatible application runtime,
adds the checked packages/program and publishes the new copy. It does not download
anything, overwrite an existing folder, copy credentials/results/references, or
launch the application.

Stop the old operator before using hardware from the new copy. Launch the new
executable using the fresh data directory printed by installation, preserving
other reviewed station launch options. The installed copy is bound to this data
directory; it uses that safe path when launched without a data argument and
refuses other data/settings/plugin overrides. Changed or added application code
also requires a new prepared installation.

Commission the fresh station configuration and identity, perform required health
checks, and acquire new golden references before production. Existing bench data
reuse is not supported by this delivery workflow. Keep the original installation
and its historical data for review; do not copy old credentials or qualification
into the new data folder.

Golden reference compatibility includes the loaded engine's assembly version and
module identity, alongside the program and effective bench configuration. After
an engine build change, acquire and qualify fresh reference data; do not rename or
copy an old profile to make it selectable. A release change should go through the
matching published operator and a fresh offline installation, followed by station
commissioning and new references.

There is a separate follow-up for ordinary installations that reuse existing
station data: `StationHealthGate` currently checks the selected health profile,
passing verdict and age, and does not bind that approval to the engine or effective
bench configuration. After changing those inputs, explicitly invalidate the old
health approval and rerun station health under the station's commissioning
procedure. The fresh data requirement in this offline workflow prevents inheriting
that approval; it does not add engine/configuration binding to the general gate.

The implementation contract and independent review evidence are in
[offline-bench-workflows.md](offline-bench-workflows.md) and
[offline-bench-review.md](offline-bench-review.md).
