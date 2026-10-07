# Valuable review follow-ups

These items remain in the combined implementation plan. Previously reported blocking findings were fixed and reviewed again; they are not listed here as outstanding work.

| Area | Follow-up | Planned destination |
| --- | --- | --- |
| Traversal prerequisite | Compare project coverage against evaluated MSBuild references as conditions, exclusions, and removals evolve; exclude generated bin/obj projects explicitly. | CI and traversal maintenance |
| UI regression foundation | Guard standalone no-build test tasks against missing built assets or empty test execution. The current workflow builds the traversal first. | CI maintenance |
| UI regression foundation | Extend control bounds checks to the sequence, inspector, and preview surfaces at supported window sizes. | Packages 11 and 17 |
| Packaging protection | Exercise a real external TUI process rather than only managed-payload and in-process catalog/roundtrip fixtures. | Packages 09 and 10 |
| Packaging protection | Define dependency identity and containment for resolved symlinks when capturing a selected OpenTAP home. | Package 09 |
| Packaging protection | Add a successful GUI packaging case checking rendered home details, excluded plans, and retained results. | Packages 09 and 18 |
| Exception-safe persistence | Fault-inject rollback restoration failure, retain durable backups, and test interruption between file replacements. Current protection covers exceptions, not process crashes. | Package 06 |
| Saving and lifecycle | Recover pending invalid editor text along with committed edits after a crash. | Package 06 |
| Saving and lifecycle | Measure modal header/footer space for larger fonts and localization; extend native folder-picker focus and operating-system close coverage on Windows and Linux. | Packages 11 and 22 |
| Destructive scope | Preserve actual imported legacy algorithm instrument bindings, including non-first instruments, before offering more permissive removal. Current removal blocks unresolved bindings. | Packages 07 and 08 |
| Destructive scope | Expand isolated stale-impact mutations for expression inputs/sources, transfer coefficients/methods, limits/history, metric metadata, setup fields, cleanup policies, and instrument metadata. | Packages 05 and 08 |
| Destructive scope | Name default-report fallback and program-kind reset in each affected program's catalog-deletion preview. | Package 14 |
| Destructive scope | Recheck owner/session after asynchronous custom lifecycle chooser and workspace picker results. Actual modal hiding cancels safely; an injected pending chooser can currently return Save All after owner hiding. | Package 22 |

New test plan initialization remains in packages 19 and 20 and follows the shared authoring/document infrastructure. It is not part of this initial protection milestone.
