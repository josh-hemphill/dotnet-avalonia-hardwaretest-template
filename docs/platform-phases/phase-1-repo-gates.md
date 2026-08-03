# Phase 1 — Repo gates + green CI

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** —
**Unblocks:** every other phase — no quality gate in this repo has ever executed
**Status:** In progress — local gates landed; awaiting green Actions run after push / `workflow_dispatch`.

## Goal

Make the existing CI workflow actually run, and make it pass. Add the licensing and line-ending files the repo is missing.

## Why this is first

[`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) triggers on `push` to `main`/`master` and on `pull_request`. This repo's default branch is **`latest`**, all work lives on `cursor/opentap-ux-and-scottplot-reports`, and no PR has ever been opened. `gh run list` returns nothing: the build, three test suites, coverage floors, and publish smoke have **never executed once** across 14 commits.

Two tests are red as a result, one of them a real adopter-facing regression.

## Locked rules

- No new gates in this phase. Make the *existing* ones run before adding more.
- Fix the tests by fixing the product where the product is wrong — do not delete or weaken an assertion to get green.

## Work items

1. **`.gitattributes`** — `.editorconfig` already declares `end_of_line = lf`, but git has no normalization rule, so every file operation emits CRLF warnings and a Linux runner will see spurious diffs.

   ```gitattributes
   * text=auto eol=lf
   *.png binary
   *.pdf binary
   *.TapPlan text eol=lf
   ```

   Follow with a one-time `git add --renormalize .` in its own commit so the reformatting does not contaminate a feature diff.

2. **`LICENSE`** — MIT, current year, project author. Add the SPDX id to each `.csproj` (`<PackageLicenseExpression>MIT</PackageLicenseExpression>`) and a license line to `README.md`.

3. **CI triggers** — run on integration-branch pushes and on every PR; avoid double runs:

   ```yaml
   on:
     push:
       branches: [latest, main, master]
     pull_request:
     workflow_dispatch:
   ```

   Feature branches (`cursor/**`, etc.) get CI via `pull_request` only. That prevents the same commit from running once for `push` and again for `pull_request` when a PR is open. `workflow_dispatch` still lets you verify a branch without inventing a commit or opening a PR.

4. **Fix the Typst template override regression.** `TypstReportServiceTests.CompileTemplateAsync_uses_DataDirectory_reports_override` fails because [`ResolveTemplateName`](../../src/HardwareTest.Core/Reporting/TypstReportService.cs) explicitly refuses to honor `ReportTemplateName` when its value is `test-report.typ` — which is its **default** value, and the exact filename [adapting.md §7](../adapting.md#7-reports-typst) tells adopters to drop into `{DataDirectory}/reports/`. The `InvalidOperationException` fallback never fires either, because `status-report.typ` is now embedded and always resolves.

   Decide one of:
   - Keep `test-report.typ` as the status template name (default unchanged, docs unchanged), and let `status-report.typ` be an alias; or
   - Move the default to `status-report.typ` and update `AppSettings.ReportTemplateName`, [adapting.md §7](../adapting.md#7-reports-typst), and the migration note together.

   Either way a `{DataDirectory}/reports/` override of the status template must win. Keep the test.

5. **Fix the stale Instruments E2E assertion.** `InstrumentsE2ETests` asserts `item.Subtitle == item.Resource`, but `DiscoveredResourceItem.Subtitle` became a composed display string (`MOCK · INSTR0`) in commit `38058cc`. The product is right and the test is stale — assert on `Resource` directly and add a separate assertion that `Subtitle` contains the interface hint.

6. **Commit the working tree.** ~1,500 changed lines and seven untracked files, including `Features/Presentation/` and `PresentationRoleMapTests.cs`. Untracked tests cannot fail CI and untracked source is not backed up.

7. **Format check** — add a non-blocking `dotnet format --verify-no-changes` step. Start it as `continue-on-error: true`, fix the backlog, then make it blocking in a follow-up. Blocking it on day one turns Phase 1 into a whitespace project.

8. **NuGet caching** — `actions/setup-dotnet` with `cache: true` and a `packages.lock.json`-or-csproj cache key. Cheap, and the OpenTAP + Avalonia restore is not small.

## Exit criteria

- [ ] A CI run is visible in the Actions tab and is green.
- [x] `dotnet test dirs.proj -r win-x64` is green locally with zero failures.
- [ ] `git status` is clean; no untracked source or test files.
- [x] `LICENSE` exists and `README.md` states MIT.
- [x] A fresh clone on Linux produces no CRLF warnings (`.gitattributes` + `.editorconfig` `eol=lf`).

## Out of scope

- Linux CI matrix and containers (Phase 7).
- Architecture rules (Phase 2).
- Release tagging, artifact signing, dependency scanning — after the basic loop is trusted.
