# Advanced fixture procedure

This optional OpenTAP example tests a simulated signal board. It demonstrates how a procedure engineer can organize a long test, share fixture prerequisites, maintain switch mappings and pass criteria alongside the plan, and publish both evaluated rows and multichannel capture evidence.

## Run the example

Use a matching desktop app, Worker, and example plugin built from this checkout. The example stays outside the default program catalog until it is staged.

From the repository root on Linux x64:

```sh
dotnet build src/HardwareTest -c Release -r linux-x64
dotnet build examples/AdvancedProcedure/Runner -c Release -r linux-x64 -p:RestoreLockedMode=true

app=src/HardwareTest/bin/Release/net10.0/linux-x64
data="$PWD/examples/AdvancedProcedure/AppData"
example=examples/AdvancedProcedure/Program

dotnet run --no-build -c Release -r linux-x64 --project examples/AdvancedProcedure/Runner -- stage "$example" "$app" "$data"
dotnet run --no-build -c Release -r linux-x64 --project examples/AdvancedProcedure/Runner -- inspect "$app/Programs/advanced-procedure.TapPlan"
dotnet run --no-build -c Release -r linux-x64 --project examples/AdvancedProcedure/Runner -- verify "$app" "$data"
dotnet run --project src/HardwareTest.PlanValidate -c Release -- --strict --opentap-plugin-dirs "$data/plugins/AdvancedProcedure" "$app/Programs/advanced-procedure.TapPlan"

HARDWARETEST_DATA_DIRECTORY="$data" \
HARDWARETEST_OPEN_TAP_PLUGIN_DIRECTORIES="$data/plugins/AdvancedProcedure" \
HARDWARETEST_USE_MOCK_VISA=true \
HARDWARETEST_EXPORT_OPENTAP_RESULTS=true \
"$app/HardwareTest"
```

Use the matching RID and executable suffix on Windows and macOS. The serial plugin needs the managed `System.IO.Ports.dll` and the corresponding native helper on Unix. The staging command copies these together with the example DLL. It installs the plan and sidecar in the app's `Programs` directory and places the two tables under `Programs/tables/advanced-procedure/`. Keep the plan-relative layout if distributing the example manually. The desktop and Worker must see the same trusted plugin directory. Restart the app after staging.

In **Run**, select **Advanced Fixture Procedure (Simulation)** and confirm a DUT serial and operator. Continue through the simulated harness and DUT handoffs. At the configuration prompt, select `Crossed`, `Terminated`, and `Loopback` in any order. Only outstanding configurations appear in the list. The prompt title shows how many configurations are complete. Use the full procedure when running this plan. A narrower **Run Selected** request stops before measurements because its required sections are outside the selection.

## Procedure layout

| Section | Suites and evidence |
| --- | --- |
| Station bring-up and self-test | Logic, data, then analog supply. Each simulated route checks leakage to ground and the enabled rail against its setpoint. A supplies-off harness handoff precedes power and data continuity, shield isolation, consecutive resistance samples, and worst adjacent-sample change. |
| DUT connection and basic checks | A second supplies-off handoff precedes the DUT identity, ping and status commands, clock waveform, and high and low levels. |
| Differential and loaded analog | A tracking loop detects settling before paired captures. The unloaded and loaded ladders yield gain, load strength, spectral residual in dBc, reference and previous-step differences, full span difference, and curve scores against two references. The load suite compares clamp and reclamp timing, peak current, pulse width, and external current with DUT sensor readback. |
| AC equalization and attenuation | A shared capture compares reciprocal paths at two impedances. Per-route station corrections yield attenuation, equalization, reference error, adjacent-step error, and span error. |
| Counters and embedded digital | Trigger increments and accumulated span are compared with DUT counter readback. Two function-generator signals and a digitizer trace feed a pulse-width decoder that checks payload, timing, frequency, amplitude, and edge shape. |
| Operator-selected configurations and cleanup | Remaining configurations execute once in the chosen order. Separate start and completion events identify an interrupted configuration. Fixture reset runs on completion, a failed prerequisite, or cooperative cancellation. |

The station and DUT prerequisite sections execute once per full run. A failure in either section gates all subsequent sections. Later measurement failures still allow independent sections to collect evidence. The root owns the simulated fixture and serial resource. Individual measurement classes consume a case row, capture data, evaluate named criteria, and publish verdicts.

## Change a route, criterion, or measurement

- [Station table](Program/tables/advanced-procedure/station.json) holds logical switch routes, nominal simulator observations, and saved AC calibration by path and impedance.
- [Test table](Program/tables/advanced-procedure/tests.json) maps case IDs to routes, stimulus values, reference traces, and bounded criteria. Each measurement class documents the expected criterion names and units. Limits are inclusive.
- [Procedure source](Runner/ProcedurePlan.cs) defines the setup dependencies and suite tree. [Plugin source](Plugin) contains the isolated measurements. To regenerate the checked-in TapPlan after changing its structure, run the example runner with `generate examples/AdvancedProcedure/Program`.
- Run `inspect <TapPlan>` before staging edited data. It validates the joins and prints effective routing, nominal values, stimuli, reference points, calibrated corrections, and criteria. Each run loads a fresh table snapshot. Commit revisions with changes. Run evidence includes the revision labels, file hashes, and route IDs used by each case.
- Custom measurement code and fixture orchestration can be maintained in source or loaded into an OpenTAP Editor with the plugin available. HardwareTest Authoring represents the custom procedure as a raw step. The example's staging command distributes the JSON files along with it.

## Serial DUT starter

[SerialDutPort.cs](Plugin/SerialDutPort.cs) is a small standalone OpenTAP instrument that can be copied into a product plug-in. The writable `Address` exposes the port as a bindable station slot. The resource sends one printable ASCII line and reads LF or CRLF replies with a bounded deadline and response length. It opens the physical port on the first query, so a product plan can establish the connection at an operator handoff. A failed physical exchange invalidates that resource session. Close and reopen it before another command.

The example fixture accepts only `MOCK::SERIAL`. In a product plan using this resource, set the COM or tty path, baud rate, timeout, and `EnablePhysicalPort` deliberately. Replace the demonstration command vocabulary and parse replies for the actual DUT protocol. For interfaces other than serial, keep the test steps and evidence contract and implement the relevant transport in your product plugin.

## Evidence and reports

Both built-in PDFs include the evaluated metric, value, unit, lower and upper limits, verdict, and producer timestamp in UTC. PDFs display ordinary numbers to eight significant digits and use compact scientific notation for large or small readings. `run.json` retains full numeric precision and every sample index and timestamp. The simulation supplies a capture clock for array points. A hardware adapter can publish its acquisition clock instead.

The plugin publishes `Capture` rows with `Channel, Index, Value, Unit, ElapsedMs, TimestampUtc` and `Evaluated` rows with `Name, Value, Unit, LimitLow, LimitHigh, Verdict, TimestampUtc`. Use stable names per result row and round-trip ISO 8601 timestamps. An `OperatorGuidance` event can provide a recovery message in the Run banner when a prerequisite blocks execution. A shared OpenTAP step-run ID ties a case's capture, evaluated rows, and routing events together. With OpenTAP exports enabled, the run folder also includes `Capture.csv`, `Evaluated.csv`, and `Event.csv`. The desktop Results page uses the distinct metric keys to show both bounds and the measured score.

See the [procedure Council review](council-review.md) and [UI and workflow review](council-ux-review.md) for the staged checks and Authoring handoff.

## Maintain the procedure in Authoring

Prepare a fresh workspace from the example after building the Runner. The command copies the plan, sidecar, and both data files. It creates a plugin package for the current platform and declares it in the workspace.

```sh
dotnet run --no-build -c Release -r linux-x64 --project examples/AdvancedProcedure/Runner -- \
  authoring-workspace examples/AdvancedProcedure/Program /path/to/fixture-workspace
```

1. Open the new workspace in HardwareTest Authoring. In **Environment**, import `HardwareTest.Advanced.Fixture.0.1.0.TapPackage` from the workspace and prepare the selected OpenTAP home. Reopen the workspace if it was already loaded before the plugin was installed.
2. Select **Advanced Fixture Procedure**, open **Sequence**, and choose **Edit procedure**. Search for `rail-logic`, then open its related route or evaluated row. The same links lead back from each data row to the cases that use it. **Data tables** includes case inputs, reference traces, station routes, and calibration corrections.
3. Use **Review changes** to see edited fields and differences on disk. **Apply to draft** validates the procedure and tables together using the fixture plugin. **Save all** publishes the validated draft. Undo in the active Authoring session restores the previous procedure and data, which can be published with Save All.
4. Use **Build & package** to export the procedure and its plugin package. The build includes the declared table files and records the inputs in `authoring-build-receipt.json`.

For an Authoring workspace with a compatible plugin, the editor also exposes section prerequisites, suite names and notes, test case bindings, station serial profiles and mock serial settings, and operator handoffs. Select **Run Remaining Configurations** to edit the prompt and preview the choices. The preview uses the same prompts the plugin sends to Operator during a run. To check acquisition changes, run the saved package in Operator with simulation enabled. Select a matching recording in Authoring to compare proposed bounds against previously evaluated values.

Workspace owners declare portable inputs in `programAssets` in `authoring.json`, keyed by plan ID. Paths are relative to `plansDirectory`. A custom plugin can provide the version 1 `IProcedureAuthoring` profile from HardwareTest Basic to define editable fields, data tables, and preview requests. Its validator is called for Authoring edits, strictly checked packages, and the standalone inspection workflow. The source tree and an inspectable copy remain available when the plugin is unavailable. [Delivery plan](../../docs/authoring-procedure-maintenance-plan.md)

A test’s result display name can change independently of its metric ID. In Operator **Results**, expand the evaluated row’s attempt to copy its exact UTC timestamp, execution ID, capture channels, and procedure and data provenance. Status and certification reports use the same identities.
