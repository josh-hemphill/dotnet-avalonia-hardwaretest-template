import assert from "node:assert/strict";
import { test } from "node:test";
import { run, runCapture } from "./lib/run.ts";

test("capture preserves argument boundaries, environment and both output streams", async () => {
  const args = ["space here", 'quote"here', "$variable; & literal", ""];
  const result = await runCapture([
    process.execPath, "-e",
    'console.log(JSON.stringify({args:process.argv.slice(1), value:process.env.HT_CI_TEST, inherited:!!process.env.PATH})); console.error("stderr text"); process.exitCode=23;',
    ...args,
  ], { env: { HT_CI_TEST: "override" } });
  assert.equal(result.code, 23);
  assert.deepEqual(JSON.parse(result.stdout), { args, value: "override", inherited: true });
  assert.equal(result.stderr.trim(), "stderr text");
});

test("run rejects nonzero exits and spawn errors", async () => {
  await assert.rejects(
    run([process.execPath, "-e", "process.exitCode=23"]),
    /Command failed \(exit 23\)/,
  );
  await assert.rejects(runCapture(["ht-ci-nonexistent-command"]), { code: "ENOENT" });
});
