import { assertEquals } from "@std/assert";
import { formatAuditFailure, hasVulnerablePackages } from "./lib/audit.ts";

Deno.test("audit treats a clean listing as no vulnerabilities", () => {
  const text = `The following sources were used:
   https://api.nuget.org/v3/index.json

The given project \`HardwareTest.Core\` has no vulnerable packages given the current sources.
`;
  assertEquals(hasVulnerablePackages(text), false);
});

Deno.test("audit fails when a project reports vulnerable packages", () => {
  const text = `Project \`HardwareTest.Core\` has the following vulnerable packages
   [net10.0]:
   Transitive Package      Resolved   Severity   Advisory URL
   > Newtonsoft.Json       12.0.1     High       https://github.com/advisories/GHSA-5crp-9r3c-p9vr
`;
  assertEquals(hasVulnerablePackages(text), true);
  assertEquals(
    formatAuditFailure(text).startsWith("Vulnerable NuGet packages detected:"),
    true,
  );
});
