import { assertEquals, assertStringIncludes } from "@std/assert";
import * as path from "@std/path";

Deno.test("authoring UI command failures remain fatal with advisory E2E enabled", async () => {
  const directory = await Deno.makeTempDir({ prefix: "ht-ci-failing-dotnet-" });
  try {
    const record = path.join(directory, "dotnet-args.json");
    const stub = path.join(directory, "dotnet.js");
    const shim = path.join(directory, "launch-dotnet.js");
    const recordLiteral = JSON.stringify(record);
    const stubLiteral = JSON.stringify(stub);
    await Deno.writeTextFile(
      stub,
      `await Deno.writeTextFile(${recordLiteral}, JSON.stringify(Deno.args));
Deno.exit(23);
`,
    );
    // Use Deno to launch the failing stub on every platform, avoiding shell or
    // batch-file resolution differences. The production CLI and run() still
    // execute normally and receive the stub's real subprocess exit status.
    await Deno.writeTextFile(
      shim,
      `const Command = Deno.Command;
Deno.Command = class extends Command {
  constructor(command, options = {}) {
    if (command === "dotnet") {
      super(Deno.execPath(), {
        ...options,
        args: ["run", "--allow-write", ${stubLiteral}, ...options.args],
      });
    } else {
      super(command, options);
    }
  }
};
`,
    );
    const output = await new Deno.Command(Deno.execPath(), {
      args: [
        "run",
        "-A",
        "--preload",
        shim,
        path.fromFileUrl(new URL("./main.ts", import.meta.url)),
        "test:authoring-ui",
        "--rid",
        "linux-x64",
        "--advisory-e2e",
      ],
      stdout: "piped",
      stderr: "piped",
    }).output();

    assertEquals(
      JSON.parse(await Deno.readTextFile(record)),
      [
        "test",
        "tests/HardwareTest.Authoring.UI.Tests/HardwareTest.Authoring.UI.Tests.csproj",
        "-c",
        "Release",
        "-r",
        "linux-x64",
        "--no-build",
      ],
    );
    assertEquals(output.code, 1);
    assertEquals(output.success, false);
    assertStringIncludes(
      new TextDecoder().decode(output.stderr),
      "Command failed (exit 23): dotnet test",
    );
  } finally {
    await Deno.remove(directory, { recursive: true });
  }
});
