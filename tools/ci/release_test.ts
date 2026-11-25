import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import test from "node:test";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { stagePublishedNotices } from "./release-notices.ts";
import { archiveName, combineManifests, parseReleaseTag, releaseRids } from "./release.ts";

test("validates release tags and uses native archive formats", () => {
  assert.equal(parseReleaseTag("v0.1.0-preview.1"), "0.1.0");
  assert.equal(archiveName("v0.1.0", "win-x64"), "hardwaretest-v0.1.0-win-x64.zip");
  assert.equal(archiveName("v0.1.0", "linux-x64"), "hardwaretest-v0.1.0-linux-x64.tar.gz");
  for (const tag of ["v00.1.0", "v0.1.0-preview.01", "v0.1.0\n", "v0.1.0/next"]) {
    assert.throws(() => parseReleaseTag(tag), /Use vMAJOR/);
  }
});

test("combines only a complete pair for the exact tag, inventory, and commit", () => {
  const commit = "a".repeat(40);
  const tag = "v0.1.0-preview.1";
  const repository = "owner/repo";
  const manifests = releaseRids.map((rid) => ({
    tag,
    commit,
    repository,
    appVersion: "0.1.0+aaaaaaa",
    asset: { rid, file: archiveName(tag, rid), sha256: "b".repeat(64) },
  }));
  assert.equal(combineManifests(manifests, tag, commit, repository).assets.length, 2);
  assert.throws(() => combineManifests(manifests.slice(1), tag, commit, repository), /complete release/);
  assert.throws(() => combineManifests([manifests[0], manifests[0]], tag, commit, repository), /metadata disagrees/);
  assert.throws(() => combineManifests([
    manifests[0],
    { ...manifests[1], commit: "c".repeat(40) },
  ], tag, commit, repository), /metadata disagrees/);
});

test("stages nested packages, exact versions, runtime notices, and source fallbacks", async (t) => {
  const root = await mkdtemp(join(tmpdir(), "hardwaretest-notices-test-"));
  t.after(() => rm(root, { recursive: true, force: true }));
  const payload = join(root, "payload");
  const cache = join(root, "cache");
  const sources = join(root, "sources");
  await mkdir(join(payload, "authoring"), { recursive: true });
  await mkdir(sources);
  await writeFile(join(payload, "HardwareTest.deps.json"), JSON.stringify({
    libraries: {
      "Example/1.0.0": { type: "package" },
      "runtimepack.Runtime.Linux/1.0.0": { type: "runtimepack" },
      "HardwareTest/1.0.0": { type: "project" },
    },
  }));
  await writeFile(join(payload, "authoring", "Authoring.deps.json"), JSON.stringify({
    libraries: {
      "Example/2.0.0": { type: "package" },
      "Parlot/1.5.9": { type: "package" },
    },
  }));
  for (const [identity, notice] of [
    ["example/1.0.0", "first"], ["example/2.0.0", "second"], ["runtime.linux/1.0.0", "runtime"],
  ]) {
    await mkdir(join(cache, identity), { recursive: true });
    await writeFile(join(cache, identity, "LICENSE.txt"), notice);
  }
  await mkdir(join(cache, "parlot", "1.5.9"), { recursive: true });
  await writeFile(join(sources, "Parlot-LICENSE.txt"), "Parlot attribution");
  await writeFile(join(sources, "catalog.json"), "{}");
  await assert.rejects(stagePublishedNotices(payload, cache, sources), /No release license or notice files for parlot\/1\.5\.9/);

  await writeFile(join(sources, "catalog.json"), JSON.stringify({
    "parlot/1.5.9": { source: "https://example.org/parlot", files: ["Parlot-LICENSE.txt"] },
  }));
  const staged = await stagePublishedNotices(payload, cache, sources);
  assert.deepEqual(staged.packages, ["example/1.0.0", "example/2.0.0", "parlot/1.5.9", "runtime.linux/1.0.0"]);
  for (const [identity, file, expected] of [
    ["example/1.0.0", "LICENSE.txt", "first"],
    ["example/2.0.0", "LICENSE.txt", "second"],
    ["parlot/1.5.9", "Parlot-LICENSE.txt", "Parlot attribution"],
    ["runtime.linux/1.0.0", "LICENSE.txt", "runtime"],
  ]) {
    const relativePath = `licenses/packages/${identity}/${file}`;
    assert.ok(staged.files.includes(relativePath));
    assert.equal(await readFile(join(payload, relativePath), "utf8"), expected);
  }
  assert.match(await readFile(join(payload, "licenses", "DEPENDENCIES.md"), "utf8"), /https:\/\/example\.org\/parlot/);
});
