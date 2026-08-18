# Changelog

## 0.1.6 — 2026-08-18

- First public release.
- Added keyboard/mouse and Pad 1 Freecam control modes with a 10% controller
  deadzone.
- Added live camera/player coordinates, player carry, reversible God mode,
  and a permanent version watermark.
- Added reversible, fail-open native Pad 1 capture, including an optional
  launcher setting for PCSX2 keyboard mappings.
- Returning to normal camera mode now stops overriding the game's active
  camera by default instead of forcing the generic player FollowCamera. This
  preserves scripted and cutscene camera behavior.
- Added an opt-in launcher setting to return to the player FollowCamera for
  users who prefer it after relocating the player.
- Added preflight validation, clean shutdown, state restoration, and detailed
  local diagnostic logs.
- Confirmed support for PCSX2 stable and PCSX2 2.7.522 nightly.
- Clarified supported PCSX2 channels, loaded-map requirements, memory-card
  backup guidance, recommended emulator settings, and troubleshooting.
- Documented exclusive-fullscreen overlay limitations as a known issue.
