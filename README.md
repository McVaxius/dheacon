# Dheacon

Local TTS commentary and classic transition alerts.

Dheacon brings both modes to FINAL FANTASY XIV.

Dheacon offers two operating modes:

- **Classic transition alerts** play the packaged alert sound for eligible territory changes while preserving teleport and Return suppression.
- **Spoken commentary** uses selectable presets for login, travel, combat, idle, BGM, nearby-player, duty, crafting, gathering, and other supported events.

## Install

Add the following custom repository URL in Dalamud Settings, then install Dheacon from the plugin installer:

```text
https://aethertek.io/x.json
```

Run `/dheacon` to open the main window or `/dheacon config` to open Settings.

## Quick Setup

New installations open the permanent **Quick Setup** tab automatically. It guides you through:

1. Choosing classic alerts or a spoken-commentary preset.
2. Preparing the recommended local Piper voice or selecting Windows-default speech.
3. Reviewing and testing the setup, then explicitly choosing whether to enable automatic triggers.

Closing Quick Setup before finishing leaves it incomplete, so it opens again on the next plugin load. Existing installations keep their settings and do not receive an unsolicited wizard. Quick Setup remains available in Settings whenever you want to rerun it.

## Speech and presets

Spoken audio is generated locally and cached as WAV files for reuse. Piper uses a one-time managed runtime download plus one-time downloads for selected voice models. Windows speech uses voices already available through Windows. If Piper preparation fails during Quick Setup, Dheacon selects Windows-default speech and leaves Piper available to retry.

Presets control the commentary style, event toggles, trigger chance, cooldowns, speech behavior, and optional line pack. The settings window also supports user preset duplication, import, export, and detailed Piper voice management.

Some presets can optionally request an **Imaginary Fren** follower through Krangler. The follower is local-only, Krangler is not required for Dheacon, and alerts or commentary continue when the integration is unavailable.

## Controls

- The Settings window controls presets, event triggers, cooldowns, speech backends, voices, cache limits, DTR display, and diagnostics.
- The DTR entry shows Dheacon's enabled state and active preset; click it to toggle the plugin.
- `/dheacon on` and `/dheacon off` change the enabled state directly.
- `/dheacon preset <name>` selects a preset.
- `/dheacon say <text>` queues a manual spoken line while spoken-commentary mode is enabled.

## Privacy and network use

Speech synthesis, playback, and caching happen locally. Network access is used only when Dheacon refreshes the Piper catalog or downloads a requested Piper runtime or voice. Optional Krangler communication uses Dalamud IPC on the local client.

## Support

[Aethertek plugins and guides](https://aethertek.io/) · [Support development on Ko-fi](https://ko-fi.com/mcvaxius)
