import { runCapture } from "./lib/run.ts";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import { readTextFile, writeTextFile, makeTempDir } from "./lib/fs.ts";
import * as fs from "node:fs/promises";
import { assertEquals, assertStringIncludes } from "./lib/assert.ts";
import * as path from "node:path";

test("authoring UI command failures remain fatal with advisory E2E enabled", async () => {
  const directory = await makeTempDir({ prefix: "ht-ci-failing-dotnet-" });
  try {
    const record = path.join(directory, "dotnet-args.json");
    const stub = path.join(directory, "dotnet.js");
    const shim = path.join(directory, "launch-dotnet.js");
    const recordLiteral = JSON.stringify(record);
    const stubLiteral = JSON.stringify(stub);
    await writeTextFile(
      stub,
      `import { writeFile } from "node:fs/promises";
await writeFile(${recordLiteral}, JSON.stringify(process.argv.slice(2)));
process.exit(23);
`,
    );
    // Redirect only dotnet while running the real CLI and subprocess helper.
    await writeTextFile(
      shim,
      `import childProcess from "node:child_process";
import { syncBuiltinESMExports } from "node:module";
const spawn = childProcess.spawn;
childProcess.spawn = (command, args, options) => command === "dotnet"
  ? spawn(process.execPath, [${stubLiteral}, ...args], options)
  : spawn(command, args, options);
syncBuiltinESMExports();
`,
    );
    const output = await runCapture([process.execPath,
      "--import", shim,
      fileURLToPath(new URL("./main.ts", import.meta.url)),
      "test:authoring-ui", "--rid", "linux-x64", "--advisory-e2e",
    ]);

    assertEquals(
      JSON.parse(await readTextFile(record)),
      [
        "test",
        "tests/HardwareTest.Authoring.UI.Tests/HardwareTest.Authoring.UI.Tests.csproj",
        "-c",
        "Release",
        "-r",
        "linux-x64",
        "--no-build",
        "--no-restore",
        "--logger",
        "trx;LogFileName=HardwareTest.Authoring.UI.Tests.trx",
        "--results-directory",
        path.resolve(
          fileURLToPath(
            new URL("../../artifacts/test-results/linux-x64", import.meta.url),
          ),
        ),
      ],
    );
    assertEquals(output.code, 1);
    assertStringIncludes(
      output.stderr,
      "Command failed (exit 23): dotnet test",
    );
  } finally {
    await fs.rm(directory, { recursive: true });
  }
});
