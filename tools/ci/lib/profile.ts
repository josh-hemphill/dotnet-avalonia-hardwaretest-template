/** Fast is an allowlist: unfamiliar files, missing diffs and non-PR events run full. */
export type Profile = "fast" | "full";

export function classifyPaths(paths: readonly string[]): Profile {
  if (paths.length === 0) return "full";
  return paths.every((path) => {
      // Shared build inputs take precedence over otherwise fast directories.
      if (
        /\.(csproj|props|targets|sln|slnx)$/.test(path) ||
        /(^|\/)(packages\.lock\.json|global\.json|nuget\.config)$/i.test(path)
      ) {
        return false;
      }
      return /^(docs\/|README\.md$|CHANGELOG\.md$)/.test(path) ||
        /^src\/HardwareTest\/Features\/(Home|Inspect|Instruments|Presentation|ReportPreview|Results|RunTest|Settings)\//
          .test(path) ||
        /^tests\/HardwareTest\.ViewModels\.Tests\//.test(path);
    })
    ? "fast"
    : "full";
}

export function eventProfile(
  event: string,
  paths?: readonly string[],
): Profile {
  return event === "pull_request" && paths ? classifyPaths(paths) : "full";
}

export function aggregatePassed(
  results: Readonly<Record<string, string>>,
  required: readonly string[],
  optional: readonly string[] = [],
): boolean {
  return required.every((job) => results[job] === "success") &&
    optional.every((job) =>
      results[job] === "success" || results[job] === "skipped"
    );
}

export const AUTHORING_FAST_FILTER = "Category!=AuthoringIntegration";
// The native process class is deliberately retained: cleanup and Windows jobs differ
// from Linux session/process groups. Fast Windows still runs the complete Core/host suite.
export const AUTHORING_WINDOWS_FILTER = [
  "FullyQualifiedName~AuthoringWindowsTuiLauncherTests",
  "FullyQualifiedName~AuthoringProgressFileTests",
  "FullyQualifiedName~AuthoringOperationTests",
  "FullyQualifiedName~AuthoringCreationOwnershipTests",
  "FullyQualifiedName~AuthoringPlanInitializationTests.Windows_case_alias_workspace_and_destination_support_direct_and_VM_publication",
  "FullyQualifiedName~PackProtectionTests.Windows_directory_readonly_attribute_does_not_disable_pack_or_leave_probe_files",
].join("|");

export const AUTHORING_UI_WINDOWS_FILTER = [
  "FullyQualifiedName~AuthoringWindowTests",
  "FullyQualifiedName~AuthoringLifecycleTests",
  "FullyQualifiedName~AuthoringOperationWindowTests",
].join("|");
