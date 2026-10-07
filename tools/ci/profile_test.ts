import { assert, assertEquals } from "@std/assert";
import * as path from "@std/path";
import {
  aggregatePassed,
  AUTHORING_FAST_FILTER,
  AUTHORING_UI_WINDOWS_FILTER,
  AUTHORING_WINDOWS_FILTER,
  classifyPaths,
  eventProfile,
} from "./lib/profile.ts";
import { formatSolution } from "./lib/format.ts";
import { TASKS } from "./main.ts";

Deno.test("shared inputs, SDK, tooling, unknown paths and both rename sides require full", () => {
  for (
    const changed of [
    "src/HardwareTest.Core/Engine/Engine.cs",
    "src/HardwareTest/Features/Results/Extra.CSPROJ",
      "src/HardwareTest/Composition.cs",
      "src/HardwareTest/DeferredStartup.cs",
      "src/HardwareTest/App.axaml.cs",
      "src/HardwareTest/Features/Shell/ShellHost.cs",
      "src/HardwareTest/Features/NewFeature/NewViewModel.cs",
      "src/HardwareTest.OpenTap.Host/Host.cs",
      "src/HardwareTest.OpenTap.Worker/Program.cs",
      "src/HardwareTest.OpenTap.StandaloneVisa/Provider.cs",
      "src/HardwareTest.OpenTap.Plugins.Basic/Step.cs",
      "src/HardwareTest.OpenTap.Plugins.Mixins/Mixin.cs",
      "src/HardwareTest.Authoring.Core/AuthoringBuildCapture.cs",
      "src/HardwareTest.Authoring/MainWindow.axaml",
      "src/HardwareTest.ShellApps.Notes/NotesView.axaml",
      "src/HardwareTest.Shell.Abstractions/IShellApp.cs",
      "tests/HardwareTest.Authoring.Tests/CurrentAuthoringFixtures.cs",
      "tests/HardwareTest.Authoring.Tests/PublishedLibraryFixture.cs",
      "tests/HardwareTest.Authoring.ProcessFixture/Program.cs",
      "plans/opentap/sample.TapPlan",
      "Directory.Build.props",
      "Directory.Packages.props",
      "global.json",
      "NuGet.Config",
      "dirs.proj",
      "tools/ci/deno.lock",
      "src/HardwareTest/Features/Results/packages.lock.json",
      "tests/HardwareTest.ViewModels.Tests/HardwareTest.ViewModels.Tests.csproj",
      ".github/workflows/ci.yml",
      "scripts/build.sh",
      "new-subsystem/code.cs",
    ]
  ) {
    assertEquals(classifyPaths([changed]), "full", changed);
    // A deletion or rename out of a shared path still routes to full.
    assertEquals(classifyPaths([changed, "docs/moved.md"]), "full", changed);
  }
  assertEquals(classifyPaths([]), "full");
});

Deno.test("known operator UI and docs use fast; non-PR and absent diff always use full", () => {
  const paths = [
    "src/HardwareTest/Features/RunTest/RunTestViewModel.cs",
    "docs/testing.md",
  ];
  for (
    const feature of [
      "Home",
      "Inspect",
      "Instruments",
      "Presentation",
      "ReportPreview",
      "Results",
      "RunTest",
      "Settings",
    ]
  ) {
    assertEquals(
      classifyPaths([
        `src/HardwareTest/Features/${feature}/${feature}ViewModel.cs`,
      ]),
      "fast",
      feature,
    );
    assertEquals(
      classifyPaths([
        `src/HardwareTest/Features/${feature}/${feature}View.axaml`,
      ]),
      "fast",
      feature,
    );
    assertEquals(
      classifyPaths([
        `src/HardwareTest/Features/${feature}/packages.lock.json`,
      ]),
      "full",
      feature,
    );
  }
  assertEquals(eventProfile("pull_request", paths), "fast");
  assertEquals(eventProfile("pull_request"), "full");
  for (const event of ["push", "schedule", "workflow_dispatch", "unknown"]) {
    assertEquals(eventProfile(event, paths), "full");
  }
});

Deno.test("aggregate rejects failed, cancelled, skipped and missing required children", () => {
  for (const result of ["failure", "cancelled", "skipped", "", "unknown"]) {
    assertEquals(
      aggregatePassed({ policy: "success", child: result }, [
        "policy",
        "child",
      ]),
      false,
    );
  }
  assertEquals(
    aggregatePassed({ policy: "success" }, ["policy", "child"]),
    false,
  );
  assertEquals(
    aggregatePassed({ child: "success", publish: "skipped" }, ["child"], [
      "publish",
    ]),
    true,
  );
  assertEquals(
    aggregatePassed({ child: "success", publish: "failure" }, ["child"], [
      "publish",
    ]),
    false,
  );
  assertEquals(
    aggregatePassed({ child: "success", publish: "skipped" }, [
      "child",
      "publish",
    ]),
    false,
  );
});

Deno.test("format workspace includes each traversal project once with safe XML paths", () => {
  const root = path.resolve("format-fixture");
  const projects = ["src/A/A.csproj", "tests/B/B.csproj", "src/R&D/R&D.csproj"]
    .map((project) => path.join(root, project));
  const xml = formatSolution(root, [...projects, projects[0]!]);
  assertEquals([...xml.matchAll(/<Project Path=/g)].length, projects.length);
  assert(xml.includes("R&amp;D"));
  assert(xml.includes("B.csproj"));
});

Deno.test("fast authoring keeps logical compatibility, progress and pre-SDK safety guards", async () => {
  assertEquals(AUTHORING_FAST_FILTER, "Category!=AuthoringIntegration");
  const directory = new URL(
    "../../tests/HardwareTest.Authoring.Tests/",
    import.meta.url,
  );
  for (
    const file of [
      "TuiCompatCheckerTests",
      "AuthoringProgressFileTests",
      "FormulaParserTests",
      "AuthoringDocumentStoreTests",
    ]
  ) {
    const source = await Deno.readTextFile(new URL(`${file}.cs`, directory));
    assertEquals(
      source.includes('[Trait("Category", "AuthoringIntegration")]'),
      false,
      file,
    );
  }
  const guards = {
    AuthoringBuildEnvironmentGuardTests: [
      "Inherited_redirect_properties_are_rejected_before_SDK_evaluation",
      "SDK_environment_bindings_must_resolve_inside_declared_SDK",
    ],
    AuthoringBuildShellCopyTests: [
      "Copy_metadata_cannot_overwrite_external_sentinel",
      "Project_reference_cannot_remove_or_override_checked_global_properties",
    ],
    AuthoringBuildBoundaryTests: [
      "Versioned_receipt_and_build_result_roundtrip_with_owned_collections",
      "Publication_retires_previous_owned_outputs_transactionally_and_preserves_unrelated_files",
    ],
  };
  for (const [file, methods] of Object.entries(guards)) {
    const source = await Deno.readTextFile(new URL(`${file}.cs`, directory));
    const classDeclaration = source.indexOf(`public sealed class ${file}`);
    assert(classDeclaration >= 0, `Missing guard class ${file}`);
    assertEquals(
      source.slice(0, classDeclaration).includes(
        '[Trait("Category", "AuthoringIntegration")]',
      ),
      false,
      file,
    );
    for (const method of methods) {
      const declaration = new RegExp(
        `public\\s+(?:async\\s+)?(?:void|Task(?:<[^>]+>)?)\\s+${method}\\s*\\(`,
      ).exec(source);
      assert(declaration, `Missing guard declaration ${file}.${method}`);
      const prefix = source.slice(0, declaration.index);
      const attributes = /((?:[ \t]*\[[^\n]*\]\r?\n)+)[ \t]*$/.exec(prefix);
      assert(
        attributes,
        `Missing immediately preceding guard attributes ${file}.${method}`,
      );
      assert(/\[(Fact|Theory)\]/.test(attributes[1]!), method);
      assertEquals(
        attributes[1]!.includes('[Trait("Category", "AuthoringIntegration")]'),
        false,
        method,
      );
    }
  }
  for (
    const name of [
      "AuthoringWindowsTuiLauncherTests",
      "AuthoringProgressFileTests",
      "AuthoringOperationTests",
      "AuthoringCreationOwnershipTests",
      "Windows_case_alias",
      "Windows_directory_readonly",
    ]
  ) {
    assert(AUTHORING_WINDOWS_FILTER.includes(name), name);
  }
  assert(AUTHORING_UI_WINDOWS_FILTER.includes("AuthoringOperationWindowTests"));
});

Deno.test("reusable workflow preserves platform gates, required aggregates and real publish verification", async () => {
  const workflow = await Deno.readTextFile(
    new URL("../../.github/workflows/ci.yml", import.meta.url),
  );
  const suite = await Deno.readTextFile(
    new URL("../../.github/workflows/ci-suite.yml", import.meta.url),
  );
  for (const yaml of [workflow, suite]) {
    for (const match of yaml.matchAll(/uses:\s+([^\s]+)/g)) {
      assert(
        match[1]!.startsWith("./") || /@[a-f0-9]{40}$/.test(match[1]!),
        match[1],
      );
    }
  }
  for (const job of ["test", "test-linux"]) {
    const block = workflow.split(`\n  ${job}:\n`)[1]!.split(
      /\n  [\w-]+:\n/,
    )[0]!;
    assert(block.includes("if: always()"));
    assert(block.includes("profile.ts aggregate"));
    assert(block.includes("toJSON(needs)"));
  }
  assert(workflow.includes("suite: [host, ui, vm, quality]"));
  assert(workflow.includes("schedule:"));
  assert(
    workflow.includes(
      "cancel-in-progress: ${{ github.event_name == 'pull_request' }}",
    ),
  );
  assertEquals([...suite.matchAll(/main.ts format/g)].length, 1);
  assertEquals([...suite.matchAll(/main.ts audit/g)].length, 1);
  assertEquals([...suite.matchAll(/main.ts coverage/g)].length, 1);
  assertEquals(suite.includes("main.ts test:authoring-compat"), false);
  assertEquals(
    suite.includes("FullyQualifiedName~AuthoringProgressFileTests"),
    false,
  );
  assert(suite.includes("main.ts verify"));
  assert(suite.includes("if-no-files-found: error"));
  assert(suite.includes("if: always() && inputs.suite != 'publish'"));
  assert(suite.includes('main.ts test:e2e --rid "$CI_RID"\n')); // Windows is blocking.
  assert(suite.includes('main.ts test:e2e --rid "$CI_RID" --advisory-e2e'));
  const globalJson = JSON.parse(
    await Deno.readTextFile(new URL("../../global.json", import.meta.url)),
  );
  assert(suite.includes(`dotnet-version: "${globalJson.sdk.version}"`));
  for (
    const identity of [
      "DOTNET_INSTALL_DIR",
      "DOTNET_ROOT_X64",
      "(Get-Command dotnet).Source",
      "$actualVersion -ne $expectedVersion",
      "--list-sdks",
      "--list-runtimes",
    ]
  ) {
    assert(suite.includes(identity), identity);
  }
  const config = JSON.parse(
    await Deno.readTextFile(new URL("./deno.json", import.meta.url)),
  );
  for (const task of TASKS) {
    assertEquals(config.tasks[task], `deno run -A main.ts ${task}`);
  }
});
