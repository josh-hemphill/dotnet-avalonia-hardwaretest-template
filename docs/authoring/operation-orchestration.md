# Authoring operation orchestration

The GUI Bootstrap, Validate and Pack handlers await `AuthoringOperationCoordinator`.
Core remains Avalonia-free: the view model receives an explicit UI dispatcher for
stages, findings, results, errors and final busy state. Opening a replacement
workspace cancels the current generation; queued updates check that generation.
The synchronous CLI and Core APIs retain their previous behavior.

Each operation starts a fresh **authoring** executable in headless mode. It never
starts or reuses the operator Worker. A process-wide lane serializes these
operations. Framework-dependent applications launch through `dotnet`; published
applications launch directly. A duplicate start is rejected while the coordinator
owns an operation, including its cancellation and cleanup.
An authoring host remains alive as the ownership anchor while the operation root
runs or exits. The parent gates startup after native ownership setup. Unix hosts
create a private session and terminate their own process group; Windows parents
retain a kill-on-close Job handle. The operation's exit code travels separately
from host termination. Root exit with descendants holding diagnostic pipes cannot
drop scope ownership, and cleanup never searches recycled descendant PIDs.
Unix owner-pipe EOF and Windows Job handle closure also terminate the process
scope on abrupt owner death. Such an owner cannot run directory cleanup, so its
private staging directory can remain; normal cancel and accepted close remove it.

Version 1 requests and results use source-generated JSON in a uniquely owned
operation directory. Results carry the operation ID, kind and owned root and are
validated before use. Structured stages use a separate atomic progress file;
stdout and stderr remain distinct diagnostics. Logs retain at most 128 chunks of
1024 characters, including output with no newline. The GUI exposes the retained
diagnostics through the busy Operation logs tooltip and the completed status/error tooltip. A child error remains a failure;
package or compatibility failures cannot create a successful placeholder result.

The child captures the selected home's bytes, clones them into its owned tree,
and bootstraps that clone. Pack and Validate do not modify the selected home.
Pack captures saved workspace inputs, the isolated home and the original selected
home baseline, then calls `PrepareOwned`. The parent reconstructs that immutable
request and prepared result, checks receipt identity, cancellation and generation,
and calls the existing transactional `Publish`. The receipt records the original
home baseline as well as the isolated prepared home. Its checks cover the existing
in-process compatibility provider; they do not imply physical instrument I/O or
external operator execution.
Unix file modes are captured alongside byte hashes, included in receipt inputs
and transported with snapshots. Every captured-file materializer restores those
modes, including the initial home clone and its prepared clone. Permission-only
changes invalidate publication; rollback restores the original file and mode.

Bootstrap publishes the prepared home only in the parent, after checking its
original baseline and the current generation. Publication uses rollback-protected
moves. Files unrelated to the prepared home are retained. Cancellation signals an
owned process-tree kill, reaps the child asynchronously and removes only the owned
operation tree. Publication observes the same cancellation token and rolls back
when cancellation interrupts it. A cancellation after the transaction has already
committed cannot retroactively undo that completed transaction.
Accepted window close first honors the existing unsaved-change decision, then
cancels and asynchronously awaits child reaping, publication rollback and owned
cleanup before closing the owner. Cancelling the unsaved-change dialog leaves
the operation running. The UI remains responsive while accepted close drains it.

Recovery checkpoints write durable candidates in private subdirectories outside
the session gate. Cancel, Dispose and session replacement therefore do not wait
for those writes. Only current candidates enter the short gate for final validated
rename; obsolete candidates are removed without replacing the recovery file.
Saved source documents and their session baselines are not changed by recovery.

The process fixture delegates real preparation to the production child protocol,
with release files providing deterministic slow-work and pre-publication barriers.
Tests cover responsiveness, duplicate rejection, bounded separate diagnostics,
owned child and descendant termination (including operation-root exit and a
cancelled drain with an unrelated live process), cancellation before publication, stale
workspace results, isolated selected-home catalogs, successful checked builds,
actionable package failures, UI dispatch, accepted owner close and slow
recovery-write replacement, executable-home helper preservation and permission-only
publication rejection.
The execution checks run on Linux; published Windows/macOS launcher behavior is
implemented but is not exercised by that Linux run.
