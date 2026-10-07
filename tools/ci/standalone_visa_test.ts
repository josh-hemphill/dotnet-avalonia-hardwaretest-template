import { assertEquals, assertStringIncludes } from "@std/assert";
import * as path from "@std/path";
import { consumerOutputs } from "./lib/instrument_artifacts.ts";

const fixtureProject =
  "tests/HardwareTest.StandaloneVisa.CliFixture/HardwareTest.StandaloneVisa.CliFixture.csproj";
type Event = { command: string; args: string[]; cwd: string };

async function exerciseCleanVerification(failBuild: boolean) {
  const root = await Deno.makeTempDir({ prefix: "ht-ci-clean-publish-" });
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
      await Deno.mkdir(artifacts, { recursive: true });
      await Deno.writeTextFile(
        path.join(artifacts, "HardwareTest.Standalone.VISA.0.1.0.TapPackage"),
        "deterministic archive",
      );
    }
    await Deno.writeTextFile(generated, "deterministic archive");
    await Deno.writeTextFile(
      stub,
      `const [command, ...args] = Deno.args;
await Deno.writeTextFile(${
        JSON.stringify(record)
      }, JSON.stringify({ command, args, cwd: Deno.cwd() }) + "\\n", { append: true });
const fixture = ${JSON.stringify(fixture)};
if (command === "dotnet" && args[0] === "build" && args[1] === ${
        JSON.stringify(fixtureProject)
      }) {
  if (${failBuild}) Deno.exit(23);
  await Deno.mkdir(${
        JSON.stringify(path.dirname(fixture))
      }, { recursive: true });
  await Deno.writeTextFile(fixture, "built dispatcher fixture");
} else if (command === "dotnet" && args[0] === "build") {
  // Repeated wrapper builds preserve the deterministic archive above.
} else if (command === "dotnet" && args[0] === "msbuild") {
  if (args.includes("-getProperty:TargetPath")) {
    // A clean checkout has no fixture until the build succeeds.
    await Deno.stat(fixture);
    console.log(fixture);
  } else console.log(${JSON.stringify(generated)});
} else if (args[0] === "--bootstrap") {
  await Deno.mkdir(args[args.indexOf("--opentap-home") + 1], { recursive: true });
} else if (command === "dotnet" && args[1] === "visa-boundary") {
  if (await Deno.readTextFile(${
        JSON.stringify(path.basename(fixture))
      }) !== "built dispatcher fixture") Deno.exit(24);
  console.log("standalone-registered-before-dispatch-existing-provider-preserved");
  Deno.exit(17);
} else throw new Error("Unexpected command: " + command + " " + args.join(" "));
`,
    );
    // Keep run()/runCapture() and filesystem operations real, replacing only
    // external SDK/app processes with portable Deno subprocesses.
    await Deno.writeTextFile(
      shim,
      `const Command = Deno.Command;
Deno.Command = class extends Command {
  constructor(command, options = {}) {
    super(Deno.execPath(), {
      ...options,
      args: ["run", "-A", ${
        JSON.stringify(stub)
      }, command, ...(options.args ?? [])],
    });
  }
};
const makeTempDir = Deno.makeTempDir;
Deno.makeTempDir = async (options) => {
  await Deno.writeTextFile(${
        JSON.stringify(record)
      }, JSON.stringify({ command: "makeTempDir", args: [], cwd: Deno.cwd() }) + "\\n", { append: true });
  return await makeTempDir(options);
};
`,
    );
    await Deno.writeTextFile(
      harness,
      `import { verifyStandaloneVisaArtifacts } from ${
        JSON.stringify(
          new URL("./lib/standalone_visa.ts", import.meta.url).href,
        )
      };
await verifyStandaloneVisaArtifacts(${JSON.stringify(opts)});
`,
    );
    const output = await new Deno.Command(Deno.execPath(), {
      args: [
        "run",
        "-A",
        "--config",
        path.fromFileUrl(new URL("./deno.json", import.meta.url)),
        "--preload",
        shim,
        harness,
      ],
      cwd: path.dirname(path.fromFileUrl(import.meta.url)),
      stdout: "piped",
      stderr: "piped",
    }).output();
    const events: Event[] = (await Deno.readTextFile(record)).trim().split("\n")
      .map((line) => JSON.parse(line));
    return { output, events, opts };
  } finally {
    await Deno.remove(root, { recursive: true });
  }
}

Deno.test("standalone publish verification builds a missing fixture before relocation", async () => {
  const { output, events, opts } = await exerciseCleanVerification(false);
  assertEquals(output.code, 0, new TextDecoder().decode(output.stderr));
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
    new TextDecoder().decode(output.stdout),
    "relocated bootstrap and actual dispatcher",
  );
});

Deno.test("standalone fixture build failure stops before evaluating or creating a smoke home", async () => {
  const { output, events } = await exerciseCleanVerification(true);
  assertEquals(output.code, 1);
  assertStringIncludes(
    new TextDecoder().decode(output.stderr),
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
