// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

using System.Diagnostics;
using System.Numerics;
using System.Net.Sockets;

namespace TheWarriorsFreecam;

internal sealed class SessionController : IDisposable
{
    private const float BaseMovementSpeed = 8f;
    private const float MouseDegreesPerPixel = 0.14f;
    private const float ControllerDegreesPerSecond = 120f;
    private const float ControllerDeadzone = 0.10f;
    private static readonly VirtualKey[] EdgeKeys =
    [
        VirtualKey.V,
        VirtualKey.F8,
        VirtualKey.G,
        VirtualKey.R,
        VirtualKey.F10,
    ];

    private readonly SessionLogger logger;
    private readonly bool capturePadInKeyboardMode;
    private readonly bool returnToFollowCamera;
    private readonly CancellationTokenSource cancellation = new();
    private readonly KeyboardState keyboard = new();
    private readonly MouseCapture mouse = new();
    private SessionSnapshot snapshot = new();
    private Task? worker;
    private bool disposed;

    public SessionController(
        SessionLogger logger,
        bool capturePadInKeyboardMode = false,
        bool returnToFollowCamera = false)
    {
        this.logger = logger;
        this.capturePadInKeyboardMode = capturePadInKeyboardMode;
        this.returnToFollowCamera = returnToFollowCamera;
    }

    public SessionSnapshot Snapshot => Volatile.Read(ref snapshot);

    public Task Completion => worker ?? Task.CompletedTask;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (worker is not null)
        {
            throw new InvalidOperationException("The Freecam session already started.");
        }

        worker = Task.Run(() => Run(cancellation.Token));
    }

    public void RequestStop() => cancellation.Cancel();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        cancellation.Cancel();
        try
        {
            worker?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception error)
        {
            // Completion reports the same failure to the UI. Disposal must remain safe.
            logger.Warning("session_dispose_observed_fault", new
            {
                error = error.ToString(),
            });
        }
        finally
        {
            mouse.Release();
            cancellation.Dispose();
            disposed = true;
        }
    }

    private void Run(CancellationToken token)
    {
        PineClient? client = null;
        PadCaptureHook? padHook = null;
        CameraController? camera = null;
        PlayerController? player = null;
        Exception? terminalError = null;
        bool cleanupPausedByUs = false;
        bool resumeOwed = false;
        nint gameHandle = nint.Zero;
        try
        {
            client = PineClient.Connect();
            GameIdentity identity = GameIdentity.Read(client);
            identity.EnsureSupported();
            gameHandle = GameWindow.Find();
            if (gameHandle == nint.Zero)
            {
                throw new InvalidOperationException(
                    "The PCSX2 game window named 'The Warriors' was not found.");
            }

            logger.Info("session_connect", new
            {
                identity,
                gameWindow = GameWindow.Inspect(),
                pinePort = BuildInfo.DefaultPinePort,
                capturePadInKeyboardMode,
                returnToFollowCamera,
            });

            resumeOwed = PauseForCodePatch(client, gameHandle, token);
            try
            {
                padHook = new PadCaptureHook(client);
                padHook.Install();
            }
            finally
            {
                ResumeAfterCodePatch(client, gameHandle, resumeOwed, token);
                resumeOwed = false;
            }

            camera = new CameraController(client);
            player = new PlayerController(client);
            keyboard.Synchronize(EdgeKeys);
            logger.Info("session_ready", new
            {
                cameraObject = $"0x{camera.CameraObject:X8}",
                cameraPosition = camera.Position,
                playerObject = $"0x{player.Human:X8}",
                godMode = player.GodModeEnabled,
                mode = ControlMode.KeyboardAndMouse.ToString(),
            });

            RunLoop(client, padHook, camera, player, gameHandle, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logger.Info("session_stop_requested");
        }
        catch (Exception error)
        {
            terminalError = error;
            logger.Error("session_failed", error);
        }
        finally
        {
            mouse.Release();
            if (client is not null)
            {
                try
                {
                    if (padHook is not null || player is not null)
                    {
                        cleanupPausedByUs = PauseForCodePatch(
                            client,
                            gameHandle != nint.Zero ? gameHandle : GameWindow.Find(),
                            CancellationToken.None);
                    }
                }
                catch (Exception error)
                {
                    logger.Error("cleanup_pause_failed", error);
                }

                try
                {
                    player?.Dispose();
                    logger.Info("player_state_restored");
                }
                catch (Exception error)
                {
                    logger.Error("player_state_restore_failed", error);
                    terminalError ??= error;
                }

                if (returnToFollowCamera && camera is not null)
                {
                    TryHandOffToFollow(camera, "session_cleanup");
                }

                try
                {
                    padHook?.Dispose();
                    logger.Info("pad_hook_removed");
                }
                catch (Exception error)
                {
                    logger.Error("pad_hook_cleanup_failed", error);
                    terminalError ??= error;
                }

                try
                {
                    ResumeAfterCodePatch(
                        client,
                        gameHandle != nint.Zero ? gameHandle : GameWindow.Find(),
                        cleanupPausedByUs || resumeOwed,
                        CancellationToken.None);
                    resumeOwed = false;
                }
                catch (Exception error)
                {
                    logger.Error("cleanup_resume_failed", error);
                    terminalError ??= error;
                }

                client.Dispose();
            }

            Publish(Snapshot with
            {
                Mode = ControlMode.NormalCamera,
                PadSuppressed = false,
                CarryActive = false,
                StatusText = terminalError is null
                    ? "Freecam closed cleanly"
                    : $"Stopped: {terminalError.Message}",
            });
            logger.Info("session_finished", new
            {
                clean = terminalError is null,
                terminalError = terminalError?.ToString(),
                snapshot = Snapshot,
            });
        }

        if (terminalError is not null)
        {
            throw new InvalidOperationException(
                "The Freecam session stopped because of an error. See the log for details.",
                terminalError);
        }
    }

    private void RunLoop(
        PineClient client,
        PadCaptureHook padHook,
        CameraController camera,
        PlayerController player,
        nint initialGameHandle,
        CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        double previousSeconds = clock.Elapsed.TotalSeconds;
        double nextStatusRead = 0;
        double nextPlayerRead = 0;
        double nextPlayerRetry = 0;
        double nextOwnershipCheck = 0;
        double nextSuppressionRefresh = 0;
        double nextTrace = 0;
        double nextWorldRetry = 0;
        double? controllerExitStarted = null;
        bool controllerExitArmed = false;
        bool playerReady = true;
        bool carryPreference = false;
        bool hudVisible = true;
        bool gamePaused = false;
        bool padSuppressed = false;
        long cameraWrites = 0;
        PadState previousPad = PadState.Neutral;
        PadState pad = PadState.Neutral;
        ControlMode mode = ControlMode.KeyboardAndMouse;
        ControlMode? modeBeforeWait = null;
        nint gameHandle = initialGameHandle;
        Vector3? playerPosition = player.LastPosition;

        while (!token.IsCancellationRequested)
        {
            double iterationStart = clock.Elapsed.TotalSeconds;
            float deltaSeconds = (float)Math.Clamp(
                iterationStart - previousSeconds, 0, 0.05);
            previousSeconds = iterationStart;
            gameHandle = GameWindow.Find();
            bool focused = GameWindow.HasFocus(gameHandle);

            if (iterationStart >= nextStatusRead)
            {
                gamePaused = client.ReadStatus() == PineStatus.Paused;
                nextStatusRead = iterationStart + 0.25;
            }

            pad = padHook.ReadPad();
            if (iterationStart >= nextOwnershipCheck)
            {
                padHook.EnsureOwnership();
                nextOwnershipCheck = iterationStart + 1.0;
            }

            if (focused)
            {
                if (keyboard.Pressed(VirtualKey.F10))
                {
                    logger.Info("keyboard_exit");
                    break;
                }

                if (keyboard.Pressed(VirtualKey.V))
                {
                    ControlMode effectiveMode = mode == ControlMode.WaitingForWorld
                        ? modeBeforeWait ?? ControlMode.KeyboardAndMouse
                        : mode;
                    ControlMode target = effectiveMode is
                        ControlMode.KeyboardAndMouse or ControlMode.Controller
                        ? ControlMode.NormalCamera
                        : ControlMode.KeyboardAndMouse;
                    if (mode == ControlMode.WaitingForWorld)
                    {
                        modeBeforeWait = target;
                    }
                    else
                    {
                        ChangeMode(
                            target,
                            ref mode,
                            ref modeBeforeWait,
                            camera,
                            player,
                            playerReady,
                            carryPreference,
                            padHook);
                    }

                    logger.Info("keyboard_mode_toggle", new
                    {
                        mode,
                        requestedMode = target,
                    });
                }

                if (keyboard.Pressed(VirtualKey.F8))
                {
                    carryPreference = !carryPreference;
                    SetCarryForCurrentMode(
                        player, playerReady, mode, carryPreference);
                    logger.Info("keyboard_carry_toggle", new
                    {
                        carryPreference,
                        active = player.Attached,
                    });
                }

                if (keyboard.Pressed(VirtualKey.G) && playerReady)
                {
                    bool enabled = player.ToggleGodMode();
                    logger.Info("keyboard_god_toggle", new { enabled });
                }

                if (keyboard.Pressed(VirtualKey.R))
                {
                    hudVisible = !hudVisible;
                    logger.Info("keyboard_hud_toggle", new { hudVisible });
                }
            }
            else
            {
                keyboard.Synchronize(EdgeKeys);
            }

            bool controllerModeChord = !controllerExitArmed && ChordPressed(
                pad, previousPad, PadButtons.L3);
            if (controllerModeChord)
            {
                ControlMode effectiveMode = mode == ControlMode.WaitingForWorld
                    ? modeBeforeWait ?? ControlMode.KeyboardAndMouse
                    : mode;
                ControlMode target = effectiveMode == ControlMode.Controller
                    ? ControlMode.NormalCamera
                    : ControlMode.Controller;
                if (mode == ControlMode.WaitingForWorld)
                {
                    modeBeforeWait = target;
                }
                else
                {
                    ChangeMode(
                        target,
                        ref mode,
                        ref modeBeforeWait,
                        camera,
                        player,
                        playerReady,
                        carryPreference,
                        padHook);
                }

                logger.Info("controller_mode_toggle", new
                {
                    mode,
                    requestedMode = target,
                });
            }

            if (mode == ControlMode.Controller)
            {
                if (!controllerExitArmed &&
                    ChordPressed(pad, previousPad, PadButtons.R3))
                {
                    carryPreference = !carryPreference;
                    SetCarryForCurrentMode(
                        player, playerReady, mode, carryPreference);
                    logger.Info("controller_carry_toggle", new
                    {
                        carryPreference,
                        active = player.Attached,
                    });
                }

                if (!controllerExitArmed &&
                    ChordPressed(pad, previousPad, PadButtons.Circle) && playerReady)
                {
                    bool enabled = player.ToggleGodMode();
                    logger.Info("controller_god_toggle", new { enabled });
                }

                if (!controllerExitArmed &&
                    ChordPressed(pad, previousPad, PadButtons.Triangle))
                {
                    hudVisible = !hudVisible;
                    logger.Info("controller_hud_toggle", new { hudVisible });
                }

                if (!controllerExitArmed &&
                    pad.IsDown(PadButtons.Select | PadButtons.Start))
                {
                    controllerExitStarted ??= iterationStart;
                    if (iterationStart - controllerExitStarted >= 1.5)
                    {
                        controllerExitArmed = true;
                        controllerExitStarted = null;
                        logger.Info("controller_exit_armed", new
                        {
                            instruction = "Release Select and Start to exit safely.",
                        });
                    }
                }
                else if (!controllerExitArmed)
                {
                    controllerExitStarted = null;
                }
                else if (!pad.IsDown(PadButtons.Select) &&
                    !pad.IsDown(PadButtons.Start))
                {
                    logger.Info("controller_exit_released");
                    break;
                }
            }
            else
            {
                controllerExitStarted = null;
                controllerExitArmed = false;
            }

            bool shouldSuppressPad = PadCapturePolicy.ShouldSuppress(
                mode, capturePadInKeyboardMode);
            if (shouldSuppressPad &&
                iterationStart >= nextSuppressionRefresh)
            {
                padHook.RefreshSuppression();
                padSuppressed = true;
                nextSuppressionRefresh = iterationStart + 0.10;
            }
            else if (!shouldSuppressPad && padSuppressed)
            {
                padHook.ReleaseSuppression();
                padSuppressed = false;
            }

            if (mode == ControlMode.WaitingForWorld)
            {
                mouse.Release();
                if (iterationStart >= nextWorldRetry)
                {
                    nextWorldRetry = iterationStart + 0.25;
                    try
                    {
                        camera.Rebind(preservePose: false);
                        player.Refresh();
                        playerReady = true;
                        mode = modeBeforeWait ?? ControlMode.KeyboardAndMouse;
                        modeBeforeWait = null;
                        if (returnToFollowCamera && mode == ControlMode.NormalCamera)
                        {
                            TryHandOffToFollow(camera, "world_recovered_normal_mode");
                        }
                        SetCarryForCurrentMode(
                            player, playerReady, mode, carryPreference);
                        logger.Info("world_recovered", new
                        {
                            mode,
                            cameraObject = $"0x{camera.CameraObject:X8}",
                            playerObject = $"0x{player.Human:X8}",
                        });
                    }
                    catch (WorldUnavailableException)
                    {
                        // Loading screens are expected; keep retrying.
                    }
                }
            }
            else
            {
                try
                {
                    if (mode == ControlMode.NormalCamera)
                    {
                        mouse.Release();
                        camera.ReadNormalPose();
                    }
                    else
                    {
                        ApplyFreecamInput(
                            camera, mode, pad, gameHandle, focused, deltaSeconds);
                        camera.WritePose();
                        cameraWrites++;
                    }
                }
                catch (WorldUnavailableException error)
                {
                    modeBeforeWait = mode;
                    mode = ControlMode.WaitingForWorld;
                    mouse.Release();
                    player.SuspendWorld();
                    playerReady = false;
                    playerPosition = null;
                    if (padSuppressed)
                    {
                        padHook.ReleaseSuppression();
                        padSuppressed = false;
                    }

                    logger.Warning("world_unavailable", new { error.Message });
                }
            }

            if (mode != ControlMode.WaitingForWorld)
            {
                if (playerReady)
                {
                    try
                    {
                        if (player.Attached)
                        {
                            playerPosition = player.WriteAttachedPosition(
                                camera.Position, camera.Forward);
                        }
                        else if (iterationStart >= nextPlayerRead)
                        {
                            playerPosition = player.ReadPosition();
                            nextPlayerRead = iterationStart + 0.05;
                        }
                    }
                    catch (WorldUnavailableException error)
                    {
                        player.SuspendWorld();
                        playerReady = false;
                        playerPosition = null;
                        nextPlayerRetry = iterationStart + 0.25;
                        logger.Warning("player_unavailable", new { error.Message });
                    }
                }
                else if (iterationStart >= nextPlayerRetry)
                {
                    nextPlayerRetry = iterationStart + 0.25;
                    try
                    {
                        player.Refresh();
                        playerReady = true;
                        SetCarryForCurrentMode(
                            player, playerReady, mode, carryPreference);
                        logger.Info("player_recovered", new
                        {
                            playerObject = $"0x{player.Human:X8}",
                        });
                    }
                    catch (WorldUnavailableException)
                    {
                        // Retry after the next interval.
                    }
                }
            }

            double loopMilliseconds =
                (clock.Elapsed.TotalSeconds - iterationStart) * 1000.0;
            string statusText = BuildStatus(
                mode,
                focused,
                gamePaused,
                playerReady,
                controllerExitArmed,
                capturePadInKeyboardMode);
            Publish(new SessionSnapshot
            {
                Mode = mode,
                ModeBeforeWait = modeBeforeWait,
                HudVisible = hudVisible,
                CarryPreference = carryPreference,
                CarryActive = player.Attached,
                GodModeEnabled = player.GodModeEnabled,
                PadSuppressed = padSuppressed,
                GameFocused = focused,
                GamePaused = gamePaused,
                CameraPosition = camera.Position,
                PlayerPosition = playerPosition,
                CameraObject = camera.CameraObject,
                PlayerObject = player.Human,
                Pad = pad,
                LoopMilliseconds = loopMilliseconds,
                CameraWrites = cameraWrites,
                StatusText = statusText,
            });

            if (iterationStart >= nextTrace)
            {
                logger.Trace("session_state", Snapshot);
                nextTrace = iterationStart + 1.0;
            }

            previousPad = pad;
            double remaining = (1.0 / 120.0) -
                (clock.Elapsed.TotalSeconds - iterationStart);
            if (remaining > 0)
            {
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(remaining));
            }
        }
    }

    private void ApplyFreecamInput(
        CameraController camera,
        ControlMode mode,
        PadState pad,
        nint gameHandle,
        bool focused,
        float deltaSeconds)
    {
        Vector3 movement = Vector3.Zero;
        float speedMultiplier = 1f;
        float yaw = 0f;
        float pitch = 0f;

        if (mode == ControlMode.KeyboardAndMouse)
        {
            if (focused)
            {
                movement.X = Axis(keyboard, VirtualKey.D, VirtualKey.A);
                movement.Y = Axis(keyboard, VirtualKey.W, VirtualKey.S);
                movement.Z = Axis(keyboard, VirtualKey.E, VirtualKey.Q);
                if (keyboard.IsDown(VirtualKey.Shift))
                {
                    speedMultiplier *= 4f;
                }

                if (keyboard.IsDown(VirtualKey.Control))
                {
                    speedMultiplier *= 0.25f;
                }

                Point delta = mouse.ReadDelta(gameHandle);
                yaw = -DegreesToRadians(delta.X * MouseDegreesPerPixel);
                pitch = -DegreesToRadians(delta.Y * MouseDegreesPerPixel);
            }
            else
            {
                mouse.Release();
            }
        }
        else if (mode == ControlMode.Controller)
        {
            mouse.Release();
            Vector2 left = CameraMath.ApplyRadialDeadzone(
                pad.LeftStick, ControllerDeadzone);
            Vector2 right = CameraMath.ApplyRadialDeadzone(
                pad.RightStick, ControllerDeadzone);
            movement.X = left.X;
            movement.Y = left.Y;
            movement.Z =
                (pad.IsDown(PadButtons.R1) ? 1f : 0f) -
                (pad.IsDown(PadButtons.L1) ? 1f : 0f);
            if (pad.IsDown(PadButtons.L2))
            {
                speedMultiplier *= 4f;
            }

            if (pad.IsDown(PadButtons.R2))
            {
                speedMultiplier *= 0.25f;
            }

            float lookRadians = DegreesToRadians(
                ControllerDegreesPerSecond * deltaSeconds);
            yaw = -right.X * lookRadians;
            pitch = -right.Y * lookRadians;
        }

        camera.Rotate(yaw, pitch);
        Vector3 worldMovement =
            (camera.Right * movement.X) +
            (camera.Forward * movement.Y) +
            (Vector3.UnitZ * movement.Z);
        if (worldMovement.LengthSquared() > 1f)
        {
            worldMovement = Vector3.Normalize(worldMovement);
        }

        camera.Move(
            worldMovement * BaseMovementSpeed * speedMultiplier * deltaSeconds);
    }

    private void ChangeMode(
        ControlMode target,
        ref ControlMode mode,
        ref ControlMode? modeBeforeWait,
        CameraController camera,
        PlayerController player,
        bool playerReady,
        bool carryPreference,
        PadCaptureHook padHook)
    {
        if (target == ControlMode.NormalCamera)
        {
            if (playerReady)
            {
                player.SetAttached(false);
            }

            if (returnToFollowCamera)
            {
                TryHandOffToFollow(camera, "normal_mode_selected");
            }
            padHook.ReleaseSuppression();
        }
        else if (mode is ControlMode.NormalCamera or ControlMode.WaitingForWorld)
        {
            camera.Rebind(preservePose: false);
        }

        mode = target;
        modeBeforeWait = null;
        SetCarryForCurrentMode(player, playerReady, mode, carryPreference);
        if (PadCapturePolicy.ShouldSuppress(mode, capturePadInKeyboardMode))
        {
            padHook.RefreshSuppression();
        }
    }

    private void TryHandOffToFollow(CameraController camera, string reason)
    {
        try
        {
            CameraHandoff handoff = camera.HandOffToFollow();
            logger.Info("camera_handoff_to_follow", new
            {
                reason,
                previousActiveCamera = $"0x{handoff.PreviousActiveCamera:X8}",
                previousPriorityCamera = $"0x{handoff.PreviousPriorityCamera:X8}",
                followCamera = $"0x{handoff.FollowCamera:X8}",
                position = camera.Position,
            });
        }
        catch (Exception error)
        {
            logger.Warning("camera_handoff_to_follow_failed", new
            {
                reason,
                error = error.ToString(),
            });
        }
    }

    private static void SetCarryForCurrentMode(
        PlayerController player,
        bool playerReady,
        ControlMode mode,
        bool carryPreference)
    {
        if (playerReady)
        {
            player.SetAttached(
                carryPreference && mode is
                    ControlMode.KeyboardAndMouse or ControlMode.Controller);
        }
    }

    private static bool ChordPressed(
        PadState current,
        PadState previous,
        PadButtons action) =>
        current.IsDown(PadButtons.Select | action) &&
        !previous.IsDown(PadButtons.Select | action);

    private static float Axis(
        KeyboardState state, VirtualKey positive, VirtualKey negative) =>
        (state.IsDown(positive) ? 1f : 0f) -
        (state.IsDown(negative) ? 1f : 0f);

    private static float DegreesToRadians(float degrees) =>
        degrees * (MathF.PI / 180f);

    private static string BuildStatus(
        ControlMode mode,
        bool focused,
        bool paused,
        bool playerReady,
        bool controllerExitArmed,
        bool capturePadInKeyboardMode)
    {
        if (controllerExitArmed)
        {
            return "Exit armed — release Select+Start";
        }

        if (mode == ControlMode.WaitingForWorld)
        {
            return "Waiting for gameplay to resume";
        }

        if (paused)
        {
            return "PCSX2 paused";
        }

        if (!focused && mode == ControlMode.KeyboardAndMouse)
        {
            return "Keyboard/mouse input paused (game not focused)";
        }

        if (!playerReady)
        {
            return "Freecam active; waiting for player entity";
        }

        return mode switch
        {
            ControlMode.NormalCamera => "Normal game camera",
            ControlMode.KeyboardAndMouse => capturePadInKeyboardMode
                ? "Keyboard & mouse Freecam (Pad 1 captured)"
                : "Keyboard & mouse Freecam",
            ControlMode.Controller => "Controller Freecam (Pad 1 captured)",
            _ => "Freecam active",
        };
    }

    private static bool PauseForCodePatch(
        PineClient client,
        nint gameHandle,
        CancellationToken token)
    {
        if (client.ReadStatus() != PineStatus.Running)
        {
            return false;
        }

        SendPauseHotkey(gameHandle);
        WaitForStatus(client, PineStatus.Paused, token);
        return true;
    }

    private static void ResumeAfterCodePatch(
        PineClient client,
        nint gameHandle,
        bool pausedByUs,
        CancellationToken token)
    {
        if (!pausedByUs || client.ReadStatus() != PineStatus.Paused)
        {
            return;
        }

        SendPauseHotkey(gameHandle);
        WaitForStatus(client, PineStatus.Running, token);
    }

    private static void SendPauseHotkey(nint gameHandle)
    {
        if (gameHandle == nint.Zero)
        {
            throw new InvalidOperationException(
                "The game window disappeared while preparing the native input hook.");
        }

        const int space = 0x20;
        bool down = NativeMethods.PostMessage(
            gameHandle, NativeMethods.WmKeyDown, space, 0);
        bool up = NativeMethods.PostMessage(
            gameHandle, NativeMethods.WmKeyUp, space, 0);
        if (!down || !up)
        {
            throw new InvalidOperationException(
                "PCSX2 did not accept the temporary pause hotkey.");
        }
    }

    private static void WaitForStatus(
        PineClient client,
        PineStatus expected,
        CancellationToken token)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(3))
        {
            token.ThrowIfCancellationRequested();
            if (client.ReadStatus() == expected)
            {
                return;
            }

            token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException(
            $"PCSX2 did not enter the expected {expected} state within 3 seconds.");
    }

    private void Publish(SessionSnapshot value) => Volatile.Write(ref snapshot, value);
}
