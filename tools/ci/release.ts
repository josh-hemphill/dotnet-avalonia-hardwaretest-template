import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import * as fs from "node:fs/promises";
import { homedir, tmpdir } from "node:os";
import { basename, join, resolve } from "node:path";
import { verifyPublishedRelease } from "./lib/instrument_artifacts.ts";
import { publishDir, repoRoot } from "./lib/paths.ts";
import { isNativeRid } from "./lib/rid.ts";
import { stagePublishedNotices } from "./release-notices.ts";
import { run, runCapture } from "./lib/run.ts";

export const releaseRids = ["win-x64", "linux-x64"] as const;
type ReleaseRid = (typeof releaseRids)[number];
type Asset = { rid: ReleaseRid; file: string; sha256: string };
type Build = { tag: string; commit: string; appVersion: string; repository: string };
type NativeManifest = Build & { asset: Asset };

export function parseReleaseTag(tag: string): string {
  const match = /^v((?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))(?:-(?:alpha|beta|rc|preview)\.(?:0|[1-9]\d*))?$/.exec(tag);
  if (!match || match[0] !== tag) {
    throw new Error("Use vMAJOR.MINOR.PATCH or add -preview.N, -alpha.N, -beta.N or -rc.N.");
  }
  return match[1];
}

export function archiveName(tag: string, rid: ReleaseRid): string {
  parseReleaseTag(tag);
  return `hardwaretest-${tag}-${rid}.${rid === "win-x64" ? "zip" : "tar.gz"}`;
}

async function json<T>(file: string): Promise<T> {
  return JSON.parse(await fs.readFile(file, "utf8"));
}

async function writeJson(file: string, value: unknown): Promise<void> {
  await fs.writeFile(file, JSON.stringify(value, null, 2) + "\n");
}

async function hashFile(file: string): Promise<string> {
  const hash = createHash("sha256");
  for await (const chunk of createReadStream(file)) {
    hash.update(chunk);
  }
  return hash.digest("hex");
}

async function stageSample(root: string, workspace: string): Promise<void> {
  const plans = join(root, "plans", "opentap");
  await fs.mkdir(workspace, { recursive: true });
  for (const file of ["authoring.schema.json", "authoring-draft.schema.json", "program.schema.json"]) {
    await fs.copyFile(join(plans, file), join(workspace, file));
  }
  // A distinct id keeps the product plan visible beside the built-in sample.
  await fs.copyFile(join(plans, "sample.TapPlan"), join(workspace, "starter-voltage.TapPlan"));
  const sidecar = await json<Record<string, unknown>>(join(plans, "sample.program.json"));
  await writeJson(join(workspace, "starter-voltage.program.json"), {
    ...sidecar,
    displayName: "Starter Voltage Test (Demo)",
  });
  const manifest = await json<{ package: Record<string, unknown> }>(join(plans, "authoring.json"));
  await writeJson(join(workspace, "authoring.json"), {
    ...manifest,
    displayName: "Starter Test Set",
    package: { ...manifest.package, name: "HardwareTest Starter Test Set", os: "Windows,Linux" },
  });
}

async function stageNotices(root: string, payload: string, build: Build): Promise<string[]> {
  await fs.copyFile(join(root, "LICENSE"), join(payload, "LICENSE"));
  await fs.copyFile(join(root, "THIRD-PARTY-NOTICES.md"), join(payload, "THIRD-PARTY-NOTICES.md"));
  const notices = join(payload, "licenses");
  await fs.mkdir(notices);
  await fs.copyFile(join(root, "licenses", "Apache-2.0.txt"), join(notices, "Apache-2.0.txt"));
  const packages = process.env.NUGET_PACKAGES ?? join(homedir(), ".nuget", "packages");
  const published = await stagePublishedNotices(payload, packages, join(root, "licenses", "upstream"));
  const miPdf = published.packages.find((identity) => identity.startsWith("mipdflib/"));
  const ivi = published.packages.find((identity) => identity.startsWith("ivifoundation.visa/"));
  if (!miPdf || !ivi) throw new Error("The release needs the miPDFlib notice and IVI Foundation VISA agreement.");
  await fs.access(join(notices, "packages", miPdf, "NOTICE"));
  await fs.copyFile(join(packages, ivi, "license", "license.txt"), join(notices, "ivifoundation.visa-agreement.txt"));
  await fs.writeFile(join(payload, "SOURCE.md"), [
    "# Source and redistribution",
    "",
    `HardwareTest source and build instructions: https://github.com/${build.repository}/tree/${build.commit}`,
    `miPDFlib source: https://github.com/Mibuw/miPDFlib/tree/v${miPdf.split("/")[1]}`,
    "",
    "HardwareTest is licensed under MIT and miPDFlib under Apache-2.0. The archive includes the separate IVI Foundation VISA agreement.",
    "See LICENSE, THIRD-PARTY-NOTICES.md, and licenses/DEPENDENCIES.md for license texts, package notices, and available upstream sources.",
    "",
  ].join("\n"));
  return [
    "LICENSE", "SOURCE.md", "THIRD-PARTY-NOTICES.md", "licenses/Apache-2.0.txt",
    "licenses/ivifoundation.visa-agreement.txt", ...published.files,
  ];
}

function tarExecutable(rid: ReleaseRid): string {
  return rid === "win-x64" ? join(process.env.SystemRoot ?? "C:\\Windows", "System32", "tar.exe") : "tar";
}

async function verifyExtracted(root: string, scratch: string, rid: ReleaseRid, archive: string, build: Build, noticeFiles: string[]): Promise<void> {
  const app = publishDir(rid, scratch);
  await fs.mkdir(app, { recursive: true });
  await run([tarExecutable(rid), "-xf", archive, "-C", app]);
  const suffix = rid === "win-x64" ? ".exe" : "";
  const operator = join(app, `HardwareTest${suffix}`);
  const worker = join(app, `HardwareTest.OpenTap.Worker${suffix}`);
  const author = join(app, "authoring", `HardwareTest.Authoring${suffix}`);
  for (const file of [operator, worker, author]) {
    const info = await fs.stat(file);
    if (!info.isFile() || (rid === "linux-x64" && !(info.mode & 0o111))) {
      throw new Error(`Archive lost executable: ${file}`);
    }
  }
  const version = await runCapture([operator, "--version"], { cwd: scratch });
  if (version.code !== 0 || version.stdout.trim() !== build.appVersion) {
    throw new Error("Extracted Operator reports a different build identity.");
  }
  for (const file of noticeFiles) {
    await fs.access(join(app, file));
  }
  await verifyPublishedRelease({ root: scratch, rid, configuration: "Release" });

  const workspace = join(scratch, "workspace");
  const home = join(scratch, "authoring-home");
  const data = join(scratch, "station-data");
  const env = { HARDWARETEST_DATA_DIRECTORY: data, HARDWARETEST_USE_MOCK_VISA: "true" };
  await stageSample(root, workspace);
  await run([author, "--bootstrap", workspace, "--offline", "--opentap-home", home], { cwd: scratch, env });
  await run([author, "--validate", workspace, "--strict", "--opentap-home", home], { cwd: scratch, env });
  const dist = join(scratch, "dist");
  await run([author, "--pack", workspace, "--out", dist, "--opentap-home", home], { cwd: scratch, env });
  const result = await json<{ files: string[] }>(join(dist, "ship-manifest.json"));
  if (!result.files.some((file) => file.endsWith(".TapPackage") && basename(file) === file)) {
    throw new Error("The starter did not produce a program TapPackage.");
  }
  for (const file of result.files) {
    await fs.access(join(dist, file));
  }

  // Exercise the extracted Worker and produce real result evidence with mock instruments.
  const project = join(root, "examples", "AdvancedProcedure", "Runner", "AdvancedProcedure.csproj");
  const runner = ["dotnet", "run", "--no-build", "--project", project, "-c", "Release", "-r", rid, "--"];
  await run(["dotnet", "build", project, "-c", "Release", "-r", rid, "-p:RestoreLockedMode=true"], { cwd: root });
  await run([...runner, "stage", join(root, "examples", "AdvancedProcedure", "Program"), app, data], { cwd: scratch, env });
  await run([...runner, "simulate", app, data, "Loopback", "Crossed", "Terminated"], { cwd: scratch, env });
  const runs = await fs.readdir(join(data, "runs"));
  if (runs.length !== 1) throw new Error("Expected one isolated mock run.");
  const runDirectory = join(data, "runs", runs[0]);
  const record = await json<{ samples: unknown[] }>(join(runDirectory, "run.json"));
  if (record.samples.length === 0 || !(await fs.readdir(runDirectory)).some((file) => file.endsWith(".pdf"))) {
    throw new Error("The extracted Worker did not produce samples and a PDF.");
  }
}

async function bundle(rid: ReleaseRid, directory: string, requestedTag?: string): Promise<void> {
  if (!releaseRids.includes(rid) || !isNativeRid(rid)) throw new Error(`Unsupported native release RID: ${rid}`);
  const root = repoRoot();
  const commitResult = await runCapture(["git", "rev-parse", "HEAD"], { cwd: root });
  const commit = commitResult.stdout.trim();
  const repository = process.env.GITHUB_REPOSITORY ?? "josh-hemphill/dotnet-avalonia-hardwaretest-template";
  if (commitResult.code !== 0 || !/^[0-9a-f]{40}$/.test(commit) || !/^[\w.-]+\/[\w.-]+$/.test(repository)) {
    throw new Error("Expected a Git commit and GitHub repository identity.");
  }
  const publish = publishDir(rid, root);
  const version = await runCapture([join(publish, rid === "win-x64" ? "HardwareTest.exe" : "HardwareTest"), "--version"], { cwd: root });
  const appVersion = version.stdout.trim();
  const [number, stamp] = appVersion.split("+");
  const tag = requestedTag || `v${number}-preview.0`;
  if (version.code !== 0 || parseReleaseTag(tag) !== number || stamp !== commit.slice(0, 7)) {
    throw new Error(`Tag ${tag} and source do not match app identity ${appVersion}.`);
  }

  const build = { tag, commit, appVersion, repository };
  const file = archiveName(tag, rid);
  const scratch = await fs.mkdtemp(join(tmpdir(), "hardwaretest-release-"));
  try {
    const payload = join(scratch, "payload");
    const archive = join(scratch, file);
    await fs.cp(publish, payload, { recursive: true });
    const noticeFiles = await stageNotices(root, payload, build);
    await writeJson(join(payload, "release.json"), { ...build, rid });
    const create = rid === "win-x64" ? ["-a", "-cf"] : ["-czf"];
    await run([tarExecutable(rid), ...create, archive, "-C", payload, "."]);
    await verifyExtracted(root, scratch, rid, archive, build, noticeFiles);
    await fs.mkdir(directory, { recursive: true });
    await fs.copyFile(archive, join(directory, file));
    await writeJson(join(directory, "native-manifest.json"), {
      ...build,
      asset: { rid, file, sha256: await hashFile(archive) },
    } satisfies NativeManifest);
  } finally {
    await fs.rm(scratch, { recursive: true, force: true });
  }
}

export function combineManifests(manifests: NativeManifest[], tag: string, commit: string, repository: string): Build & { assets: Asset[] } {
  const number = parseReleaseTag(tag);
  const appVersion = `${number}+${commit.slice(0, 7)}`;
  if (!/^[0-9a-f]{40}$/.test(commit) || manifests.length !== releaseRids.length) {
    throw new Error("A complete release requires one manifest per supported platform.");
  }
  const assets = releaseRids.map((rid) => {
    const matching = manifests.filter((manifest) => manifest.asset.rid === rid);
    const manifest = matching[0];
    if (matching.length !== 1 || manifest.tag !== tag || manifest.commit !== commit
      || manifest.repository !== repository || manifest.appVersion !== appVersion
      || manifest.asset.file !== archiveName(tag, rid) || !/^[0-9a-f]{64}$/.test(manifest.asset.sha256)) {
      throw new Error(`Release metadata disagrees for ${rid}.`);
    }
    return manifest.asset;
  });
  return { tag, commit, appVersion, repository, assets };
}

async function collect(directory: string, output: string, tag: string): Promise<void> {
  const root = repoRoot();
  const commit = process.env.GITHUB_SHA ?? "";
  const repository = process.env.GITHUB_REPOSITORY ?? "";
  const manifests = await Promise.all(releaseRids.map((rid) =>
    json<NativeManifest>(join(directory, `release-${rid}`, "native-manifest.json"))));
  const release = combineManifests(manifests, tag || manifests[0].tag, commit, repository);

  await fs.mkdir(output, { recursive: true });
  for (const asset of release.assets) {
    const path = join(directory, `release-${asset.rid}`, asset.file);
    if (await hashFile(path) !== asset.sha256) throw new Error(`Archive checksum mismatch: ${asset.file}`);
    await fs.copyFile(path, join(output, asset.file));
  }
  await writeJson(join(output, "release.json"), release);
  const scratch = await fs.mkdtemp(join(tmpdir(), "hardwaretest-starter-"));
  try {
    await fs.cp(join(root, "starters", "test-set"), scratch, { recursive: true });
    await fs.copyFile(join(root, "global.json"), join(scratch, "global.json"));
    await fs.copyFile(join(root, "LICENSE"), join(scratch, "LICENSE"));
    await stageSample(root, join(scratch, "workspace"));
    await writeJson(join(scratch, "hardwaretest-release.json"), release);
    const starter = join(output, `hardwaretest-testset-starter-${release.tag}.zip`);
    await run(["zip", "-q", "-r", starter, "."], { cwd: scratch });
    const files = [...release.assets.map(({ file }) => file), basename(starter), "release.json"];
    const checksums = await Promise.all(files.map(async (file) => `${await hashFile(join(output, file))}  ${file}`));
    await fs.writeFile(join(output, "SHA256SUMS.txt"), checksums.join("\n") + "\n");
  } finally {
    await fs.rm(scratch, { recursive: true, force: true });
  }
}

if (import.meta.main) {
  const args = process.argv.slice(2);
  try {
    if (args[0] === "bundle" && (args.length === 3 || args.length === 4)) {
      await bundle(args[1] as ReleaseRid, resolve(args[2]), args[3]);
    } else if (args[0] === "collect" && args.length === 4) {
      await collect(resolve(args[1]), resolve(args[2]), args[3]);
    } else {
      throw new Error("Usage: release.ts bundle <rid> <output> [tag] | collect <artifacts> <output> <tag>");
    }
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  }
}
