// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

using System.Buffers.Binary;
using System.Numerics;

namespace TheWarriorsFreecam;

[Flags]
public enum PadButtons : ushort
{
    None = 0,
    Select = 0x0100,
    L3 = 0x0200,
    R3 = 0x0400,
    Start = 0x0800,
    Up = 0x1000,
    Right = 0x2000,
    Down = 0x4000,
    Left = 0x8000,
    L2 = 0x0001,
    R2 = 0x0002,
    L1 = 0x0004,
    R1 = 0x0008,
    Triangle = 0x0010,
    Circle = 0x0020,
    Cross = 0x0040,
    Square = 0x0080,
}

public readonly record struct PadState(
    byte LeftX,
    byte LeftY,
    byte RightX,
    byte RightY,
    PadButtons Buttons)
{
    public static PadState Neutral { get; } = new(127, 127, 127, 127, 0);

    public Vector2 LeftStick => new(NormalizeAxis(LeftX), -NormalizeAxis(LeftY));

    public Vector2 RightStick => new(NormalizeAxis(RightX), NormalizeAxis(RightY));

    public bool IsDown(PadButtons buttons) => (Buttons & buttons) == buttons;

    public static PadState Decode(ReadOnlySpan<byte> capture)
    {
        if (capture.Length < 6)
        {
            throw new ArgumentException(
                "A captured pad record requires at least six bytes.", nameof(capture));
        }

        return new PadState(
            capture[0],
            capture[1],
            capture[2],
            capture[3],
            (PadButtons)BinaryPrimitives.ReadUInt16LittleEndian(capture[4..6]));
    }

    private static float NormalizeAxis(byte value)
    {
        const float center = 127.5f;
        return Math.Clamp((value - center) / center, -1f, 1f);
    }
}
