# Dheacon UI/UX Recommendations

**Review date:** 2026-08-18  
**Scope:** UI code review only; no runtime behaviour or implementation changes are included in this document.

## Product goal

Choose an alert/commentary personality, prepare local speech, hear a successful test, and understand what is speaking or blocking playback.

## Reviewed surfaces

- `dheacon/Windows/MainWindow.cs`
- `dheacon/Windows/ConfigWindow.cs`
- `dheacon/Windows/MiniWindow.cs`

## What is already working

- Quick Setup cleanly separates operating mode, speech backend, and test/finish.
- Piper preparation exposes progress, retry, and a Windows speech fallback.
- Presets, cached local speech, diagnostics, and optional Krangler integration are all represented.

## Prioritized recommendations

| Priority | Recommendation | Rationale and completion signal |
| --- | --- | --- |
| P0 | Make the main window mode-specific. | Classic alerts should show transition status and Test alert; Spoken commentary should show current preset, voice, queue, and Test speech. Hide irrelevant backend details for the inactive mode. |
| P0 | End Quick Setup with proof of success. | Require or strongly encourage a successful audible test, show the selected voice/preset summary, and make the final enable choice explicit. |
| P0 | Convert speech failures into actions. | Pair runtime, voice, cache, and queue errors with Retry, Prepare Piper, Choose another voice, or Use Windows default. |
| P1 | Reduce path and diagnostics noise in normal use. | Cache folder, resolved WAV path, pitch-shift internals, BGM probe state, and trigger-decision traces should live only in Diagnostics or copyable issue data. |
| P1 | Improve preset selection context. | Show mode, description, imaginary-Fren dependency, and a short sample line before a preset becomes active. |
| P1 | Make the Piper catalog task-oriented. | Default to recommended and installed voices, retain search/filter state, use clear Downloading/Installed/Selected badges, and keep actions in a consistent final column. |
| P2 | Give the mini window a useful state. | Show Speaking/Queued/Idle and provide a Stop speech action when playback is active. |

## Suggested information hierarchy

1. Active mode and preset
2. Test/playback state
3. Simple enable controls
4. Voice and preset setup
5. Diagnostics

## Validation checklist

- A new user can identify the primary action and current blocker within five seconds.
- Every disabled control has a nearby plain-language reason and, when possible, a direct corrective action.
- Healthy, warning, error, running, and disabled states remain distinguishable without colour.
- The UI remains usable at narrow window widths and common Dalamud UI scales without clipped labels or unreachable controls.
- Destructive, global, or high-impact actions identify their scope and require confirmation or provide a safe undo.
- Empty, loading, stale-data, success, partial-success, and failure states each provide an appropriate next action.
- Settings clearly identify whether they apply globally, per account, per character, per preset, or only for the current session.
- Advanced diagnostics are still reachable but do not compete with the everyday workflow.

## Recommended implementation order

1. Implement P0 items and validate the primary workflow plus blocker recovery.
2. Implement P1 information-architecture and configuration improvements.
3. Apply P2 polish, then test at multiple UI scales with both fresh and mature configurations.
