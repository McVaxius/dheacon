# Changelog

## 2026-10-06 - Hindi text rendering

- Add all 724 Hindi UI and displayed service-message entries alongside the fourteen existing languages, preserving native identities, saved locale positions, original font roles and opaque preset/voice values. Use Windows text shaping for visible text, captions, hints and editable values; keep authored game-rendered DTR text in English for Hindi. The Debug x64 build passes without warnings or errors; the focused Footballer/Dheacon probe passes 6,614 assertions with exact current library/resource bytes. Game, GPU, managed-host and IME acceptance remain separate.

## 2026-10-06 - Window appearance and transparency

- Add Window appearance settings for colour, compact mode, UI language and independent main-header visibility. Persist complete-window transparency, normal opacity (100%), automatic unfocused fade (50% after 10 seconds), and the main transparency toggle using the existing configuration. Retain speech presets, voice settings, actions, fonts, native IDs and original chrome hooks across Main, Mini, Settings and font status. Translate new labels in all fourteen UI languages. The unchanged local launcher builds successfully with zero warnings and errors. Native appearance/persistence checks and game acceptance remain pending.

## 2026-10-05 - Rounded outer window chrome

- Adopt shared rounded chrome and native minimize in Main, Mini, Settings and font status, retaining existing style/menu hooks, native IDs, constraints, saved geometry and actions. Native and game verification remain pending.


## 2026-10-02 - Build and release repair

- Pin GitHub builds to SDK 10.0.201 and pass the downloaded Dalamud library path. Restore and build plugin projects with matching configuration, platform and runtime; stop on restore failure.
- Keep build tokens read-only and release writes in a separate job. Use packaged manifest versions for untagged releases.
- Local launchers build the plugin directly in the pinned environment and return its exit status.

## Unreleased

- Match regular/compact Main pane and footer spacing to their approved references, right-align speech actions when they fit, and retain complete translated preset fields. Preserve native title geometry, menus, focus and control IDs.
- Restore Mini's approved title, labels, speech and separator positions; wrap speech to the visible pane and restore font/style state after drawing. Retain native close/collapse controls, saved configuration, voice behavior and version 2.1.0.3.
- Keep translated Settings tabs and Piper catalog columns at their measured widths with native scrolling. Preserve title bearings and button descenders, align the Main toolbar, and wrap explanatory text to the visible pane. Numeric editors retain the installed native binding's zero-step defaults.
- Restore approved action icons and compact status-panel styling. Keep translated controls and native editors readable with whole-control reflow and horizontal scrolling, retaining numeric steps and control identities.
- Preserve empty service arguments and their numbered identities during UI localization without altering raw names or formatted values.
- Adopt the approved AethertekUI Main and read-only Mini designs with shared regular/compact spacing, preserved native control identities and retained Settings/Quick Setup actions.
- Add fourteen embedded UI language resources, including Vietnamese, Brazilian Portuguese, Indonesian, Polish and Turkish, alongside managed Latin/Cyrillic/CJK/symbol fonts and header/Settings language and whole-theme colour selectors. UI language remains separate from voice selection and spoken content.
- Localize the DTR On/Off labels and tooltip through the existing appearance cache in all fourteen languages, retaining glyph modes, preset names and the click action.
- Keep configuration schema and plugin version unchanged, preserve existing release assets, and include the sibling AethertekUI library in the local and Actions build payload. In-game visual/glyph acceptance remains pending.

- Build only the plugin project in GitHub Actions so test and regression projects do not block production artifacts.

- Added a permanent three-step Quick Setup tab for mode/preset selection, local speech preparation with Windows fallback, required audio testing, and explicit enablement.
- Added first-run-only setup persistence through configuration schema v12 while preserving all migrated user settings.
- Documented preset descriptions and optional Imaginary Fren behavior in setup.
- Replaced inaccurate beacon/navigation copy across plugin metadata, public documentation, developer checks, and Aethertek pages.

## 2026-03-25

- Bootstrapped the `Dheacon` repository shell.
- Added the Dalamud project, solution, plugin manifest, windows, and DTR/Ko-fi baseline.
- Added icon assets at `images\iconHQ.png` and `images\icon.png`.
- Added the initial import guide and README.
