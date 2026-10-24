# Dheacon LCARS concepts

Approved LCARS design concepts. Implementation is selected for the repositories in Z:\myrepos.txt, with the rollout tracked in Z:\!todo\2026-10-22GOATFUCK0002.md. These images are design references, not runtime or test evidence.

- [Main: regular and compact](01-main-regular-compact.png)
- [Settings, Mini and supporting surfaces](02-settings-mini-and-auxiliary.png)
- [Dedicated speech-monitor Mini: regular and compact](03-mini-regular-compact.png)

The second sheet depicts selected existing supporting surfaces. Other source windows retain their established layout and actions under the proposed shared appearance; this is not a claim that every scroll position or conditional state has a separate image. Browser companions retain browser controls. Main/Mini windows are distinct from density variants. Generated text and examples are design material, not runtime evidence.

## Source scope

Source commit: `b17afc4b84ec4a23fb1b845286cdbe8de1f8bf7f`. Existing dirty paths were inspected by count (0); none are included or changed by this mockup task.

## Source-verified control contract

- `dheacon/Windows/MainWindow.cs:111` defines Enabled, DTR Bar, Settings and Status to chat. `:155` draws actual Presets list with [template]/[user] suffixes and selected preset detail; Main has no invented mode tabs.
- `MainWindow.cs:224` classic mode shows Classic alert preset and packaged-WAV explanation. `:237` spoken mode shows Spoken preset, Test speech and Clear cache. `:263` read-only Speech backend / Voice / Trigger / Queue / Current text / Speech context are status fields, not six dropdowns.
- `ConfigWindow.cs:88` defines Quick Setup, General, Speech, Piper Voices, Window appearance, Diagnostics. Quick Setup Classic transition alerts / Spoken commentary choices (`:178`) are wizard controls, not Main tabs. Speech controls have existing backend/voice/cache semantics (`:575`).
- `MiniWindow.cs:120` shows Active preset and Last speech, with No speech yet. in the idle fixture. Native title actions open Main/Settings and toggle product Enabled; this is distinct from UI-tone mute.

Selected supporting surfaces are full Settings navigation with Quick Setup and Appearance crops, and actual speech-monitor Mini. General, Speech, Piper Voices, Diagnostics, preset editors/import, runtime install and adapter controls retain source layout and gates. Product speech/output gain settings are not rebranded as LCARS UI volume. The proposed LCARS feedback mute never disables commentary.

## Existing UI source inventory

- `dheacon/Windows/ConfigWindow.cs`
- `dheacon/Windows/MainWindow.cs`
- `dheacon/Windows/MiniWindow.cs`

## Shared appearance contract

Auto and Picker retain their order and values; LCARS is the third option. Preserve native IDs, original actions, saved scope and semantic status colours. The proposed LCARS UI tones default off; the speaker and Settings preference are the same value, with no added volume or pack selector. Krangle obfuscates identifying text. Actual adoption waits for the FrenRider pilot.

Exact generation prompts are in [PROMPTS.md](PROMPTS.md).






## Dedicated Mini review — 2026-10-23

[03-mini-regular-compact.png](03-mini-regular-compact.png) is the dedicated, source-checked Mini sheet generated with the built-in image tool. The dedicated regular/compact sheet clearly exposes the complete two-field speech monitor and its original Main/Settings/Enabled action strip.

Source evidence: `dheacon/Windows/MiniWindow.cs:29`–`:45` define Open Main, Settings and product Enabled title actions; `:120` Active preset and `:131` Last speech fields.

Raster limits: Product speech remains separate from the illustrated muted UI-tone shortcut. Exact titlebar geometry, font pixels and scale still require the consumer's native checks; raster variants are readability proposals, not native acceptance. The regular/compact comparison changes spacing; implementation must retain the same readable body typography. This artwork is not runtime or game/GPU evidence.
