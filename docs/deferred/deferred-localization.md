# Deferred — Localization

**Parent:** [platform-roadmap.md](../platform-roadmap.md)
**Status:** Deferred

## Goal

Ship a multi-language operator UI so benches can run with locale-appropriate labels, without breaking appliance constraints or source-generated config.

## Locked decisions

- English remains the development and fallback locale.
- Operator strings live in resource files / localization framework compatible with Avalonia; **no** OS message boxes.
- Do not localize golden TapPlan step names or OpenTAP plugin Display attributes in v1 (Editor language stays separate).
- Settings and provenance keys stay English (diagnostics for support).

## Workstreams

1. Choose Avalonia localization approach (resx / JSON / community toolkit) and wire startup culture from settings/env.
2. Extract operator-facing XAML + ViewModel Status strings.
3. Pseudo-locale test to catch truncation.
4. Document how product forks add a language pack.

## Exit criteria

- [ ] At least one non-English locale selectable in Settings (Engineer)
- [ ] Run board + session confirm + Safety Stop chrome localized
- [ ] Missing keys fall back to English without crashing

## Out of scope

- Translating Typst report templates (separate product content)
- RTL layout polish beyond what Avalonia gives for free
- Localizing OpenTAP engine / plugin metadata

## Dependencies

- Stable operator chrome (Phases 10–12)
