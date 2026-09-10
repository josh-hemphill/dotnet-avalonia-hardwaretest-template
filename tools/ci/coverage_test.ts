import { assertEquals, assert } from "@std/assert";
import * as path from "@std/path";
import { evaluateCobertura, findCobertura } from "./lib/coverage.ts";
import { CORE_COVERAGE_FILTER, e2eIsAdvisory, TASKS } from "./main.ts";

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

Deno.test("coverage fails when Core floor is missed", () => {
  const xml = `<?xml version="1.0"?>
<coverage>
  <packages>
    <package name="HardwareTest.Core">
      <classes>
        <class name="HardwareTest.Core.Settings.Weak" filename="Settings/Weak.cs">
          <lines>
            <line number="1" hits="1" />
            <line number="2" hits="0" />
            <line number="3" hits="0" />
            <line number="4" hits="0" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>`;
  const report = evaluateCobertura(xml);
  assertEquals(report.ok, false);
  assert(report.failures.some((f) => f.includes("Core")));
});

Deno.test("coverage fails when Engine floor is missed", () => {
  const xml = `<?xml version="1.0"?>
<coverage>
  <packages>
    <package name="HardwareTest.Core">
      <classes>
        <class name="HardwareTest.Core.Settings.Ok" filename="Settings/Ok.cs">
          <lines>
            <line number="1" hits="1" />
            <line number="2" hits="1" />
            <line number="3" hits="1" />
            <line number="4" hits="1" />
            <line number="5" hits="1" />
            <line number="6" hits="1" />
            <line number="7" hits="1" />
            <line number="8" hits="1" />
            <line number="9" hits="1" />
            <line number="10" hits="1" />
          </lines>
        </class>
        <class name="HardwareTest.Core.Engine.Weak" filename="Engine/Weak.cs">
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
  assert(report.corePct >= 70);
  assertEquals(report.ok, false);
  assertEquals(report.failures.some((f) => f.includes("Core")), false);
  assert(report.failures.some((f) => f.includes("Engine")));
});

Deno.test("coverage skips Hardware floor when no Hardware lines exist", () => {
  const xml = `<?xml version="1.0"?>
<coverage>
  <packages>
    <package name="HardwareTest.Core">
      <classes>
        <class name="HardwareTest.Core.Settings.Ok" filename="Settings/Ok.cs">
          <lines>
            <line number="1" hits="1" />
            <line number="2" hits="1" />
            <line number="3" hits="1" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>`;
  const report = evaluateCobertura(xml);
  assertEquals(report.hardwareLines, 0);
  assertEquals(report.ok, true);
  assertEquals(report.failures.some((f) => f.includes("Hardware")), false);
});

Deno.test("coverage does not treat FooEngine as Engine", () => {
  const xml = `<?xml version="1.0"?>
<coverage>
  <packages>
    <package name="HardwareTest.Core">
      <classes>
        <class name="HardwareTest.Core.FooEngine.Bar" filename="FooEngine/Bar.cs">
          <lines>
            <line number="1" hits="0" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>`;
  const report = evaluateCobertura(xml);
  assertEquals(report.engineLines, 0);
});

Deno.test("findCobertura returns the sorted first match", async () => {
  const root = await Deno.makeTempDir({ prefix: "ht-cobertura-" });
  try {
    const late = path.join(root, "z-guid");
    const early = path.join(root, "a-guid");
    await Deno.mkdir(late, { recursive: true });
    await Deno.mkdir(early, { recursive: true });
    await Deno.writeTextFile(path.join(late, "coverage.cobertura.xml"), "<late/>");
    await Deno.writeTextFile(path.join(early, "coverage.cobertura.xml"), "<early/>");
    const found = await findCobertura(root);
    assertEquals(found, path.join(early, "coverage.cobertura.xml"));
  } finally {
    await Deno.remove(root, { recursive: true });
  }
});

Deno.test("findCobertura returns null when the directory is missing", async () => {
  const found = await findCobertura(path.join(Deno.cwd(), "no-such-coverage-dir"));
  assertEquals(found, null);
});

Deno.test("CORE_COVERAGE_FILTER excludes OpenTAP host tests", () => {
  assertEquals(CORE_COVERAGE_FILTER.includes("HardwareTest.Tests.OpenTap"), true);
  assertEquals(CORE_COVERAGE_FILTER.startsWith("FullyQualifiedName!~"), true);
});

Deno.test("e2eIsAdvisory is true for linux RIDs or the flag", () => {
  assertEquals(e2eIsAdvisory({ advisoryE2e: false, rid: "win-x64" }), false);
  assertEquals(e2eIsAdvisory({ advisoryE2e: true, rid: "win-x64" }), true);
  assertEquals(e2eIsAdvisory({ advisoryE2e: false, rid: "linux-x64" }), true);
  assertEquals(e2eIsAdvisory({ advisoryE2e: false, rid: "linux-arm64" }), true);
});

Deno.test("test:host does not attach a Coverlet collector", async () => {
  const src = await Deno.readTextFile(new URL("./main.ts", import.meta.url));
  const hostFn = src.match(/async function testHost[\s\S]*?\n\}/);
  assert(hostFn, "testHost function must exist");
  assertEquals(hostFn[0].includes("collect"), false);
  assertEquals(hostFn[0].includes("Coverlet"), false);
});

Deno.test("coverage fails when Hardware floor is missed", () => {
  const xml = `<?xml version="1.0"?>
<coverage>
  <packages>
    <package name="HardwareTest.Core">
      <classes>
        <class name="HardwareTest.Core.Settings.Ok" filename="Settings/Ok.cs">
          <lines>
            <line number="1" hits="1" />
            <line number="2" hits="1" />
            <line number="3" hits="1" />
            <line number="4" hits="1" />
            <line number="5" hits="1" />
            <line number="6" hits="1" />
            <line number="7" hits="1" />
            <line number="8" hits="1" />
            <line number="9" hits="1" />
            <line number="10" hits="1" />
          </lines>
        </class>
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
  assert(report.corePct >= 70);
  assertEquals(report.ok, false);
  assertEquals(report.failures.some((f) => f.includes("Core")), false);
  assert(report.failures.some((f) => f.includes("Hardware")));
});

Deno.test("TASKS catalog is sorted and complete", () => {
  const expected = [
    "all",
    "audit",
    "build",
    "coverage",
    "format",
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
    "format",
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

function jobBlock(yaml: string, job: string): string {
  const start = yaml.search(new RegExp(`^  ${job}:`, "m"));
  assert(start >= 0, `ci.yml must define job ${job}`);
  const rest = yaml.slice(start);
  const next = rest.slice(1).search(/^  [a-zA-Z]/m);
  return next === -1 ? rest : rest.slice(0, next + 1);
}

Deno.test("ci.yml catalog heredocs match TASKS on both platforms", async () => {
  const root = path.resolve(
    path.dirname(path.fromFileUrl(import.meta.url)),
    "../..",
  );
  const yaml = await Deno.readTextFile(path.join(root, ".github/workflows/ci.yml"));
  const expected = TASKS.join("\\n");
  const heredocs = [...yaml.matchAll(/expected=\$'([^']+)'/g)].map((m) => m[1]!);
  assertEquals(heredocs.length, 2, "windows and linux catalog asserts");
  for (const heredoc of heredocs) {
    assertEquals(heredoc, expected);
  }
});

Deno.test("ci.yml invokes required tasks with the matching platform RID", async () => {
  const root = path.resolve(
    path.dirname(path.fromFileUrl(import.meta.url)),
    "../..",
  );
  const yaml = await Deno.readTextFile(path.join(root, ".github/workflows/ci.yml"));
  const windows = jobBlock(yaml, "test");
  const linux = jobBlock(yaml, "test-linux");
  for (const task of ["build", "test:arch", "test:host", "test:vm", "test:e2e", "coverage"]) {
    assert(
      windows.includes(`main.ts ${task} --rid win-x64`),
      `windows test must call ${task} with win-x64`,
    );
    assert(
      linux.includes(`main.ts ${task} --rid linux-x64`),
      `linux test must call ${task} with linux-x64`,
    );
  }
  assert(windows.includes("main.ts format"));
  assert(linux.includes("main.ts format"));
  assert(jobBlock(yaml, "publish-win").includes("main.ts publish --rid win-x64"));
  assert(linux.includes("main.ts publish --rid linux-x64"));
});

Deno.test("linux E2E is advisory; windows E2E stays blocking", async () => {
  const root = path.resolve(
    path.dirname(path.fromFileUrl(import.meta.url)),
    "../..",
  );
  const yaml = await Deno.readTextFile(path.join(root, ".github/workflows/ci.yml"));
  const windows = jobBlock(yaml, "test");
  const linux = jobBlock(yaml, "test-linux");
  assert(linux.includes("E2E smoke (advisory on Linux)"));
  assert(linux.includes("continue-on-error: true"));
  assertEquals(windows.includes("continue-on-error: true"), false);
  assertEquals(windows.includes("advisory"), false);
});

Deno.test("artifact uploads fail when publish output is missing", async () => {
  const root = path.resolve(
    path.dirname(path.fromFileUrl(import.meta.url)),
    "../..",
  );
  const yaml = await Deno.readTextFile(path.join(root, ".github/workflows/ci.yml"));
  assertEquals((yaml.match(/if-no-files-found: error/g) ?? []).length, 2);
});

Deno.test("setup-dotnet pins the global.json SDK", async () => {
  const root = path.resolve(
    path.dirname(path.fromFileUrl(import.meta.url)),
    "../..",
  );
  const yaml = await Deno.readTextFile(path.join(root, ".github/workflows/ci.yml"));
  const globalJson = JSON.parse(
    await Deno.readTextFile(path.join(root, "global.json")),
  ) as { sdk: { version: string } };
  const pins = [...yaml.matchAll(/dotnet-version:\s*"([^"]+)"/g)].map((m) => m[1]!);
  assert(pins.length >= 3);
  for (const pin of pins) {
    assertEquals(pin, globalJson.sdk.version);
  }
});

Deno.test("both platform jobs run Deno catalog unit tests", async () => {
  const root = path.resolve(
    path.dirname(path.fromFileUrl(import.meta.url)),
    "../..",
  );
  const yaml = await Deno.readTextFile(path.join(root, ".github/workflows/ci.yml"));
  assert(jobBlock(yaml, "test").includes("deno task --cwd tools/ci test"));
  assert(jobBlock(yaml, "test-linux").includes("deno task --cwd tools/ci test"));
});
