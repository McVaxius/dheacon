2026-10-08 - GitHub Actions shared-library repair

- Build against published AethertekUI main so current shared APIs are available. Retain repository-specific read-only SSH deploy keys, which do not expire, and disabled credential persistence. Publish library APIs before consumer changes.

# Changelog

## Unreleased - Hindi font availability and recovery

- Treat the Hindi language-menu caption as optional while retaining mandatory selected/English catalogue checks. Refresh the stable Hindi option's availability with the existing font generation; unavailable captions use disabled ASCII `Hindi (unavailable)` without blocking ordinary languages.
- Keep font-failure status readable in ASCII and offer an explicit Use English action for selected Hindi through the existing configuration save route. Retain atlas roles, merges, dimensions and native control IDs.
- Source integration is complete; compilation, native availability/recovery checks and Linux/Wine acceptance remain pending.

## Unreleased - Original plugin images and UI guidance

- Replace Main's drawn emblem with the existing embedded plugin icon and add original-colour images to Main/Mini titles, including collapsed windows. Retain existing body geometry, title text, native actions and saved window placement; borrowed host textures keep aspect ratio and a blank reservation while unavailable.
- Clarify shared appearance controls and this plugin's existing automation/setup ownership in the README.
- Source integration is complete; compilation, native image/title/control checks and game acceptance remain pending.

## Unreleased - Button sizing

- Use local Toolbar metrics for ordinary and icon buttons, growing for the active font and original icons with narrower side padding and retained native labels and actions.
- Current Debug/x64 compilation passes. Final actual-product native checks pass 9714 assertions across 32 focused scenes and 112 pointer activations, with integer exit 0 in all 2 routes. Coverage uses English/Hindi captions, original exercised font roles, both densities, 100/150 percent scale and enlarged text; game/GPU acceptance remains separate.

## Unreleased - Managed CJK font atlas

- Merge one bundled CJK face per font role, selecting the active language's regional forms. Set both managed atlas dimensions to 4096 on every rebuild; preserve font heights, required glyph ranges, symbol merges and host-language coverage.
- Current compilation and guarded production callback/rebuild checks pass, together with bounded native glyph checks for the checked text. Managed-host readiness, complete displayed glyph coverage, language/scale host rebuilds and game/GPU acceptance remain unverified.

## Unreleased - Native titlebar shortcuts

- Add Settings, Mini and Enabled shortcuts to Main, plus Main, Settings and Enabled to Mini. Share the retained enable action's configuration save, DTR update and Krangler follower reconciliation; keep the complete speech monitor and every body control.
- Reserve native buttons and translated titles before motion, including Mini's original larger painted heading. Preserve window identities, geometry, font roles and release version. The unchanged dheacon.bat passes Debug/x64 with zero warnings/errors.
- Focused English checks pass 197 consumer assertions plus shared native-cell checks, 64 installed-host pointer presses and 48 inert non-left callbacks across Main/Mini, both densities, 100%/150% scales and collapsed/expanded owners. Verify exact existing saves, DTR updates and follower reconciliation against supplied IPC responses, plus the actual retained Mini heading and title-button clearance. The 4096x4096 atlas completes within 60 seconds/768 MiB. Real speech, managed icon-font readiness, GPU and game acceptance remain separate.

## 2026-10-06 - Actions dependency revision

- Pin the existing AethertekUI checkout to published revision `6c193cf06ac67f954c549cafc2033ac0efdd630a`, which includes the Hindi text host required by this plugin. The preceding Actions run checked out the library before those APIs were published; local compilation alone did not establish runner compatibility.

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
