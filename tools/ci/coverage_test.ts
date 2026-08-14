import { assertEquals, assert } from "@std/assert";
import * as path from "@std/path";
import { evaluateCobertura } from "./lib/coverage.ts";
import { TASKS } from "./main.ts";

Deno.test("coverage floors match Python port on pass fixture", async () => {
  const fixture = path.join(
    path.dirname(path.fromFileUrl(import.meta.url)),
    "fixtures",
    "coverage-pass.cobertura.xml",
  );
  const xml = await Deno.readTextFile(fixture);
  const report = evaluateCobertura(xml);

  // Core: 14 lines, 10 covered → 71.4%
  // Engine: 5 lines, 4 covered → 80.0%
  // Hardware: 5 lines (IviVisa skipped), 5 covered → 100.0%
  assertEquals(report.coreLines, 14);
  assertEquals(report.coreCovered, 10);
  assertEquals(report.corePct.toFixed(1), "71.4");
  assertEquals(report.engineLines, 5);
  assertEquals(report.engineCovered, 4);
  assertEquals(report.enginePct.toFixed(1), "80.0");
  assertEquals(report.hardwareLines, 5);
  assertEquals(report.hardwareCovered, 5);
  assertEquals(report.hardwarePct.toFixed(1), "100.0");
  assert(report.ok);
});

Deno.test("coverage fails when Hardware floor is missed", () => {
  const xml = `<?xml version="1.0"?>
<coverage>
  <packages>
    <package name="HardwareTest.Core">
      <classes>
        <class name="HardwareTest.Core.Hardware.Weak" filename="Hardware/Weak.cs">
          <lines>
            <line number="1" hits="1" />
            <line number="2" hits="0" />
            <line number="3" hits="0" />
            <line number="4" hits="0" />
            <line number="5" hits="0" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>`;
  const report = evaluateCobertura(xml);
  assertEquals(report.ok, false);
  assert(report.failures.some((f) => f.includes("Hardware")));
});

Deno.test("TASKS catalog is sorted and complete", () => {
  const expected = [
    "all",
    "audit",
    "build",
    "coverage",
    "list",
    "publish",
    "test:arch",
    "test:e2e",
    "test:host",
    "test:vm",
    "verify",
  ];
  assertEquals([...TASKS], expected);
});

Deno.test("ci.yml references every required Deno task", async () => {
  const root = path.resolve(
    path.dirname(path.fromFileUrl(import.meta.url)),
    "../..",
  );
  const yaml = await Deno.readTextFile(path.join(root, ".github/workflows/ci.yml"));
  const required = [
    "audit",
    "build",
    "test:host",
    "test:vm",
    "test:e2e",
    "test:arch",
    "coverage",
    "publish",
    "verify",
    "list",
  ];
  for (const task of required) {
    assert(
      yaml.includes(`main.ts ${task}`),
      `ci.yml must call 'main.ts ${task}'`,
    );
  }
  assert(yaml.includes("Assert CI task catalog"));
});

Deno.test("ci.yml pins GitHub Actions by commit SHA", async () => {
  const root = path.resolve(
    path.dirname(path.fromFileUrl(import.meta.url)),
    "../..",
  );
  const yaml = await Deno.readTextFile(path.join(root, ".github/workflows/ci.yml"));
  const uses = [...yaml.matchAll(/^\s+uses:\s+(\S+)/gm)].map((m) => m[1]!);
  assert(uses.length > 0, "ci.yml must use GitHub Actions");
  for (const spec of uses) {
    assert(
      /@[0-9a-f]{40}$/i.test(spec),
      `Action must be pinned by 40-char SHA: ${spec}`,
    );
  }
  assert(yaml.includes("permissions:"));
  assert(yaml.includes("timeout-minutes:"));
  assert(yaml.includes("concurrency:"));
});
