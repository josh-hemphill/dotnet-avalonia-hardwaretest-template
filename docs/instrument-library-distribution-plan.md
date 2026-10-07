# Published instrument library distribution

The tools ship the pinned Instrument Components device library and optional standalone VISA counterpart, prepare isolated authoring homes without engineers building or reinstalling plugins, and preserve broker ownership in HardwareTest execution. Authoring, validation, and mock workflows do not open a vendor resource manager. Vendor VISA runtimes remain machine prerequisites for physical execution.

Stack: authoring-workspace-view-design (PR212) <- instrument-library-distribution (PR213) <- authoring-progress-file-sharing (PR214, supporting reliability fix) <- instrument-library-bootstrap <- instrument-library-standalone-visa. No merges are part of this work.

## Area 1: Published artifact distribution (1 / 3)

- Depends on: PR212.
- Goal: centrally pin all four NuGet identities at 0.1.1, restore locked dependencies, and ship the genuine GitHub release TAP archive with a fixed SHA256, identity, and origin. Avoid loading NuGet copies of the base plugin whose TAP packaging changes its bytes.
- Files: Directory.Packages.props; Host project/build targets; published asset contract; downstream lock files; packaging tests/docs.
- Surface: PublishedInstrumentComponents exposes immutable release identity/version/origin/SHA256 and an owned archive stream/materialization API. Artifact provisioning occurs at build time; deployed tools require no download for this supported release.
- Pseudocode:
  - resolve pinned release archive from supplied build path or per-project artifact cache; download only when absent;
  - hash archive; reject mismatches before extraction/embedding;
  - embed the exact archive in Host and propagate PublishedArtifacts archive content to build/publish consumers; do not place loose library plugins on metadata-only discovery paths;
  - restore NuGet payloads with ExcludeAssets=all where used only as package inputs; never preload a different NuGet base DLL;
  - expose archive bytes whose hash matches the descriptor; no runtime network or hardware I/O.
- Tests: build and locked restore; exact archive/payload identities and hash; transitive publish contains release bytes; tampered supplied archive rejects build; authoring metadata isolation remains intact.
- Out of scope: home installation policy; standalone registration and bridge packaging.
- Risks: NuGet and TAP DLL bytes differ; Windows/Linux build paths; cache tampering; transitive MSBuild payload propagation.
- Conflicts: Area 3 changes Host packaging; sequential work only.

## Area 2: Bundled home preparation (2 / 3)

- Depends on: Area 1 and the supporting progress-file-sharing fix (PR214).
- Goal: declared device-library requirements prepare from the bundled release after existing explicit overrides, including offline operation. Compatible selected homes are reused. Unsupported requirements fail truthfully and do not change selected homes.
- Files: OpenTapHomeBootstrapper; published artifact installation/provenance; actual-library test fixtures; environment guidance and CI; narrowly scoped library metadata registration if required by the real release.
- Surface: existing BootstrapOptions and overrides retain precedence. Bundled version is accepted only through existing manifest version validation and payload inspection.
- Pseudocode:
  - if not declared: return; if selected library package and payload satisfy manifest: return;
  - choose options path > manifest path > captured environment path;
  - explicit path exists: use existing owned importer (invalid explicit paths never silently fall back);
  - otherwise materialize immutable embedded TAP bytes into owned temporary file, import with manifest validation, clean up;
  - record known release origin/hash only for the bundled artifact; selected-home publication remains atomic;
  - register metadata for exact validated library payloads without granting the whole home root; prove published generic identity/cleanup step construction and stale-origin/cold import behavior;
  - show bundled preparation guidance; tests default actual-library fixtures to published archive, retaining explicit fixture overrides for custom-input validation.
- Tests: absent declaration; offline preparation; all eight devices; exact non-DMM lifecycle serialization; compatible reuse; incompatible requirement rollback; explicit invalid override; cold-import recovery; published archive provenance in captured home.
- Out of scope: hardware execution, automatic downloads at runtime, measurement recipe redesign.
- Risks: process-global plugin state, fixture version mixing, stale home hashes, selected-home replacement.
- Conflicts: Area 3 extends bootstrap for the optional standalone provider; sequential work only.

## Area 3: Standalone VISA counterpart (3 / 3)

- Depends on: Areas 1 and 2.
- Goal: package the published InstrumentComponents.Visa and InstrumentComponents.OpenTap.Visa NuGet payloads in a HardwareTest-owned TAP counterpart with explicit registration in independent OpenTAP/TUI processes. HardwareTest Main/Worker execution retains IVisaBroker; metadata-only tools remain free of physical I/O.
- Files: new standalone plugin/project/metadata; Host bundled artifacts; bootstrap provider installation; environment provider readiness/guidance; host and packaging tests; docs.
- Surface: separate named HardwareTest standalone bridge package with declared base/OpenTAP requirements, a synchronous CLI wrapper, and explicit provider ownership. Never claim an upstream bridge release exists. OpenTAP IStartupInfo is asynchronous and is unsuitable for ordering provider registration before instrument Open.
- Pseudocode:
  - construct standalone package from exact NuGet bridge, VISA, IVI dependency bytes plus wrapper DLL/deps/runtimeconfig; depend on genuine base TAP package without competing base DLL copies;
  - synchronous wrapper: PluginManager.Search -> OpenTapVisa.Register -> CliActionExecutor.Execute(args); if Provider == null, register direct provider; never overwrite an existing provider; registration opens no device/resource manager;
  - prepare required library homes with bundled counterpart for external TUI/standalone execution; avoid legacy adapter reinstall prompts;
  - external TUI launcher uses the prepared wrapper, retaining process-tree cancellation, safe argument construction, and terminal lifetime; document the same wrapper for standalone run;
  - managed execution discovers a validated privately staged bundled base payload when needed, then installs the broker provider; authoring/validation do not grant this execution-only discovery path;
  - managed execution search registers broker provider after discovery in each executing process;
  - distinguish device authoring readiness from standalone-provider and vendor-runtime physical prerequisites; no hardware probing during assessment;
  - acquire broker session -> configure/proxy -> on failure close acquired lease -> propagate error.
- Tests: real startup registration in a separate OpenTAP process without VISA runtime; fake provider preserved; broker takes ownership for managed execution; package dependency/payload completeness; metadata-only/mock no vendor calls; broker lease failure cleanup; external launch uses prepared home.
- Out of scope: installing vendor runtimes, hardware measurement, upstream release modification, replacing the broker, changing resource serialization.
- Risks: startup hook timing; process-global provider state; runtime dependency layout; native runtime absence; packaging duplicate base DLLs.
- Conflicts: Host project, bootstrap, lock files shared with previous areas. Finish each parent's review loop before starting the next area.

Each area is published as a ready stacked PR, then reviewed by a fresh reduced-context subagent. Must/Should findings are fixed and the whole area is re-reviewed. Remaining valuable nits are retained for final human review.

### Building the pinned release

Host builds fetch the supported GitHub release into `src/HardwareTest.OpenTap.Host/obj/published-instrument-components/0.1.1` when absent and verify SHA256 on every build. For offline builds, pass `-p:InstrumentComponentsReleaseArchive=/absolute/path/InstrumentComponents.OpenTap.0.1.1.TapPackage`. A missing explicit path or mismatched hash fails the build. The archive is embedded in Host and copied transitively to `PublishedArtifacts` in build/publish outputs. Loose library plugins are deliberately absent from those outputs to preserve authoring metadata isolation; declared library preparation installs these embedded bytes into an owned home clone and atomically publishes it. No runtime download is required. NuGet packages are restored as locked payload inputs with all loading/build assets excluded.

### Preparing a bundled library home

Library homes use one installed layout: both managed Instrument Components DLLs at the home root and package metadata at `Packages/InstrumentComponents.OpenTap/package.xml`. Explicit archives and unpacked input folders are validated and normalized into that layout. Preparation rejects metadata-only packages, missing or invalid managed assemblies, incorrect identities, malformed or mismatching declared SHA1 hashes, and installed package-directory DLL duplicates before publishing. Unsupported installed layouts require a fresh home; they are not migrated.

Declare `InstrumentComponents.OpenTap` with a compatible version requirement and select **Prepare**. A compatible installed package and validated payload are reused; otherwise preparation uses options, manifest, then captured environment overrides, or the embedded 0.1.1 archive when no override exists. Preparation requires no runtime network access or upstream build. Invalid explicit overrides and incompatible version requirements preserve the selected home. Bundled imports record release origin and archive SHA256 in `Packages/InstrumentComponents.OpenTap/hardwaretest-provenance.json`, which is captured with home bytes; custom replacement overwrites a prior attestation with a custom-source marker that contains no bundled origin, version or archive hash. Metadata discovery loads only the validated library and contract DLLs from a process-owned staging directory retained until exit, so deleting or replacing a selected home cannot invalidate lazy OpenTAP metadata.
