# The Warriors Freecam

Standalone Freecam, coordinate display, player carry, and God mode for the
USA release of *The Warriors* running in PCSX2. The public v0.1.5 build is a
portable Windows x64 executable: it does not require Python or a separately
installed .NET runtime.

Every official screenshot keeps this visible in the lower-right corner:

`Freecam mod by mostdak1ng v0.1.5`

The watermark cannot be hidden in the official build, even when the rest of
the HUD is hidden. It identifies the exact version when reporting a bug.

## Requirements

- Windows 10 or Windows 11, x64.
- PCSX2 with its PINE server reachable on the default TCP port `28011`.
- *The Warriors* USA: serial `SLUS-21215`, game version `1.03`, CRC
  `B99A75DE`.
- PCSX2 2.6.3 stable and PCSX2 2.7.522 nightly are tested.
- Borderless fullscreen is the primary tested display mode. Windowed mode is
  supported. Switching between them while the mod is running should work.
- Exclusive fullscreen can run the Freecam but cannot display its external
  HUD or permanent watermark. Use borderless fullscreen or windowed mode.

No administrator rights, Python installation, or external controller library
is required.

## Critical save-state warning

1. Create a backup save state **before** starting the mod.
2. Do not create or load a save state while the mod is running.
3. Exit cleanly with `F10`, or hold `Select+Start` for 1.5 seconds while in
   controller mode and then release both buttons when prompted.

A save state made while the mod is active can contain temporary executable
patches and modified player state. Loading any save state while active can
also replace the memory that the program owns. The launcher requires explicit
confirmation of this warning before it will start.

The program removes its native input hook and restores the player's original
God/no-target bits on a normal exit. It intentionally does **not** move the
player back to its starting position and does not load a save state
automatically. Returning to normal mode hands camera control to the player's
live follow camera.

## Running

1. Start PCSX2 and load supported gameplay.
2. Create the backup save state.
3. Run `TheWarriorsFreecam.exe`.
4. Wait for the green preflight result, acknowledge the warning, and choose
   **Start Freecam**.
5. Click the game window if keyboard/mouse input is paused because PCSX2 is
   not focused.

The session starts with keyboard/mouse Freecam active, player carry off, HUD
visible, and God mode on. By default, Pad 1 continues to control the game until
controller Freecam is selected.

If PCSX2 maps Pad 1 to keyboard keys such as `W`, `A`, `S`, and `D`, enable
**Capture Pad 1 in keyboard/mouse Freecam** in the launcher. Keyboard and mouse
still control the Freecam through Windows, while the game receives a neutral
Pad 1. Normal-camera mode releases Pad 1 automatically. The option is off by
default so a physical controller can continue controlling the game alongside
keyboard/mouse Freecam.

## Keyboard and mouse controls

| Input | Action |
| --- | --- |
| Mouse | Look |
| `W` `A` `S` `D` | Move |
| `Q` / `E` | Move down / up |
| `Shift` | Fast movement (4×) |
| `Ctrl` | Precise movement (0.25×) |
| `V` | Toggle keyboard/mouse Freecam and the normal game camera |
| `F8` | Toggle player carry |
| `G` | Toggle God mode |
| `R` | Hide/show the HUD; the version watermark remains visible |
| `F10` | Clean exit |

## Controller controls (Pad 1)

Controller Freecam uses a 10% radial deadzone.

| Input | Action |
| --- | --- |
| `Select+L3` | Enter controller Freecam; press again for normal camera |
| Left stick | Move |
| Right stick | Look |
| `L1` / `R1` | Move down / up |
| `L2` | Fast movement (4×) |
| `R2` | Precise movement (0.25×) |
| `Select+R3` | Toggle player carry |
| `Select+Circle` / `Select+B` | Toggle God mode |
| `Select+Triangle` / `Select+Y` | Hide/show the HUD |
| Hold `Select+Start` for 1.5 seconds, then release both | Clean exit without leaking `Start` to the game |

Entering with `Select+L3` captures Pad 1 so it controls only the Freecam.
Leaving controller mode hands control to the player's follow camera and then
returns Pad 1 to the game. Entering with `V` selects keyboard/mouse Freecam
and leaves Pad 1 available to the game.

The native Pad 1 capture has a guest-side timeout. If the host program stops
refreshing it unexpectedly, controller input fails open and returns to the
game automatically in about one second of active gameplay.

## Player carry and God mode

Carry starts off. When enabled during either Freecam mode, the player is held
slightly in front of and below the camera. Carry is released in normal-camera
mode and during loading screens; the preference resumes after gameplay returns.

God mode starts on. Toggling it changes only the game's known God bit. On a
normal exit, the exact God/no-target state that the current player entity had
before the mod touched it is restored. This is normally God mode off.

## Overlay

The overlay shows:

- active control mode and status;
- camera X/Y/Z coordinates;
- player X/Y/Z coordinates;
- carry, God mode, and HUD state;
- current controls; and
- the permanent version watermark.

It tracks the PCSX2 rendering viewport when switching between borderless
fullscreen and windowed mode. It is click-through and hides when the game is
minimized or loses focus.

## Logs and bug reports

Each run creates a JSON-lines log in `logs` beside the executable. If that
location is not writable, it uses:

`%LOCALAPPDATA%\TheWarriorsFreecam\logs`

Use **Open Logs** in the launcher. Attach the newest log and a screenshot when
reporting a bug; the screenshot should include the permanent watermark.

Logs are never uploaded automatically. They deliberately include detailed
diagnostic data such as app and emulator versions, executable and configuration
paths, Windows user/domain and machine names, display/window data, game object
addresses, coordinates, Pad 1 state, timings, transitions, and exception
details. Review the file yourself before sharing it if any of that information
is sensitive to you.

## Troubleshooting

- **PINE is not reachable:** start PCSX2, enable its PINE server, use port
  `28011`, load the game, and choose **Recheck**.
- **Unsupported executable:** use `SLUS-21215` v1.03 with CRC `B99A75DE`.
- **Another hook is active:** close MapTriggers and other PINE/native tools,
  then restart the game before using this standalone mod.
- **Windows SmartScreen appears:** v0.1.5 is not code-signed. Verify the SHA-256
  checksum distributed with the release before running it.
- **The overlay is hidden:** restore and focus the PCSX2 game window. If PCSX2
  is using exclusive fullscreen, switch it to borderless fullscreen or
  windowed mode.
- **A save state was loaded accidentally:** exit the mod, close PCSX2 without
  making another save state, restart it, and load the backup created before
  the session.

## Building from source

Install the .NET 8 SDK on Windows x64, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

The script runs the dependency-free test executable, publishes a self-contained
single-file Win64 app, creates the binary and Corresponding Source archives,
and writes SHA-256 checksums under `artifacts`.

The v0.1.5 release build is pinned to .NET runtime 8.0.15 so its included
third-party notices match the runtime payload.

## License

Copyright (C) 2026 mostdak1ng.

The Freecam source is licensed under GNU GPL version 3 only. See `LICENSE`.
Official binary distributions must accompany the complete Corresponding Source,
including the build script and license. The self-contained .NET runtime remains
under its own terms listed in `THIRD-PARTY-NOTICES.txt`.

GPLv3 permits inspection, modification, redistribution, and forks under its
terms. A modified build can therefore change the source-level branding; it
must not be represented as an unmodified official v0.1.5 binary.
