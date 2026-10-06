import * as path from "@std/path";
import {
  type ArtifactOptions,
  consumerOutputs,
} from "./instrument_artifacts.ts";
import { run, runCapture } from "./run.ts";

const archiveName = "HardwareTest.Standalone.VISA.0.1.0.TapPackage";
const wrapperName = "HardwareTest.OpenTap.StandaloneVisa";
const forbidden = new Set([
  "InstrumentComponents",
  "InstrumentComponents.OpenTap",
  "InstrumentComponents.Visa",
  "InstrumentComponents.OpenTap.Visa",
  wrapperName,
]);

function requireSameBytes(
  expected: Uint8Array,
  actual: Uint8Array,
  label: string,
) {
  if (
    expected.length !== actual.length ||
    expected.some((value, index) => value !== actual[index])
  ) {
    throw new Error(`${label}: standalone archive bytes differ`);
  }
}

async function* files(directory: string): AsyncGenerator<string> {
  for await (const entry of Deno.readDir(directory)) {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory) yield* files(file);
    else if (entry.isFile) yield file;
  }
}

async function evaluatedProperty(
  opts: ArtifactOptions,
  project: string,
  property: string,
): Promise<string> {
  const result = await runCapture([
    "dotnet",
    "msbuild",
    project,
    `-p:Configuration=${opts.configuration}`,
    `-p:RuntimeIdentifier=${opts.rid}`,
    `-getProperty:${property}`,
  ], { cwd: opts.root });
  if (result.code !== 0 || !result.stdout.trim()) {
    throw new Error(`Cannot evaluate ${property}: ${result.stderr}`);
  }
  return path.resolve(opts.root, result.stdout.trim());
}

/** Check real publish outputs, repeated generation, and a relocated consumer. */
export async function verifyStandaloneVisaArtifacts(
  opts: ArtifactOptions,
): Promise<void> {
  let expected: Uint8Array | undefined;
  for (const consumer of consumerOutputs(opts)) {
    const archive = await Deno.readFile(
      path.join(consumer.path, "PublishedArtifacts", archiveName),
    );
    if (expected) requireSameBytes(expected, archive, consumer.name);
    else expected = archive;
    for await (const file of files(consumer.path)) {
      if (
        file.endsWith(".dll") &&
        forbidden.has(path.basename(file, ".dll"))
      ) {
        throw new Error(`${consumer.name}: loose execution payload ${file}`);
      }
      if (file.endsWith(".deps.json")) {
        const graph = JSON.parse(await Deno.readTextFile(file));
        const names = [
          ...Object.keys(graph.libraries ?? {}),
          ...Object.values(graph.targets ?? {}).flatMap((target) =>
            Object.keys(target as Record<string, unknown>)
          ),
        ];
        if (names.some((name) => forbidden.has(name.split("/")[0]))) {
          throw new Error(
            `${consumer.name}: execution dependency leaked in ${file}`,
          );
        }
      }
    }
  }
  if (!expected) throw new Error("No published consumers to verify");

  const project =
    "src/HardwareTest.OpenTap.StandaloneVisa/HardwareTest.OpenTap.StandaloneVisa.csproj";
  const generated = await evaluatedProperty(
    opts,
    project,
    "StandaloneVisaArchive",
  );
  // Force the compiler and packaging target to run twice: an incremental no-op
  // cannot establish that generation is deterministic.
  for (let attempt = 0; attempt < 2; attempt++) {
    await run([
      "dotnet",
      "build",
      project,
      "--no-restore",
      "--no-incremental",
      "-c",
      opts.configuration,
      "-r",
      opts.rid,
    ], { cwd: opts.root });
    requireSameBytes(
      expected,
      await Deno.readFile(generated),
      `generation ${attempt + 1}`,
    );
  }
  await verifyRelocatedConsumer(opts);
  console.log(
    "standalone VISA ok: five consumer outputs/deps isolated, repeated archive bytes identical, relocated bootstrap and actual dispatcher",
  );
}

async function verifyRelocatedConsumer(opts: ArtifactOptions): Promise<void> {
  const authoring = consumerOutputs(opts).find((consumer) =>
    consumer.name === "authoring"
  );
  if (!authoring) throw new Error("Published authoring consumer missing");
  const fixture = await evaluatedProperty(
    opts,
    "tests/HardwareTest.StandaloneVisa.CliFixture/HardwareTest.StandaloneVisa.CliFixture.csproj",
    "TargetPath",
  );
  const smoke = await Deno.makeTempDir({ prefix: "ht-standalone-published-" });
  try {
    const app = path.join(smoke, "app");
    for await (const file of files(authoring.path)) {
      const destination = path.join(app, path.relative(authoring.path, file));
      await Deno.mkdir(path.dirname(destination), { recursive: true });
      await Deno.copyFile(file, destination);
      if (Deno.build.os !== "windows") {
        const mode = (await Deno.stat(file)).mode;
        if (mode !== null) await Deno.chmod(destination, mode);
      }
    }
    const workspace = path.join(smoke, "workspace");
    const home = path.join(smoke, "home");
    await Deno.mkdir(path.join(workspace, "plans"), { recursive: true });
    await Deno.writeTextFile(
      path.join(workspace, "authoring.json"),
      JSON.stringify({
        schemaVersion: 2,
        displayName: "Published standalone VISA smoke",
        plansDirectory: "plans",
        package: {
          name: "Standalone VISA Smoke",
          version: "0.1.0",
          os: "Windows,Linux",
        },
        dependencies: [{
          package: "InstrumentComponents.OpenTap",
          version: "^0.1.0",
        }],
      }),
    );
    const exe = path.join(
      app,
      `HardwareTest.Authoring${opts.rid.startsWith("win-") ? ".exe" : ""}`,
    );
    const bootstrap = await runCapture([
      exe,
      "--bootstrap",
      workspace,
      "--offline",
      "--opentap-home",
      home,
    ], { cwd: smoke });
    if (bootstrap.code !== 0) {
      throw new Error(
        `Relocated library bootstrap failed: ${bootstrap.stdout}\n${bootstrap.stderr}`,
      );
    }
    await Deno.copyFile(fixture, path.join(home, path.basename(fixture)));
    const dispatch = await runCapture([
      "dotnet",
      path.join(home, `${wrapperName}.dll`),
      "visa-boundary",
    ], { cwd: home });
    if (
      dispatch.code !== 17 ||
      !(dispatch.stdout + dispatch.stderr).includes(
        "standalone-registered-before-dispatch-existing-provider-preserved",
      )
    ) {
      throw new Error(
        `Installed standalone dispatch failed (${dispatch.code}): ${dispatch.stdout}\n${dispatch.stderr}`,
      );
    }
  } finally {
    await Deno.remove(smoke, { recursive: true });
  }
}
