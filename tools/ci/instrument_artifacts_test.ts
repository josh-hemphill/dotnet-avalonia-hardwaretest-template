import { test } from "node:test";
import { writeTextFile, makeTempDir } from "./lib/fs.ts";
import * as fs from "node:fs/promises";
import { assertRejects } from "./lib/assert.ts";
import { join, resolve } from "node:path";
import { verifyReleaseProvisioning } from "./lib/instrument_artifacts.ts";
import { repoRoot } from "./lib/paths.ts";

test("release verifier accepts a genuine external archive with an absent default cache", async () => {
  const root = repoRoot();
  const temporary = await makeTempDir({
    prefix: "ht-offline-release-test-",
  });
  const original = process.env["InstrumentComponentsReleaseArchive"];
  try {
    const projectRoot = join(root, "src", "HardwareTest.OpenTap.Host");
    const supplied = original ? resolve(projectRoot, original) : join(
      projectRoot,
      "obj",
      "published-instrument-components",
      "0.1.1",
      "InstrumentComponents.OpenTap.0.1.1.TapPackage",
    );
    const archive = join(temporary, "external.TapPackage");
    await fs.copyFile(supplied, archive);
    const isolated = join(temporary, "checkout");
    const host = join(isolated, "src", "HardwareTest.OpenTap.Host");
    await fs.mkdir(host, { recursive: true });
    const target = join(projectRoot, "PublishedInstrumentComponents.targets")
      .replaceAll("&", "&amp;").replaceAll('"', "&quot;");
    await writeTextFile(
      join(host, "HardwareTest.OpenTap.Host.csproj"),
      `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Import Project="${target}" /></Project>`,
    );
    process.env["InstrumentComponentsReleaseArchive"] = archive;
    await verifyReleaseProvisioning({
      root: isolated,
      rid: process.platform === "win32" ? "win-x64" : "linux-x64",
      configuration: "Release",
    });
    await assertRejects(
      () =>
        fs.stat(
          join(
            host,
            "obj",
            "published-instrument-components",
            "0.1.1",
            "InstrumentComponents.OpenTap.0.1.1.TapPackage",
          ),
        ),
      { code: "ENOENT" },
    );
  } finally {
    if (original === undefined) {
      delete process.env["InstrumentComponentsReleaseArchive"];
    } else process.env["InstrumentComponentsReleaseArchive"] = original;
    await fs.rm(temporary, { recursive: true });
  }
});
