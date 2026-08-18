// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

using System.Numerics;

namespace TheWarriorsFreecam;

public enum ControlMode
{
    NormalCamera,
    KeyboardAndMouse,
    Controller,
    WaitingForWorld,
}

public sealed record SessionSnapshot
{
    public ControlMode Mode { get; init; } = ControlMode.KeyboardAndMouse;

    public ControlMode? ModeBeforeWait { get; init; }

    public bool HudVisible { get; init; } = true;

    public bool CarryPreference { get; init; }

    public bool CarryActive { get; init; }

    public bool GodModeEnabled { get; init; } = true;

    public bool PadSuppressed { get; init; }

    public bool GameFocused { get; init; }

    public bool GamePaused { get; init; }

    public bool WorldPaused { get; init; }

    public Vector3? CameraPosition { get; init; }

    public Vector3? PlayerPosition { get; init; }

    public uint CameraObject { get; init; }

    public uint PlayerObject { get; init; }

    public PadState Pad { get; init; } = PadState.Neutral;

    public double LoopMilliseconds { get; init; }

    public long CameraWrites { get; init; }

    public string StatusText { get; init; } = "Starting";
}
