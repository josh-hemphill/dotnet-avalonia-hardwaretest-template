import * as path from "@std/path";
import { parseArgs } from "@std/cli/parse-args";
import { formatAuditFailure, hasVulnerablePackages } from "./lib/audit.ts";
import { evaluateCobertura, findCobertura } from "./lib/coverage.ts";
import { coverageDir, publishDir, repoRoot } from "./lib/paths.ts";
import { defaultRid } from "./lib/rid.ts";
import { run, runCapture } from "./lib/run.ts";

/** Canonical CI task names — workflow and `list` must stay in sync. */
export const TASKS = [
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
] as const;

/** OpenTAP host tests must not load Coverlet (process-global TapThread flakes). */
const CORE_COVERAGE_FILTER = "FullyQualifiedName!~HardwareTest.Tests.OpenTap";

export type TaskName = (typeof TASKS)[number];

type Options = {
  rid: string;
  configuration: string;
  root: string;
  advisoryE2e: boolean;
};

function usage(): string {
  return `HardwareTest CI tasks

Usage:
  deno run -A main.ts <task> [--rid <rid>] [--configuration Release]

Tasks:
  ${TASKS.join(", ")}

RID defaults to the host platform (${defaultRidSafe()}).
`;
}

function defaultRidSafe(): string {
  try {
    return defaultRid();
  } catch {
    return "(unknown — pass --rid)";
  }
}

function parseOptions(args: string[]): Options {
  const parsed = parseArgs(args, {
    string: ["rid", "configuration"],
    boolean: ["advisory-e2e", "help"],
    alias: { h: "help", c: "configuration" },
    default: {
      configuration: "Release",
      "advisory-e2e": false,
    },
  });

  if (parsed.help) {
    console.log(usage());
    Deno.exit(0);
  }

  const rid = typeof parsed.rid === "string" && parsed.rid.length > 0
    ? parsed.rid
    : defaultRid();

  return {
    rid,
    configuration: String(parsed.configuration ?? "Release"),
    root: repoRoot(),
    advisoryE2e: Boolean(parsed["advisory-e2e"]),
  };
}

async function build(opts: Options): Promise<void> {
  await run([
    "dotnet",
    "build",
    "dirs.proj",
    "-c",
    opts.configuration,
    "-r",
    opts.rid,
  ], { cwd: opts.root });
}

async function testHost(opts: Options): Promise<void> {
  await run([
    "dotnet",
    "test",
    "tests/HardwareTest.Tests/HardwareTest.Tests.csproj",
    "-c",
    opts.configuration,
    "-r",
    opts.rid,
    "--no-build",
  ], { cwd: opts.root });
}

async function testVm(opts: Options): Promise<void> {
  await run([
    "dotnet",
    "test",
    "tests/HardwareTest.ViewModels.Tests/HardwareTest.ViewModels.Tests.csproj",
    "-c",
    opts.configuration,
    "-r",
    opts.rid,
    "--no-build",
  ], { cwd: opts.root });
}

async function testE2e(opts: Options): Promise<void> {
  await run([
    "dotnet",
    "test",
    "tests/HardwareTest.E2E.Tests/HardwareTest.E2E.Tests.csproj",
    "-c",
    opts.configuration,
    "-r",
    opts.rid,
    "--no-build",
  ], { cwd: opts.root });
}

async function testArch(opts: Options): Promise<void> {
  await run([
    "dotnet",
    "test",
    "tests/HardwareTest.Architecture.Tests/HardwareTest.Architecture.Tests.csproj",
    "-c",
    opts.configuration,
    "-r",
    opts.rid,
    "--no-build",
  ], { cwd: opts.root });
}

async function collectCoreCoverage(opts: Options): Promise<void> {
  const results = coverageDir(opts.root);
  try {
    await Deno.remove(results, { recursive: true });
  } catch (err) {
    if (!(err instanceof Deno.errors.NotFound)) throw err;
  }
  await Deno.mkdir(results, { recursive: true });
  await run([
    "dotnet",
    "test",
    "tests/HardwareTest.Tests/HardwareTest.Tests.csproj",
    "-c",
    opts.configuration,
    "-r",
    opts.rid,
    "--no-build",
    "--filter",
    CORE_COVERAGE_FILTER,
    "--collect:XPlat Code Coverage",
    "--settings",
    "tests/coverage.runsettings",
    "--results-directory",
    results,
  ], { cwd: opts.root });
}

async function coverage(opts: Options): Promise<void> {
  await collectCoreCoverage(opts);
  const cobertura = await findCobertura(coverageDir(opts.root));
  if (!cobertura) {
    throw new Error("coverage.cobertura.xml not found under artifacts/coverage");
  }

  const xml = await Deno.readTextFile(cobertura);
  const report = evaluateCobertura(xml);
  console.log(report.summary);
  for (const failure of report.failures) {
    console.error(failure);
  }
  if (!report.ok) {
    Deno.exit(1);
  }
}

async function audit(opts: Options): Promise<void> {
  const result = await runCapture([
    "dotnet",
    "list",
    "HardwareTest.slnx",
    "package",
    "--vulnerable",
    "--include-transitive",
  ], { cwd: opts.root });
  const combined = `${result.stdout}\n${result.stderr}`;
  if (result.stdout.trim().length > 0) {
    console.log(result.stdout.trimEnd());
  }
  if (result.stderr.trim().length > 0) {
    console.error(result.stderr.trimEnd());
  }
  if (hasVulnerablePackages(combined)) {
    throw new Error(formatAuditFailure(combined));
  }
  if (result.code !== 0) {
    throw new Error(
      `dotnet list package --vulnerable failed (exit ${result.code})`,
    );
  }
  console.log("audit ok: no known vulnerable packages");
}

async function publish(opts: Options): Promise<void> {
  const out = publishDir(opts.rid, opts.root);
  await Deno.mkdir(out, { recursive: true });
  await run([
    "dotnet",
    "publish",
    "src/HardwareTest",
    "-c",
    opts.configuration,
    "-r",
    opts.rid,
    "--self-contained",
    "-p:PublishAot=false",
    "-o",
    out,
  ], { cwd: opts.root });
}

function publishedExe(opts: Options): string {
  const base = publishDir(opts.rid, opts.root);
  return Deno.build.os === "windows"
    ? path.join(base, "HardwareTest.exe")
    : path.join(base, "HardwareTest");
}

async function verify(opts: Options): Promise<void> {
  const expectedRid = opts.rid;
  const hostRid = defaultRid();
  if (expectedRid !== hostRid) {
    throw new Error(
      `verify requires a native RID (expected host ${hostRid}, got --rid ${expectedRid})`,
    );
  }

  const exe = publishedExe(opts);
  try {
    await Deno.stat(exe);
  } catch {
    console.log("publish output missing; running publish first");
    await publish(opts);
  }

  const version = await runCapture([exe, "--version"], { cwd: opts.root });
  if (version.code !== 0 || version.stdout.trim().length === 0) {
    throw new Error(`--version failed: ${version.stderr || version.stdout}`);
  }
  console.log(`version: ${version.stdout.trim()}`);

  const dataDir = await Deno.makeTempDir({ prefix: "ht-verify-" });
  try {
    const config = await runCapture(
      [exe, "--print-config", "--data-directory", dataDir],
      {
        cwd: opts.root,
        env: {
          ...Deno.env.toObject(),
          HARDWARETEST_LOG_MINIMUM_LEVEL: "Warning",
        },
      },
    );
    if (config.code !== 0) {
      throw new Error(`--print-config failed: ${config.stderr || config.stdout}`);
    }

    const lines = config.stdout.split(/\r?\n/);
    const dataRow = lines.find((l) => l.startsWith("DataDirectory\t"));
    if (!dataRow) {
      throw new Error("--print-config missing DataDirectory row");
    }

    const cols = dataRow.split("\t");
    const effective = cols[1] ?? "";
    const source = cols[2] ?? "";
    if (!effective.includes(dataDir) && effective !== dataDir) {
      throw new Error(
        `DataDirectory effective value did not use verify path (got ${effective})`,
      );
    }
    if (source !== "CommandLine" && source !== "Environment") {
      throw new Error(
        `expected DataDirectory source CommandLine|Environment, got ${source}`,
      );
    }

    console.log(`print-config: DataDirectory source=${source}`);
    console.log(`verify ok: rid=${expectedRid} config-source=${source}`);
  } finally {
    try {
      await Deno.remove(dataDir, { recursive: true });
    } catch {
      // best-effort cleanup
    }
  }
}

async function all(opts: Options): Promise<void> {
  await build(opts);
  await audit(opts);
  await testArch(opts);
  await testHost(opts);
  await testVm(opts);

  // Linux Avalonia headless E2E starts advisory; Windows keeps it required.
  if (opts.rid.startsWith("linux-") || opts.advisoryE2e) {
    try {
      await testE2e(opts);
    } catch (err) {
      console.warn(`advisory: test:e2e failed on ${opts.rid}: ${err}`);
    }
  } else {
    await testE2e(opts);
  }

  await coverage(opts);
  await publish(opts);
  await verify(opts);
}

function listTasks(): void {
  console.log(TASKS.join("\n"));
}

export async function main(argv = Deno.args): Promise<void> {
  const [task, ...rest] = argv;
  if (!task || task === "help" || task === "--help" || task === "-h") {
    console.log(usage());
    Deno.exit(task ? 0 : 2);
  }

  if (!(TASKS as readonly string[]).includes(task)) {
    console.error(`Unknown task: ${task}\n`);
    console.error(usage());
    Deno.exit(2);
  }

  if (task === "list") {
    listTasks();
    return;
  }

  const opts = parseOptions(rest);
  console.log(`task=${task} rid=${opts.rid} configuration=${opts.configuration}`);

  switch (task as TaskName) {
    case "build":
      await build(opts);
      break;
    case "audit":
      await audit(opts);
      break;
    case "test:host":
      await testHost(opts);
      break;
    case "test:vm":
      await testVm(opts);
      break;
    case "test:e2e":
      await testE2e(opts);
      break;
    case "test:arch":
      await testArch(opts);
      break;
    case "coverage":
      await coverage(opts);
      break;
    case "publish":
      await publish(opts);
      break;
    case "verify":
      await verify(opts);
      break;
    case "all":
      await all(opts);
      break;
    case "list":
      listTasks();
      break;
  }
}

if (import.meta.main) {
  try {
    await main();
  } catch (err) {
    console.error(err instanceof Error ? err.message : err);
    Deno.exit(1);
  }
}
