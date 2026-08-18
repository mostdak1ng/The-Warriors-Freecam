# Changelog

## 0.2.0 — 2026-08-18

- Added reversible world pause, off by default, scaling the native world
  timestep to 0.01%.
- Added `P` as the keyboard toggle in every active camera mode and
  `Select+Square` as the controller-Freecam toggle.
- Added the world-pause state and mode-appropriate hotkeys to the overlay.
- World pause restores the exact original timestep when toggled off, before a
  world transition, and on clean exit; it remains off after the new map loads.
- Standardized controller labels on the PS2 layout.

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
- Documented that PCSX2 must be restarted after enabling its PINE server.
- Documented exclusive-fullscreen overlay limitations as a known issue.
