# Dheacon LCARS image prompts

## 01-main-regular-compact.png

```text
Create a Dheacon main LCARS concept sheet with regular and compact specimens of the same anonymous spoken-preset fixture. Keep equal readable body type, reducing gaps/padding in compact and reflowing source panels. Use near-black backgrounds and dark panel interiors, restrained orange/lilac/periwinkle LCARS elbows/segmented rails, blue control accents and dark outlined buttons. No white cards, gradients or glow. Native titlebars keep original actions and visible globe/language, monitor/Compact mode, droplet/colour, Krangle privacy and muted speaker shortcuts. Krangle obscures identifying text, never reveals hidden settings. Anonymous fixture data only.

Preserve actual Main structure: header, Enabled and DTR Bar checkboxes, Settings and Status to chat actions, Presets list beside preset detail, then source status summaries. No invented tab bar or spaceship navigation. The Presets list retains a protected item with [template] and a fabricated Sample commentary [user] row; selected Sample commentary is a spoken preset. Keep the list selectable, not an editable text block or a new cloud preset catalog.

The selected detail heading is Spoken preset. It has the existing Test speech action with play icon and Clear cache with delete icon, shown as explicit actions, not claimed as performed. Read-only fields are Speech backend, Voice, Trigger, Queue, Current text and Speech context. Use Piper local as backend, an ordinary sample voice label, Idle / 0 pending, Waiting and None values. No real cache path, user profile, player identity or actual speech transcript. Existing speech parameter summary can remain read-only; do not invent generic sliders on Main.

Classic transition alerts and Spoken commentary are existing Quick Setup choices, not simultaneous Main tabs. The alternate Classic alert preset uses packaged WAV and should not be combined with the spoken cache toolbar as one invented universal panel. Keep product speech Enabled distinct from the native muted speaker for optional LCARS UI tones. UI mute does not turn off commentary.

Compact preserves selected preset, state and actions at the same body size, stacking the list above detail if width requires it. Source contract, not literal screen text: dheacon/Windows/MainWindow.cs:111 header, :155 Presets, :224 classic state, :237 spoken state and :263 status. No claimed Piper installation, cache clear or audible test; this sheet describes appearance only.
```

## 02-settings-mini-and-auxiliary.png

```text
Create a Dheacon supporting-surface sheet with a full native Settings window, actual Mini speech monitor and a separate Appearance crop. Put specimen labels outside the app. Use near-black backgrounds and dark panel interiors, restrained orange/lilac/periwinkle LCARS elbows/segmented rails, blue control accents and dark outlined buttons. No white cards, gradients or glow. Native titlebars keep original actions and visible globe/language, monitor/Compact mode, droplet/colour, Krangle privacy and muted speaker shortcuts. Krangle obscures identifying text, never reveals hidden settings. Anonymous fixture data only.

The Settings owner retains complete tabs Quick Setup, General, Speech, Piper Voices, Window appearance, Diagnostics. Select Quick Setup at its existing speech-choice step. Show radio choices Recommended local Piper and Windows default speech, with their real Back and Next: Test and finish actions. Prepare Piper / Retry Piper and Use Windows default instead appear only in their applicable readiness state; select one neutral not-prepared fixture rather than inventing successful installation. This is an existing wizard step, not a new service manager. Classic transition alerts / Spoken commentary belong to the preceding setup step and may be a small separate labelled crop, not simultaneous tabs.

Appearance shows ordered radios Auto / Picker / LCARS and original language/compact/opacity controls. Proposed UI tones are off, bound to the muted native titlebar speaker. There is no new UI volume or sound pack. Existing speech/output gain preferences remain in the product's Speech page and are not confused with UI feedback.

The real Mini is titled Dheacon Mini, with read-only Active preset and Last speech. Use Sample commentary and “No speech yet.” in a quiet idle state. Preserve its native title actions for main speech settings, Settings and Enabled; product Enabled is separate from UI-sound mute. Do not invent a playback timeline, volume strip, chatbot conversation or synthesized paragraph on this small monitor.

General, Speech, Piper Voices, Diagnostics and preset import/editor controls retain their source layouts, explicit install/remove gates and original functions. No runtime install, voice download or test is represented as performed. Source evidence, not screen text: dheacon/Windows/ConfigWindow.cs:88 owner tabs, :244 speech wizard, :575 Speech; MiniWindow.cs:120 preset and :131 speech. Dark LCARS rails stay outside readable content.
```


## 03-mini-regular-compact.png — dedicated Mini continuation, 2026-10-23

Built-in image generation; new image, no CLI or image-editing script. The dedicated regular/compact sheet clearly exposes the complete two-field speech monitor and its original Main/Settings/Enabled action strip.

Source evidence: `dheacon/Windows/MiniWindow.cs:29`–`:45` define Open Main, Settings and product Enabled title actions; `:120` Active preset and `:131` Last speech fields.

```text
Use case: ui-mockup. Create a practical source-faithful LCARS review sheet for the EXISTING Dheacon Mini speech monitor. One complete REGULAR native plugin Mini window left, one complete COMPACT Mini right, on a restrained dark neutral surround. Both show the SAME readable body text size; compact means reduced padding/rails and a naturally smaller window, not smaller text. Use approximately 2000x1000 landscape sheet, with Minis each around 760x390 and 650x330, centered, enough room for clear title actions. Flat black #07090D, orange #FFAA66 LCARS rounded elbows and segmented rails, lilac #CC99CC bars, periwinkle #9999FF, warm off-white body text, dark outlined native controls. No glow, gradients, tiny ornamental numbers, sci-fi scenery or dashboard navigation.
Native titlebar reads "Dheacon Mini" on regular and "Dheacon Mini — speech monitor" on compact. RETAIN three original title actions: open-main/window icon, Settings gear, Enabled/power icon ON. Also include the proposed shared appearance colour, language/globe, compact/monitor, transparency/droplet, privacy and MUTED-speaker icons as small separate controls, with standard native close/collapse. Leave enough width for this real action strip.
The body is intentionally SIMPLE and contains only the actual speech-monitor information, plus restrained LCARS chrome:
A small readable brand line "DHEACON MINI" beside a short orange elbow and lilac/periwinkle segmented rail.
First section exact label "Active preset", with value "Example preset" on its own line.
A subtle separator.
Second section exact label "Last speech", with fabricated sample sentence "Welcome to the duty." wrapped naturally if needed.
That is the COMPLETE Mini body. No speech queue list, Play/Stop buttons, audio waveform, Run switch, voice picker, volume slider, rank, role, progress bar, generic status table, sidebar or extra settings. Settings and product Enabled remain in titlebar. The muted speaker represents OPTIONAL LCARS click tones only and does not imply product speech is disabled: preserve this distinction. Do not add an explanatory paragraph inside the product body. The only captions outside app windows are "REGULAR" and "COMPACT" and a small neutral line "Design concept · same readable text". Show complete unclipped windows with sufficient body text contrast. Fabricated sample data only; static design art, not runtime evidence.
```

Observed limits: Product speech remains separate from the illustrated muted UI-tone shortcut. Exact titlebar geometry, font pixels and scale still require the consumer's native checks; raster variants are readability proposals, not native acceptance.
