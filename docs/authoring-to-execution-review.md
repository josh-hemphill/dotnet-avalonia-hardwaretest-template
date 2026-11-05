# Combined authoring-to-execution review

Branch: `review/authoring-to-execution`. This integrates the current review heads
without merging either PR into `latest`:

| Area | Source | Included commit |
| --- | --- | --- |
| Authoring | [PR #239](https://github.com/josh-hemphill/dotnet-avalonia-hardwaretest-template/pull/239) | `a53d3d8df595fa8014f2f55a8e8d17cbcc7a85fd` |
| Operator | [PR #241](https://github.com/josh-hemphill/dotnet-avalonia-hardwaretest-template/pull/241) | `f47c51e687669f4367c667b7523ec6e51cc7d672` |

The source branches remain independent. This branch is a snapshot; later source
PR updates must be merged here to refresh it. Both heads merged without conflicts.

## Checkout and build

From the repository root, with the repository's .NET SDK installed:

```sh
git fetch origin
git switch review/authoring-to-execution
dotnet build src/HardwareTest.Authoring/HardwareTest.Authoring.csproj -c Release
dotnet build src/HardwareTest/HardwareTest.csproj -c Release
```

## Review the handoff

1. Start the authoring app:

   ```sh
   dotnet run --project src/HardwareTest.Authoring -c Release --no-build
   ```

2. Create a separate review workspace. Create a voltage task with mock hardware,
   or create an empty plan and add the required steps. Use a unique stable plan
   ID such as `review-voltage`. Built-in IDs such as `sample` and `board-demo`
   select factory programs in the operator catalog and should not be used for
   this custom-plan handoff.
3. Set the title, required DUT/operator fields, instrument binding, measurement,
   criterion and any operator prompt. Inspect the preview and issues. Use
   **Save all** and **Save draft / compile** as needed until the saved source and
   compiled artifacts are current; saving the sidecar alone does not compile a
   changed sequence.
4. In **Build**, choose **Prepare selected program for bench**, enter a transfer
   folder and prepare the copy. Keep its receipt. Copy the complete generated
   `<planId>` folder, containing the plan, sidecar and `bench-program.json`, into
   the operator build's `Programs` folder:

   ```text
   src/HardwareTest/bin/Release/net10.0/<rid>/Programs/
   ```

   `<rid>` is the platform used by the build, for example `linux-x64` or `win-x64`.
   The receipt verifies the exact compiled pair and required operator features.
   Changed or mixed files are refused. This local review uses the compiled-file
   handoff. The authoring app does not
   automatically install edits into a running operator app; package deployment
   is a separate workflow. Keep the workspace's authoring source files there for
   further edits.
5. Start the operator with a separate, absolute review-data directory:

   ```sh
   dotnet run --project src/HardwareTest -c Release --no-build -- --mock-visa true --mock-operator-credential true --data-directory "<absolute review-data directory>"
   ```

6. Open **Run**, reload programs if already open, select the unique plan and
   inspect **Program details**. Confirm that its ID and path point to the copied
   plan. Confirm **VISA: simulated** and use the mock instrument binding created
   for the review.
7. If a measurement needs a golden unit, enable its **Golden reference** criterion
   in authoring before compiling. Set its reference key, comparison and tolerance;
   the editor shows the units and arithmetic-mean reduction. At the bench, open
   **Golden references**. An engineer can explicitly enable **Reference acquisition**,
   confirm the golden-unit session, acquire measurements and open the saved result.
   Choose **Save golden reference**, name the immutable profile and optionally set
   its validity. Return to **Golden references**, leave acquisition mode, choose a
   compatible profile for each criterion and save the choices. Acquisition results
   are labeled separately and do not qualify station health or production.
8. Enter the required session information, confirm it and explicitly start Run.
   Exercise any prompts and inspect the terminal verdict. Use **View saved
   result** to check the exact program, DUT, operator, scope and saved Run ID.
9. Inspect the report and export destination. Import the exported `run.json` into
   authoring's recording review to inspect the frozen inputs and try proposed
   criteria. For iteration, stop at an idle station, edit and recompile in
   authoring, prepare and transfer the updated complete folder, then reload the
   program. The previous revision's profiles are incompatible; explicitly capture
   and select a new profile before production. Run again and verify the previous
   saved run is still intact. An already loaded plan does not live-update from
   authoring. Retiring a profile prevents future selection and preserves its use
   in historical results.

## Validation boundaries

Both applications and their test projects built in Release with zero warnings
or errors on Linux. Focused checks on the combined tree passed:

- 19 authoring headless UI cases: quick creation, guided onboarding and first-save
  guidance/recovery.
- 15 operator real-render cases: session entry, execution, selected execution,
  Results/report routes and opening the exact completed run.

Local TRX records: `/workspace/combined-review-results/combined-authoring-ui.trx`
and `/workspace/combined-review-results/combined-operator-execution.trx`.
These are focused checks within each application; the manual route above covers
transferring a newly authored plan between them.

These counts describe the initial combined baseline. The expanded golden-reference
contract, fresh verification and independent voting rounds are documented in
[cross-app-persona-workflows.md](cross-app-persona-workflows.md) and
[cross-app-persona-review.md](cross-app-persona-review.md).

The separate authoring and operator review reports retain their original scoring
scope. Their scores do not constitute a new scored review of this combined
cross-application journey. Physical instruments and deployment onto a live
station are outside this local mock review.
