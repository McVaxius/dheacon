# How To Import Dheacon

## Dev Plugin Path

`Z:\dheacon\dheacon\bin\x64\Debug\dheacon.dll`

## Add The Plugin In XIVLauncher

1. Launch FFXIV through XIVLauncher.
2. Open Dalamud settings with `/xlsettings`.
3. Go to `Experimental`.
4. Add `Z:\dheacon\dheacon\bin\x64\Debug\dheacon.dll` to `Dev Plugin Locations`.
5. Open the plugin installer with `/xlplugins`.
6. Go to `Dev Tools > Installed Dev Plugins`.
7. Enable `Dheacon`.
8. Run `/dheacon` in game to open the main window.
9. Use `/dheacon config` if you want the settings window directly.

## First Checks

- Confirm the installer copy reads **"Local TTS commentary and classic transition alerts."**
- On a genuinely new configuration, confirm Settings opens on **Quick Setup** automatically.
- Close Quick Setup before finishing, reload the plugin, and confirm it opens again.
- Exercise classic-alert testing and spoken-commentary testing with an existing preset.
- Exercise Piper already-ready, Piper one-time install, retry/failure with Windows fallback, and direct Windows-default selection.
- Confirm Quick Setup requires a test attempt and an explicit enabled/disabled choice before finishing.
- Finish setup, reload the plugin, and confirm Quick Setup no longer opens automatically.
- Open Settings manually and confirm the permanent Quick Setup tab can be rerun.
- Confirm preset selection updates the DTR tooltip and optional Imaginary Fren state.
- Confirm migrated v11 settings remain unchanged and do not open Quick Setup automatically.
