# Phase 2 — Architecture compliance tests

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Depends on:** [Phase 1](phase-1-repo-gates.md) (a rule nobody runs is not a rule)
**Unblocks:** safe refactoring in Phase 9; confident adopter forks
**Status:** Done

## Goal

Turn the layering rules that currently exist only in prose into a handful of fast, executable smoke tests.

The separation claims in [README](../../README.md) and [opentap-platform.md](../opentap-platform.md) are real today — I verified `HardwareTest.Core` has zero Avalonia packages and `HardwareTest.OpenTap.Host` references only OpenTAP. But nothing enforces it. One `PackageReference` line would break the central architectural promise of the template with no test failing and no warning emitted.

## Locked rules

- **Smoke level, not a rule engine.** Under a dozen assertions. If a rule needs a paragraph to explain, it belongs in docs, not here.
- **No new test dependencies.** `Assembly.GetReferencedAssemblies()` and `Type` reflection cover every rule below. Skip NetArchTest and friends.
- Each assertion's failure message must name the rule *and* the doc that states it, so the fix is obvious to someone who has not read this file.

## Work items

1. **New project** `tests/HardwareTest.Architecture.Tests` referencing all five `src/` projects. It needs the references in order to load the assemblies; that is the one place a project may reach across every layer.

2. **Assembly reference rules** — walk `GetReferencedAssemblies()` transitively from each target assembly:

   | Assembly | Must not reference | Rule source |
   | --- | --- | --- |
   | `HardwareTest.Core` | `Avalonia*` | [README](../../README.md) hard separation |
   | `HardwareTest.Core` | `OpenTap*` | Core is the test-executive-agnostic layer |
   | `HardwareTest.OpenTap.Host` | `Avalonia*` | [README](../../README.md) hard separation |
   | `HardwareTest.OpenTap.Plugins.Basic` | `Avalonia*`, `ScottPlot*` | [Phase I locked rule](../opentap-phases/phase-i-presentation-contract.md#locked-rules) |
   | `HardwareTest.OpenTap.Plugins.Mixins` | `Avalonia*`, `ScottPlot*` | [Phase I locked rule](../opentap-phases/phase-i-presentation-contract.md#locked-rules) |
   | *(any)* | `System.Windows.Forms`, `PresentationFramework` | appliance no-dialog rule |

3. **No second top-level window.** Assert that the only type in `HardwareTest` assignable to `Avalonia.Controls.Window` is `MainWindow`. This is the executable form of the [forbidden-on-appliance list](../opentap-platform.md#interaction-contract-avalonia-owned) — operator flow must stay in-panel. Allowlist by name so adding a legitimate second window is a deliberate, reviewed edit to this test.

4. **Core public surface is OpenTAP-free.** Walk exported types of `HardwareTest.Core` and assert no public member signature mentions an `OpenTap` type. Catches the subtle version of rule 2, where a transitive type leaks through a shared model.

5. **Reflection-free serialization holds.** Assert `HardwareTest.Core` and `HardwareTest` do not carry `RequiresUnreferencedCode`-triggering `JsonSerializer` overload usage — or, more cheaply and reliably, assert every type persisted to disk appears in [`AppJsonContext`](../../src/HardwareTest.Core/Serialization/AppJsonContext.cs). `JsonSerializerIsReflectionEnabledByDefault=false` means a missed registration is a **runtime** failure on an operator's bench, which is the worst place to find it.

6. **Wire into CI** as a fourth labeled test step in [`ci.yml`](../../.github/workflows/ci.yml), and document the suite in [testing.md](../testing.md) alongside the existing three.

## Exit criteria

- [x] Adding `<PackageReference Include="Avalonia" />` to `HardwareTest.Core.csproj` fails a test with a message naming the rule.
- [x] Adding a second `Window` subclass to the app fails a test.
- [x] Adding a persisted type without registering it in `AppJsonContext` fails a test.
- [x] The whole suite runs in under two seconds.
- [x] [testing.md](../testing.md) documents when a rule belongs here versus in a normal unit test.

## Out of scope

- Namespace/folder conventions, naming rules, cyclomatic limits — `.editorconfig` and review own those.
- Enforcing the `IOpenTapSession` façade boundary at the call-site level (Phase 8 pins behavior instead).
- Dependency-direction rules inside a single assembly.
