# Offline bench delivery workflows

This extends version 2 of [the cross-app persona contract](cross-app-persona-workflows.md).
Existing identity, safety, golden-reference and evidence requirements still apply.
The added task belongs primarily to P5 (technician), P6 (author) and P7 (release
engineer); P1/P2 must see an uncommissioned new station rather than inherited
approval, and P4 must retain historical evidence in the original installation.

## Supported delivery boundary

Supported targets are `win-x64`, `linux-x64` and `linux-arm64`. Authoring resolves
for the selected target, not its own host. Installed packages can be repacked only
for the current host RID, from captured identity and actual declared payload.
Other targets require original compatible archives or target-aware downloads.

The bench must already have the matching operator build with the
`offline-bench-v1` installer capability. This is a test/package delivery feature,
not an operating-system, vendor-driver or standalone operator installer. A cold
bench first needs that matching published operator release and its prerequisites.
Required production dependency roots come from both the saved workspace manifest
and the checked compiled plan's `Package.Dependencies` metadata;
unrelated installed/editor/TUI/optional packages are excluded. Optional packages
become required when selected package dependencies require them.

The bundle records exact versions, target, dependency closure, archive hashes,
checked program/settings hashes and required operator build. Runtime-provided
OpenTAP/Basic/Mixins/adapter components are distinguished from installable plugin
payload: matching existing runtime bytes are verified rather than silently
upgraded. Cross-target identity uses the selected release/build and target;
authoring-host binary hashes are not used as the remote baseline.

The complete operator release includes its worker, engine and package SDK. SDK
resolution requires `OpenTap.Package.dll` to have the same assembly version as the
loaded OpenTAP engine. Protected engine/built-in payload is compared with exact
operator release bytes; a package display version or filename does not establish
that match. Missing/mismatched SDK or protected payload requires restoring the
complete matching release. Bundles do not upgrade or downgrade this baseline.

## W7a Prepare and transfer — P6/P7

Open **Prepare bench files** for a saved, compiled program and choose **Include
packages for an offline bench**. Select the bench platform and destination.
Compatible local archives and the prepared authoring home can supply packages.
An optional local package folder provides manual fallback. Downloading missing
packages is explicit and uses the configured HTTPS repository; credentials and
repository query tokens are never shipped.

Resolution intersects every required and transitive constraint, pins one exact
version per package and rejects ambiguity/conflict with the relevant requirement.
Missing payload, incompatible target, unsupported installation actions or package
prerequisites must give an actionable correction. Required package actions are
not silently ignored. The exact built-in Linux OpenTAP `chmod +x tap` declaration
is implemented as executable file permissions, without running `chmod`. The known
Windows OpenTAP upgrader declaration applies to in-place engine replacement; the
separate same-version clone verifies the exact engine baseline and copies files
without invoking that upgrader. These narrowly recognized engine declarations do
not authorize arbitrary actions, flags or versions. The Windows tuple is exactly
`ExeFile="Packages/OpenTAP/Tap.Upgrader.exe"`, `Arguments=""`,
`ExpectedExitCodes="0"`, `Quiet="false"`, `Optional="false"`,
`ActionName="install"`, in the original package namespace and one-step container.
Archives are bounded and checked for payload completeness,
links, traversal, absolute paths and case collisions.

Capture the selected revision, target and source bytes before preparation;
recheck before publishing. Produce the checked program folder, all required
archives, lock/transport manifest, instructions and check/install helpers together.
Failure or cancellation before commit preserves the previous bundle and entered
fields. An unchanged owned bundle may be replaced; manually changed or unrelated
files are preserved. Checked package inclusion never degrades to program-only.
Cancellation after publication reports completion truthfully. Copy the complete
folder to removable media, preserving its contents.

## W7b Check and prepare a separate installation — P5/P7

On the disconnected bench, **Settings → Offline bench bundle** accepts the bundle
folder, checks it and exposes exact target, packages and program details. Check is
read-only, does not load bundled extension code or execute package actions, consult feeds,
create logs/settings or claim installation/physical readiness. The corresponding
CLI dispatches before application settings/logging/UI bootstrap.
It may resolve the trusted operator-release package SDK for semantic-version
validation; this is not extension discovery or installation.

Helpers first verify the operator capability receipt and its bound entrypoint/host
hashes, then the matching build and target. An absent, altered or older operator
is refused before invoking unsupported flags; it must not accidentally open its
GUI. OS helper prerequisites or policy failures give a manual route through the
matching current operator's Settings instructions, without bypassing policy.
Hashes establish content integrity, not publisher authenticity: obtain packages
and the operator from trusted sources.

Installation requires a **nonexistent application destination and nonexistent
data directory**, separate from each other, the bundle and the source runtime.
It clones runtime/release files from the compatible installed operator, excluding
settings, credentials, runs, profiles, caches, logs, old program folders and external
plugin paths. It applies only validated compatible package payload and the checked
program into owned staging. Conflicting/protected files and unsupported actions
fail explicitly. Both the source runtime and its existing data remain unchanged.
Failure/cancellation cleans only owned staging and newly reserved data; publication
never overwrites an existing destination. No app is launched automatically.

## W7c Activate and commission — P5/P1/P4

Stop the old operator before using hardware from the new copy. Launch the new
installation with the explicit fresh data path and reviewed station launch options.
A deployment marker binds that installation to its newly reserved data directory;
startup without a data argument uses that safe bound path, and different/external
data or settings paths are refused before loading them. Later runs can use the
same bound directory normally. Installation is not station commissioning: confirm
instrument settings and identity, run health checks and acquire new golden
references as required before production.

**Reusing an older bench data directory is unsupported in this workflow.** Golden
reference context includes the loaded engine assembly version and module identity,
so an engine build change requires fresh acquisition and qualification. It does
not establish a complete dependency-payload migration identity; allowing data
reuse would still require a separate migration/requalification design. Historical data
and the original application remain available unchanged. Do not copy old settings,
credentials, qualification or golden profiles into the new bound data directory.

The general existing-data `StationHealthGate` remains a follow-up: it evaluates
profile, verdict and age without engine/effective-configuration binding. This
workflow avoids inherited approval by requiring fresh data; it does not change
that general gate. Existing-data station procedures must explicitly invalidate
health approval and rerun health after engine or bench configuration changes.

## Required evidence and reviews

Three independent definition reviewers must accept all D1–D7 scores above 90.
After implementation, three independent reviewers score the updated persona and
graphical workflows using the existing C1–C7/app UI rubric; every score must exceed
90, with no average masking a failure. Expert ratings are not human success rates.

Evidence must cover an actual authoring → transfer → offline Check/install →
new-installation discovery/load/mock execution → saved result/report journey using
the same test and a unique transitive plugin absent from the base runtime. Network
and source package caches must be unavailable during bench checking/installation
and execution. Record which installed assembly/payload actually executed. Prove
the source runtime and existing data unchanged after success and failure.

Exercise transitive diamonds/conflicts, missing/manual recovery, target mismatch,
archive tampering/paths/actions/limits, future/legacy manifests, old-client refusal,
source mutation, cancellation and destination/publication failures. Test first
launch's safe data binding and rejection of old data/settings. New controls need
Light/Dark wide and true 900×600/font20 renders, keyboard/native names, one scrolling
body and reachable Prepare/Cancel/Close actions. Physical drivers, physical bench
qualification, Windows UIA and spoken output need separate evidence.
