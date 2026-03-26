# Dheacon

Dalamud plugin workspace for `Dheacon`.

## Current Status

Bootstrap scaffold created on 2026-03-25. This repo now has a buildable `Debug x64` shell with command routing, Ko-fi placement, DTR support, icon assets, and repo-ready documentation.

- Solution: `Z:\dheacon\dheacon.sln`
- Project: `Z:\dheacon\dheacon\dheacon.csproj`
- Command: `/dheacon`
- Repository target: `Public`

## Plugin Concept

- Detect aetheryte-use once.
- Route through a local playback service.
- Keep audio optional and testable.

## Planned Services

- AetheryteTriggerService
- AudioPlaybackService

## Documents

- Project plan: `Z:\xa-xiv-docs\Dhog\dheacon\DHEACON_PROJECT_PLAN.md`
- Knowledge base: `Z:\xa-xiv-docs\Dhog\dheacon\DHEACON_KNOWLEDGE_BASE.md`
- Import guide: `how to import plugins.md`
- Changelog: `CHANGELOG.md`

## Notes

- Icon assets live in `images\iconHQ.png` and `images\icon.png`.
- SamplePlugin references used for the initial shell: https://github.com/goatcorp/SamplePlugin and https://github.com/goatcorp/SamplePlugin/blob/master/README.md
