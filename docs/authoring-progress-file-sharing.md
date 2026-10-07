# Supporting reliability slice: Windows progress transport

This PR sits between published artifact distribution and bundled home preparation. It repairs the child progress transport and its Windows test fixture lifecycle; package installation, VISA providers and UI redesign are out of scope.

The child writes a complete temporary JSON record and atomically replaces progress.json. The parent currently opens progress.json without delete sharing. On Windows, an overlapping parent read denies replacement and can fail an otherwise successful bootstrap/build, including a misleading PACK_PREREQUISITE error when a progress callback runs inside preflight.

Contract and pseudocode:

- Open the progress record with FileAccess.Read and FileShare.Read | FileShare.Delete.
- Publish the first closed temporary record with File.Move; atomically replace subsequent records with File.Replace. Windows CI proved File.Move overwrite fails even with delete sharing. The shared production publisher is exercised directly by the held-handle regression.
- Hold that single handle for length validation and bounded asynchronous read; reject records over 4096 bytes before allocating their payload.
- Deserialize that coherent record using the existing generated JSON context. Retain the poll cadence, stage deduplication, cancellation and transient IOException behavior.
- Tests: hold the production read handle open while atomically replacing the record; old handle sees the complete old record and a new read sees the complete new record. Verify the production bounded-read method rejects oversized input and honors cancellation.
- Run affected operation tests, formatter and fresh whole-area subagent review after publication. Hosted Windows checks prove the OS sharing behavior; Linux also verifies coherent replacement.
- Run the Windows sharing regression in a named CI step immediately after Build, before the broad suites, so native replacement proof is visible without waiting for the complete authoring suite.
- Stop operations and await recovery for every HardwareTests view model before deleting its fixture tree. A Windows CI teardown failed while a background checkpoint was removing its temporary staging directory; await ownership before cleanup using the established Core-fixture pattern.

The [Windows ReplaceFile contract](https://github.com/MicrosoftDocs/sdk-api/blob/docs/sdk-api-src/content/winbase/nf-winbase-replacefilew.md) opens the destination for read/delete with shared read/write/delete, compatible with the parent's read/delete-sharing handle. The [.NET Windows implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Windows.cs) maps File.Replace to that API, while File.Move overwrite uses MoveFileEx.

Stack: PR213 distribution -> progress-file-sharing -> bundled-bootstrap -> standalone-visa. This independent runner/test change can be prepared while bootstrap tests run; bootstrap branches inherit the fix before publication. Shared files: none with bootstrap implementation except this dependency graph.
