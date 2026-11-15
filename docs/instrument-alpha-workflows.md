# Instrument Components alpha workflows

HardwareTest evaluates **Instrument Components 0.3.0-alpha.1**, a breaking software preview. The root facade has no device implementations. HardwareTest explicitly composes the released Keysight, Keithley, Rigol, Siglent and Tektronix packages; passive catalog construction and validation do not open transport or send identification commands. Physical bench qualification remains pending upstream.

## Prepare and author a mixed bench

1. Create a Product hardware scaffold or open your workspace. In **Environment**, declare Instrument Components, **Save all**, then **Prepare authoring environment**. The tools install the bundled, SHA256-verified TAP release and the HardwareTest execution counterpart offline. Compatible selected homes are reused. Import/download errors preserve the selected home.
2. In **Hardware → Add instrument**, choose **Registered vendor device**, search/select the exact vendor and model, name the logical slot and enter its resource address. Repeat for other boxes. The catalog contains 56 model choices from the five independently published vendor packages, including the commissioned legacy subsets.
3. Use **one slot per physical box**. A Keithley 2602B's DMM and PSU operations and a 34980A's switch/digital module operations share one resource, one connection and one serialized handle.
4. **Load selected binding** to edit address, timeout, confirmed channel/output inventory or commissioning. Review and apply the change. The review preserves the logical slot and checks existing operation/cleanup bindings. Undo/redo and save/reopen preserve model, address, settings and step identities. Reusable hardware templates keep these same settings.
5. Select the instrument slot in the sequence, then **Add step** and choose an advertised **Device operation**. Unsupported operations explain their prerequisite and cannot be inserted/compiled as executable bindings. Enter named arguments such as `range=10` or `channel=1; volts=0`. Optional arguments may be omitted; duplicate/unknown names and nonfinite numbers are rejected. Result channels, physical units and optional inclusive limits travel through normal operator presentation and Results.
6. Save the source, check/compile, then use the normal **Build** and **Pack** receipt workflow. Missing package, invalid model/inventory or incompatible operations block execution before broker allocation. A prepared environment is required for a deployment build.

## Inventory and commissioning

Empty channel/output fields retain the published profile inventory. Unknown outputs do not grant shutdown coverage. After checking every physical output, enter `fgen` or a complete list such as `psu:1,psu:2`, with the corresponding confirmed channels such as `1,2`. `none` asserts an outputless device; it is rejected when it contradicts the model's PSU/generator capabilities. Shutdown disables every confirmed applicable output. Close releases a connection; it does not claim output shutdown.

Specialized factories require host assertions and, where applicable, frozen module/firmware/probe inventory. These inputs do not perform discovery and do not establish physical qualification:

| Models | Required configuration |
| --- | --- |
| Agilent/HP/Keysight 34401A, 53132A | `GpibScpiConfirmed` |
| Agilent/HP 33120A | `ContinuousSine50OhmVppOutputChangesAuthorized` |
| HP 5334A/B | `NativeInternalGatingConfirmed` — native language, internal gating and exclusive REMOTE |
| HP 3585B | `QualifiedNativeScalarConfirmed` |
| Tektronix TDS1002 | `QualifiedGpibNormalModeConfirmed;Tds2Cm;X10;X10`, replacing the communication module and both probe factors with the actual configuration. Accepted modules: Tds2Cm/Tds2Cma/Tds2Cmax; probe factors: X1/X10/X100/X1000. |
| HP 3497A | JSON with confirmed Option001, normal DVM routing and each channel's slot, local input, module and compensation. |
| Agilent/Keysight 34980A | JSON confirming no autonomous actions and the exact switch/module and input/output digital port inventory. |
| HP 8920A/B, 8921A | JSON containing exact model, firmware, attenuator, input-power option, source port, receiver input and commissioning. Confirm native GPIB, initial Manual RX/TX, independent frequency display, inhibited external PTT, exclusive access and qualified carrier. |

Hardware shows copyable JSON skeletons for complex configurations. Their `Unknown`/false values deliberately require completion; they cannot authorize execution. The released factory validates firmware ranges, module/channel mappings, probe factors and capability subsets. Only configured operations are available; for example, an input-only digital port does not grant digital output. Specialized model subsets may lack identity, reset or shutdown operations. Do not add lifecycle steps that their configured descriptor does not advertise.

## Operator and service workflow

**Instruments** shows the saved vendor/model, capabilities, package version, logical slot, effective resource and readiness. Discover addresses when needed, apply/save a station override, and explicitly query identity when appropriate. Discovery is optional for already confirmed addresses. Identification is an active diagnostic, not a catalog side effect. Configuration/commissioning errors route back to Authoring; station resource overrides preserve the model and its configuration.

Recorded identity belongs to the address that was queried. Editing a binding clears its current identity; refresh/reopen only restores an identity whose recorded address matches the effective address. The commissioning strip recommends identity queries only for configurations that advertise support. Specialized native-language subsets instead show model-appropriate manual verification guidance, and the generic diagnostic refuses a query to a known unsupported saved configuration before opening transport.

Confirm the DUT/session and run normally. Each result belongs to its producer step, including asynchronous result delivery. Scalar values retain their published physical units and limits; trace samples retain their unit and execution scope. The production worker uses the process-owned HardwareTest VISA broker and can run the saved plan again after completion. Device errors retain their message and require appropriate repair/recovery; an unsupported operation is rejected before allocating a connection.

Pause lets an in-flight device operation finish and holds the next operation until Resume. It does not interrupt a command or change instrument outputs. Stop cancels the run and releases a paused gate so owned resources can close; use explicit supported shutdown operations where output changes are required. Refresh plan slots retains the commissioning program and selected logical slot even when the Run session uses another program.

Step rows and Results distinguish a measured **Fail**, an execution/protocol **Error**, and **Cancelled** work. A cancelled waiting operation has no invented measurement failure. Needs attention includes all three outcomes; failure counts include measurement failures, while errors and cancellations retain separate terminal counts and attempt labels.

An SDK **Overload** reply is an invalid reading, even when its numeric carrier is zero. The adapter publishes no ordinary sample for it: charts, statistics and sample exports exclude the placeholder. The failing step instead retains an explicit outcome detail with channel, unit, driver, resource, operation and recovery guidance. That detail persists into Results, attempt history and the standard status/certification reports. A genuine valid 0 V remains an ordinary measurement. The simulated `MOCK::AGILENT-OVERLOAD` and `MOCK::AGILENT-ZERO` resources exercise those two qualified 34401A paths without physical hardware.

Standalone/TUI execution uses `dotnet HardwareTest.OpenTap.StandaloneVisa.dll ...` in the prepared home. Its synchronous registration runs before CLI dispatch. HardwareTest Main/Worker execution uses its broker connector. Physical execution requires an appropriate separately installed vendor VISA runtime.

## Existing 0.1.1 workspaces and homes

The 0.1 API and the alpha graph are incompatible. Preparation rejects an installed old library or an incompatible manifest requirement transactionally; it preserves selected bytes and source bindings. Keep the previous application/home for unchanged 0.1.1 plans. For an explicit alpha migration, preserve a copy of the workspace, select a fresh authoring home, change the library requirement to exact `0.3.0-alpha.1`, prepare, then deliberately select each vendor/model and re-author/check its supported operation bindings. Imported unsupported source remains available for preservation/export; the application does not silently infer a model, drop its settings or replace physical resources with demo hardware.

## Package and validation provenance

The main TAP archive is `InstrumentComponents.OpenTap.0.3.0-alpha.1.TapPackage`, SHA256 `a7891374e1e613eff83dcb6dafd93a3e4b4584f4f2c4b5a942b694188148b9c4`, from the [versioned release](https://github.com/josh-hemphill/instrument-components/releases/tag/v0.3.0-alpha.1). It declares facade/OpenTAP plus Abstractions and Runtime assemblies. Managed assembly versions are `0.3.0.0`; package versions retain the prerelease suffix. Application SDK/SCPI/vendor dependencies are exact NuGet pins with lock files. The genuine TAP plugin bytes remain isolated from NuGet copies of the facade/OpenTAP plugin. The owned standalone archive carries the wrapper, registered-device plugin, SCPI, VISA and five vendor payloads, with deterministic ZIP entries; the selected home supplies the genuine base and OpenTAP runtime.

Representative tests use strict in-memory transcripts for all five vendors, shared multifunction access, commissioned 34401A behavior, broker execution and the production worker. Avalonia persona checks render production views and drive actual input at authoring 960×600/1280×800 and operator 900×600/1280×800. Software simulation results must be reported separately from physical instrument qualification. Council review records retain the rubric, source manifests, individual ballots, evidence, findings and iteration status; a passing build or selected smoke tests alone do not establish the requested 9.5/10 result.
