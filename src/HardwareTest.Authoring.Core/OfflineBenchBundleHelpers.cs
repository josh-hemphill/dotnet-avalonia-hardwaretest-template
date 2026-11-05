namespace HardwareTest.Authoring;

/// Helpers verify release capability and content integrity before passing new switches.
internal static class OfflineBenchBundleHelpers
{
    internal static void Write(string root, OfflineBenchExportOptions options)
    {
        File.WriteAllText(Path.Combine(root, "README.txt"), $"""
            Offline bench bundle for {options.TargetOS}/{options.TargetArchitecture}

            This bundle contains a compiled program and pinned packages. It is not an OS, .NET runtime, operator release, or vendor-driver installer.
            On a cold bench, install the matching HardwareTest operator release and vendor drivers first. Keep this complete bundle folder intact.

            Check: sh check-offline-bundle.sh /path/to/HardwareTest
            Install: sh install-offline-bundle.sh /path/to/HardwareTest /path/to/NEW-app-folder /path/to/NEW-data-folder
            On Windows use the corresponding .cmd helpers with HardwareTest.exe and two fresh destination paths.
            Helpers require the selected release's offline-bench-capability.json, verify the associated operator/Host DLL hashes, and compare its safe --version output with this bundle before passing offline-install flags. Hash verification checks integrity, not publisher trust; obtain the operator release from your trusted release source.
            Shell helpers require sha256sum. Windows helpers use PowerShell -File without changing execution policy. If tools or policy block helpers, use the matching operator's current Settings workflow to check/install this bundle; do not try an older app with unrecognized flags.

            Installation creates a fresh app directory AND a fresh bound station-data directory. It never installs in place, copies old settings, downloads packages, or starts the new app. Existing station data reuse is unsupported for this bundle workflow.
            Activation is deliberate: stop the old operator copy, then launch the new copy with --data-directory <the-new-data-folder>. Preserve other needed launch options. This installed copy must use its bound new data directory; alternate --data-directory or --settings overrides are rejected. Existing apps, runs, references, settings and data remain intact.
            If checking fails, retain this folder and request matching package archives from the authoring engineer. Do not silently fall back to installing only the program.
            """);
        foreach (var install in new[] { false, true })
        {
            var name = install ? "install-offline-bundle" : "check-offline-bundle";
            File.WriteAllText(Path.Combine(root, name + ".sh"), "#!/bin/sh\nset -eu\nmode=" + (install ? "install" : "check") + "\n" + Shell + "\n");
            File.WriteAllText(Path.Combine(root, name + ".cmd"), "@echo off\r\npowershell -NoProfile -File \"%~dp0offline-bench-helper.ps1\" -Mode "
                + (install ? "install" : "check") + " -OperatorPath \"%~1\" -Destination \"%~2\" -DataDirectory \"%~3\"\r\nexit /b %errorlevel%\r\n");
        }
        File.WriteAllText(Path.Combine(root, "offline-bench-helper.ps1"), PowerShell + "\r\n");
    }

    private const string Shell = """
        fail() { echo "$*" >&2; exit 2; }
        if [ "$mode" = install ]; then [ "$#" -eq 3 ] || fail "Usage: sh install-offline-bundle.sh OPERATOR NEWAPP NEWDATA";
        else [ "$#" -eq 1 ] || fail "Usage: sh check-offline-bundle.sh OPERATOR"; fi
        command -v sha256sum >/dev/null 2>&1 || fail "sha256sum is required. Use the matching operator's current Settings workflow instead. No operator was started."
        bundle=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
        operator=$1
        case "$operator" in /*) ;; *) fail "Use an absolute path to the matching published HardwareTest executable.";; esac
        case "$operator" in */HardwareTest|*/HardwareTest.exe) ;; *) fail "Choose the matching release's HardwareTest executable.";; esac
        release=$(dirname -- "$operator")
        receipt="$release/offline-bench-capability.json"
        [ -f "$receipt" ] && [ -f "$release/HardwareTest.dll" ] && [ -f "$release/HardwareTest.OpenTap.Host.dll" ] || fail "This release has no offline-bench capability receipt. Install the matching operator release; no operator was started."
        # Generated capability receipts are deliberately pretty JSON, one field per line.
        # The JSON writer escapes '+' in build identities. Decode only that
        # literal escape; the strict field grammar still rejects other escapes.
        field() { sed 's/\\u002[Bb]/+/g' "$1" | sed -n 's/^[[:space:]]*"'"$2"'"[[:space:]]*:[[:space:]]*"\([A-Za-z0-9.+_-]*\)"[[:space:]]*,\{0,1\}[[:space:]]*$/\1/p'; }
        schema=$(sed -n 's/^[[:space:]]*"schemaVersion"[[:space:]]*:[[:space:]]*\([0-9]*\)[[:space:]]*,\{0,1\}[[:space:]]*$/\1/p' "$receipt")
        [ "$schema" = 1 ] && [ "$(field "$receipt" capability)" = offline-bench-v1 ] || fail "Unsupported capability receipt; no operator was started."
        operatorHash=$(field "$receipt" operatorSha256)
        hostHash=$(field "$receipt" hostSha256)
        [ "${#operatorHash}" -eq 64 ] && [ "${#hostHash}" -eq 64 ] || fail "Invalid capability hashes; no operator was started."
        actualOperator=$(sha256sum "$release/HardwareTest.dll" | cut -d ' ' -f 1 | tr '[:lower:]' '[:upper:]')
        actualHost=$(sha256sum "$release/HardwareTest.OpenTap.Host.dll" | cut -d ' ' -f 1 | tr '[:lower:]' '[:upper:]')
        [ "$actualOperator" = "$(printf '%s' "$operatorHash" | tr '[:lower:]' '[:upper:]')" ] && [ "$actualHost" = "$(printf '%s' "$hostHash" | tr '[:lower:]' '[:upper:]')" ] || fail "Operator release integrity check failed; no operator was started."
        os=$(field "$bundle/offline-bench.json" targetOS)
        arch=$(field "$bundle/offline-bench.json" targetArchitecture)
        case "$os/$arch" in Windows/x64) rid=win-x64;; Linux/x64) rid=linux-x64;; Linux/arm64) rid=linux-arm64;; *) fail "Unsupported bundle target.";; esac
        [ "$(field "$receipt" runtimeIdentifier)" = "$rid" ] || fail "Choose the operator release matching this bundle target."
        required=$(field "$bundle/offline-bench.json" requiredOperatorBuild)
        [ -n "$required" ] || fail "Bundle operator build identity is missing."
        version=$("$operator" --version) || fail "The matching operator version check failed."
        case "$version" in *"$required"*) ;; *) fail "Operator build does not match the bundle. Install the matching published release.";; esac
        "$operator" --check-offline-bundle "$bundle"
        if [ "$mode" = install ]; then "$operator" --install-offline-bundle "$bundle" --destination "$2" --data-directory "$3"; fi
        """;

    private const string PowerShell = """
        param([ValidateSet('check','install')][string]$Mode, [string]$OperatorPath, [string]$Destination, [string]$DataDirectory)
        $ErrorActionPreference = 'Stop'
        try {
          if (-not $OperatorPath -or -not [IO.Path]::IsPathRooted($OperatorPath)) { throw 'Choose an absolute path to the matching published HardwareTest.exe.' }
          if ([IO.Path]::GetFileName($OperatorPath) -ne 'HardwareTest.exe') { throw 'Choose the matching release HardwareTest.exe.' }
          $release = [IO.Path]::GetDirectoryName($OperatorPath)
          $receiptPath = Join-Path $release 'offline-bench-capability.json'
          if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw 'No offline-bench capability receipt. Install the matching operator release; no operator was started.' }
          $receipt = Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json
          if ($receipt.schemaVersion -ne 1 -or $receipt.capability -cne 'offline-bench-v1') { throw 'Unsupported capability receipt; no operator was started.' }
          $operatorHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $release 'HardwareTest.dll')).Hash
          $hostHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $release 'HardwareTest.OpenTap.Host.dll')).Hash
          if ($operatorHash -ine $receipt.operatorSha256 -or $hostHash -ine $receipt.hostSha256) { throw 'Operator release integrity check failed; no operator was started.' }
          $manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'offline-bench.json') | ConvertFrom-Json
          $rid = switch ($manifest.targetOS + '/' + $manifest.targetArchitecture) { 'Windows/x64' { 'win-x64' } 'Linux/x64' { 'linux-x64' } 'Linux/arm64' { 'linux-arm64' } default { throw 'Unsupported bundle target.' } }
          if ($receipt.runtimeIdentifier -cne $rid) { throw 'Choose the operator release matching this bundle target.' }
          if (-not $manifest.requiredOperatorBuild) { throw 'Bundle operator build identity is missing.' }
          $version = (& $OperatorPath --version | Out-String)
          if ($LASTEXITCODE -ne 0 -or -not $version.Contains([string]$manifest.requiredOperatorBuild)) { throw 'Operator build does not match the bundle.' }
          & $OperatorPath --check-offline-bundle $PSScriptRoot
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          if ($Mode -eq 'install') {
            if (-not $Destination -or -not $DataDirectory) { throw 'Installation requires NEW app and NEW data directory arguments.' }
            & $OperatorPath --install-offline-bundle $PSScriptRoot --destination $Destination --data-directory $DataDirectory
            exit $LASTEXITCODE
          }
          exit 0
        } catch { Write-Error ($_.Exception.Message + ' Use the matching operator current Settings workflow if tools or policy block this helper.'); exit 2 }
        """;
}
