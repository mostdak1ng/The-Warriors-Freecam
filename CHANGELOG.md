# Changelog

## 0.1.5 — 2026-08-18

- Added an optional launcher setting that captures Pad 1 during
  keyboard/mouse Freecam. This prevents keyboard bindings configured as Pad 1
  in PCSX2 from moving the player while retaining normal mouse control.
- Confirmed core compatibility with PCSX2 2.6.3 stable.
- Documented borderless fullscreen or windowed mode as required for the
  external HUD; exclusive fullscreen cannot display desktop overlays.

## 0.1.4 — 2026-08-18

- Removed the controller deadzone value from the in-game HUD to keep the
  control help concise. The 10% deadzone remains unchanged and documented.

## 0.1.3 — 2026-08-18

- Changed controller movement modifiers to match the game's conventions:
  `L2` is fast and `R2` is precise.
- Moved vertical Freecam movement to `L1` down and `R1` up.
- Increased the controller stick radial deadzone from 5% to 10%.

## 0.1.2 — 2026-08-18

- Controller exit now keeps Pad 1 captured until both `Select` and `Start`
  are released, preventing `Start` from opening the game's pause menu.
- Returning to normal camera mode, or closing the program, now hands control
  to the player's live `follow` camera instead of leaving scripted fixed
  cameras at their original position after player carry.

## 0.1.1 — 2026-08-18

- Fixed controller input becoming permanently neutral after entering
  controller Freecam.
- Fixed magenta/colored halos around overlay text.
- Added complete vector values and better hook-ownership diagnostics to logs.

## 0.1.0 — 2026-08-18

- First public standalone release.
- Keyboard/mouse and Pad 1 Freecam control modes.
- Permanent camera and player coordinate overlay with version watermark.
- Player carry and reversible God mode.
- Borderless fullscreen and windowed viewport tracking.
- Reversible, fail-open native Pad 1 capture.
- Preflight validation, save-state warning, detailed local diagnostics, and
  clean shutdown.
