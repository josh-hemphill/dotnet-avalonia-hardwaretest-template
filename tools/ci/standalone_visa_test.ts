import { runCapture } from "./lib/run.ts";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import { readTextFile, writeTextFile, makeTempDir } from "./lib/fs.ts";
import * as fs from "node:fs/promises";
import { assertEquals, assertStringIncludes } from "./lib/assert.ts";
import * as path from "node:path";
import { consumerOutputs } from "./lib/instrument_artifacts.ts";

const fixtureProject =
  "tests/HardwareTest.StandaloneVisa.CliFixture/HardwareTest.StandaloneVisa.CliFixture.csproj";
type Event = { command: string; args: string[]; cwd: string };

async function exerciseCleanVerification(failBuild: boolean) {
  const root = await makeTempDir({ prefix: "ht-ci-clean-publish-" });
  try {
    const opts = { root, rid: "win-x64", configuration: "Debug" };
    const generated = path.join(root, "generated.TapPackage");
    const fixture = path.join(root, "fixture", "CliFixture.dll");
    const record = path.join(root, "commands.jsonl");
    const stub = path.join(root, "process.js");
    const shim = path.join(root, "shim.js");
    const harness = path.join(root, "verify.js");
    for (const consumer of consumerOutputs(opts)) {
      const artifacts = path.join(consumer.path, "PublishedArtifacts");
      await fs.mkdir(artifacts, { recursive: true });
      await writeTextFile(
        path.join(artifacts, "HardwareTest.Standalone.VISA.0.1.0.TapPackage"),
        "deterministic archive",
      );
    }
    await writeTextFile(generated, "deterministic archive");
    await writeTextFile(
      stub,
      `import * as fs from "node:fs/promises";
import { readTextFile, writeTextFile } from ${JSON.stringify(new URL("./lib/fs.ts", import.meta.url).href)};
const [command, ...args] = process.argv.slice(2);
await writeTextFile(${
        JSON.stringify(record)
      }, JSON.stringify({ command, args, cwd: process.cwd() }) + "\\n", { append: true });
const fixture = ${JSON.stringify(fixture)};
if (command === "dotnet" && args[0] === "build" && args[1] === ${
        JSON.stringify(fixtureProject)
      }) {
  if (${failBuild}) process.exit(23);
  await fs.mkdir(${
        JSON.stringify(path.dirname(fixture))
      }, { recursive: true });
  await writeTextFile(fixture, "built dispatcher fixture");
} else if (command === "dotnet" && args[0] === "build") {
  // Repeated wrapper builds preserve the deterministic archive above.
} else if (command === "dotnet" && args[0] === "msbuild") {
  if (args.includes("-getProperty:TargetPath")) {
    // A clean checkout has no fixture until the build succeeds.
    await fs.stat(fixture);
    console.log(fixture);
  } else console.log(${JSON.stringify(generated)});
} else if (args[0] === "--bootstrap") {
  await fs.mkdir(args[args.indexOf("--opentap-home") + 1], { recursive: true });
} else if (command === "dotnet" && args[1] === "visa-boundary") {
  if (await readTextFile(${
        JSON.stringify(path.basename(fixture))
      }) !== "built dispatcher fixture") process.exit(24);
  console.log("standalone-registered-before-dispatch-existing-provider-preserved");
  process.exit(17);
} else throw new Error("Unexpected command: " + command + " " + args.join(" "));
`,
    );
    // Keep subprocess execution real; replace external SDK/app commands only.
    await writeTextFile(
      shim,
      `import childProcess from "node:child_process";
import fs from "node:fs/promises";
import { syncBuiltinESMExports } from "node:module";
const spawn = childProcess.spawn;
childProcess.spawn = (command, args, options) => spawn(process.execPath,
  [${JSON.stringify(stub)}, command, ...args], options);
const mkdtemp = fs.mkdtemp;
fs.mkdtemp = async (...args) => {
  await fs.appendFile(${JSON.stringify(record)}, JSON.stringify({ command: "makeTempDir", args: [], cwd: process.cwd() }) + "\\n");
  return await mkdtemp(...args);
};
syncBuiltinESMExports();
`,
    );
    await writeTextFile(
      harness,
      `import { verifyStandaloneVisaArtifacts } from ${
        JSON.stringify(
          new URL("./lib/standalone_visa.ts", import.meta.url).href,
        )
      };
await verifyStandaloneVisaArtifacts(${JSON.stringify(opts)});
`,
    );
    const output = await runCapture([process.execPath, "--import", shim, harness], {
      cwd: path.dirname(fileURLToPath(import.meta.url)),
    });
    const events: Event[] = (await readTextFile(record)).trim().split("\n")
      .map((line) => JSON.parse(line));
    return { output, events, opts };
  } finally {
    await fs.rm(root, { recursive: true });
  }
}

test("standalone publish verification builds a missing fixture before relocation", async () => {
  const { output, events, opts } = await exerciseCleanVerification(false);
  assertEquals(output.code, 0, output.stderr);
  const build = events.findIndex((event) => event.args[1] === fixtureProject);
  assertEquals(events[build], {
    command: "dotnet",
    args: ["build", fixtureProject, "-c", opts.configuration, "-r", opts.rid],
    cwd: opts.root,
  });
  assertEquals(events[build + 1].args, [
    "msbuild",
    fixtureProject,
    `-p:Configuration=${opts.configuration}`,
    `-p:RuntimeIdentifier=${opts.rid}`,
    "-getProperty:TargetPath",
  ]);
  assertEquals(events[build + 2].command, "makeTempDir");
  assertEquals(events[build + 3].args[0], "--bootstrap");
  assertEquals(events[build + 4].args[1], "visa-boundary");
  assertStringIncludes(
    output.stdout,
    "relocated bootstrap and actual dispatcher",
  );
});

test("standalone fixture build failure stops before evaluating or creating a smoke home", async () => {
  const { output, events } = await exerciseCleanVerification(true);
  assertEquals(output.code, 1);
  assertStringIncludes(
    output.stderr,
    "Command failed (exit 23): dotnet build " + fixtureProject,
  );
  assertEquals(events.at(-1)?.args[1], fixtureProject);
  assertEquals(events.at(-1)?.args[0], "build");
  assertEquals(events.some((event) => event.command === "makeTempDir"), false);
  assertEquals(
    events.some((event) => event.args.includes("-getProperty:TargetPath")),
    false,
  );
});
