// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

namespace TheWarriorsFreecam;

internal enum VirtualKey
{
    Control = 0x11,
    Shift = 0x10,
    A = 0x41,
    D = 0x44,
    E = 0x45,
    F8 = 0x77,
    F10 = 0x79,
    G = 0x47,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    V = 0x56,
    W = 0x57,
}

internal sealed class KeyboardState
{
    private readonly Dictionary<VirtualKey, bool> previous = new();

    public bool IsDown(VirtualKey key) =>
        (NativeMethods.GetAsyncKeyState((int)key) & 0x8000) != 0;

    public bool Pressed(VirtualKey key)
    {
        bool down = IsDown(key);
        bool wasDown = previous.GetValueOrDefault(key);
        previous[key] = down;
        return down && !wasDown;
    }

    public void Synchronize(params VirtualKey[] keys)
    {
        foreach (VirtualKey key in keys)
        {
            previous[key] = IsDown(key);
        }
    }
}

internal sealed class MouseCapture
{
    private bool engaged;
    private NativePoint original;

    public Point ReadDelta(nint gameWindow)
    {
        Rectangle? viewport = GameWindow.GetViewport(gameWindow);
        if (gameWindow == nint.Zero || viewport is null ||
            !GameWindow.HasFocus(gameWindow))
        {
            Release();
            return Point.Empty;
        }

        var center = new Point(
            viewport.Value.Left + (viewport.Value.Width / 2),
            viewport.Value.Top + (viewport.Value.Height / 2));
        if (!NativeMethods.GetCursorPos(out NativePoint cursor))
        {
            return Point.Empty;
        }

        Point delta = Point.Empty;
        if (engaged)
        {
            int x = cursor.X - center.X;
            int y = cursor.Y - center.Y;
            if (Math.Abs(x) < 1000 && Math.Abs(y) < 1000)
            {
                delta = new Point(x, y);
            }
        }
        else
        {
            original = cursor;
        }

        _ = NativeMethods.SetCursorPos(center.X, center.Y);
        engaged = true;
        return delta;
    }

    public void Release()
    {
        if (!engaged)
        {
            return;
        }

        _ = NativeMethods.SetCursorPos(original.X, original.Y);
        engaged = false;
    }
}
