import { publishDir } from "./paths.ts";
import { runCapture } from "./run.ts";

export type ArtifactOptions = {
  root: string;
  rid: string;
  configuration: string;
};
const archiveName = "InstrumentComponents.OpenTap.0.1.1.TapPackage";
const releaseHash =
  "779fc31299fa4624f34b0031ce67a3278ff5f895a54ef3cbcc6f64c3a79ff2ee";
const baseNames = new Set([
  "InstrumentComponents",
  "InstrumentComponents.OpenTap",
]);

export function consumerOutputs(
  opts: ArtifactOptions,
): { name: string; path: string }[] {
  const out = publishDir(opts.rid, opts.root);
  return [
    { name: "main", path: out },
    { name: "authoring", path: `${out}/authoring` },
    ...["host", "worker", "validate"].map((name) => ({
      name,
      path: `${out}/tooling/${name}`,
    })),
  ];
}

export async function sha256(bytes: Uint8Array): Promise<string> {
  const digest = new Uint8Array(
    await crypto.subtle.digest("SHA-256", new Uint8Array(bytes).buffer),
  );
  return Array.from(digest, (value) => value.toString(16).padStart(2, "0"))
    .join("");
}

/** Inspect shipped files, including nested output directories, without loading plugins. */
export async function assertNoRuntimeLeak(
  directory: string,
  names: Set<string>,
): Promise<void> {
  for await (const entry of Deno.readDir(directory)) {
    const path = `${directory}/${entry.name}`;
    if (entry.isDirectory) await assertNoRuntimeLeak(path, names);
    else if (entry.isFile) {
      if (entry.name.endsWith(".dll") && names.has(entry.name.slice(0, -4))) {
        throw new Error(`Loose instrument payload leaked into ${path}`);
      }
      if (entry.name.endsWith(".deps.json")) {
        const graph = JSON.parse(await Deno.readTextFile(path));
        for (const library of Object.keys(graph.libraries ?? {})) {
          if (names.has(library.split("/")[0])) {
            throw new Error(
              `Instrument dependency leaked into ${path}: ${library}`,
            );
          }
        }
      }
    }
  }
}

export async function verifyPublishedRelease(
  opts: ArtifactOptions,
): Promise<void> {
  for (const output of consumerOutputs(opts)) {
    const hash = await sha256(
      await Deno.readFile(`${output.path}/PublishedArtifacts/${archiveName}`),
    );
    if (hash !== releaseHash) {
      throw new Error(`${output.name}: published release hash mismatch`);
    }
    await assertNoRuntimeLeak(output.path, baseNames);
    console.log(
      `verify library release: ${output.name} exact archive and runtime isolation passed`,
    );
  }
}

/** Exercise the real provisioning target; invalid explicit inputs must fail before compilation/download. */
export async function verifyReleaseProvisioning(
  opts: ArtifactOptions,
): Promise<void> {
  const temporary = await Deno.makeTempDir({ prefix: "ht-release-input-" });
  const cached =
    `${opts.root}/src/HardwareTest.OpenTap.Host/obj/published-instrument-components/0.1.1/${archiveName}`;
  try {
    const genuine = await Deno.readFile(cached);
    const valid = `${temporary}/valid.TapPackage`;
    const tampered = `${temporary}/tampered.TapPackage`;
    await Deno.writeFile(valid, genuine);
    const changed = genuine.slice();
    changed[0] ^= 1;
    await Deno.writeFile(tampered, changed);
    for (
      const [path, expected] of [
        [valid, null],
        [
          `${temporary}/missing.TapPackage`,
          "The supplied InstrumentComponents release archive does not exist",
        ],
        [tampered, "InstrumentComponents release SHA256 mismatch"],
      ] as const
    ) {
      const result = await runCapture([
        "dotnet",
        "msbuild",
        "src/HardwareTest.OpenTap.Host/HardwareTest.OpenTap.Host.csproj",
        "-t:ProvisionPublishedInstrumentComponents",
        `-p:Configuration=${opts.configuration}`,
        `-p:RuntimeIdentifier=${opts.rid}`,
        `-p:InstrumentComponentsReleaseArchive=${path}`,
      ], { cwd: opts.root });
      const message = `${result.stdout}\n${result.stderr}`;
      if (
        expected === null
          ? result.code !== 0
          : result.code === 0 || !message.includes(expected)
      ) {
        throw new Error(
          `Unexpected provisioning result for ${path}: ${message}`,
        );
      }
    }
    if (
      await sha256(await Deno.readFile(valid)) !== releaseHash ||
      await sha256(await Deno.readFile(cached)) !== releaseHash ||
      await sha256(await Deno.readFile(tampered)) !== await sha256(changed)
    ) {
      throw new Error(
        "Provisioning changed its supplied or cached input bytes",
      );
    }
    console.log(
      "verify release provisioning: genuine, missing and tampered explicit archives passed",
    );
  } finally {
    await Deno.remove(temporary, { recursive: true });
  }
}
