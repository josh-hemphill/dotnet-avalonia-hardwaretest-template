/** Parse `dotnet list package --vulnerable` output. Exit code is not always non-zero. */

export function hasVulnerablePackages(text: string): boolean {
  return /has the following vulnerable packages/i.test(text);
}

export function formatAuditFailure(text: string): string {
  return `Vulnerable NuGet packages detected:\n${text.trim()}`;
}
