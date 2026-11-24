import assert from "node:assert/strict";
import test from "node:test";
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
