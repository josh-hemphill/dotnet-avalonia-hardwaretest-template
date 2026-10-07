import { assertRejects } from "@std/assert";
import { join, resolve } from "@std/path";
import { verifyReleaseProvisioning } from "./lib/instrument_artifacts.ts";
import { repoRoot } from "./lib/paths.ts";

Deno.test("release verifier accepts a genuine external archive with an absent default cache", async () => {
  const root = repoRoot();
  const temporary = await Deno.makeTempDir({
    prefix: "ht-offline-release-test-",
  });
  const original = Deno.env.get("InstrumentComponentsReleaseArchive");
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
    await Deno.copyFile(supplied, archive);
    const isolated = join(temporary, "checkout");
    const host = join(isolated, "src", "HardwareTest.OpenTap.Host");
    await Deno.mkdir(host, { recursive: true });
    const target = join(projectRoot, "PublishedInstrumentComponents.targets")
      .replaceAll("&", "&amp;").replaceAll('"', "&quot;");
    await Deno.writeTextFile(
      join(host, "HardwareTest.OpenTap.Host.csproj"),
      `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><Import Project="${target}" /></Project>`,
    );
    Deno.env.set("InstrumentComponentsReleaseArchive", archive);
    await verifyReleaseProvisioning({
      root: isolated,
      rid: Deno.build.os === "windows" ? "win-x64" : "linux-x64",
      configuration: "Release",
    });
    await assertRejects(
      () =>
        Deno.stat(
          join(
            host,
            "obj",
            "published-instrument-components",
            "0.1.1",
            "InstrumentComponents.OpenTap.0.1.1.TapPackage",
          ),
        ),
      Deno.errors.NotFound,
    );
  } finally {
    if (original === undefined) {
      Deno.env.delete("InstrumentComponentsReleaseArchive");
    } else Deno.env.set("InstrumentComponentsReleaseArchive", original);
    await Deno.remove(temporary, { recursive: true });
  }
});
