// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

using System.Diagnostics;

namespace TheWarriorsFreecam;

internal sealed record GameWindowSnapshot(
    nint Handle,
    Rectangle WindowBounds,
    Rectangle ViewportBounds,
    long Style,
    long ExtendedStyle,
    uint Dpi,
    bool FullscreenSized,
    bool Focused,
    int? ProcessId,
    string? ProcessPath);

internal static class GameWindow
{
    public static nint Find() => NativeMethods.FindWindow(null, "The Warriors");

    public static bool HasFocus(nint handle) =>
        handle != nint.Zero && NativeMethods.GetForegroundWindow() == handle;

    public static Rectangle? GetViewport(nint handle)
    {
        if (handle == nint.Zero ||
            !NativeMethods.GetClientRect(handle, out NativeRect client))
        {
            return null;
        }

        var origin = new NativePoint(0, 0);
        if (!NativeMethods.ClientToScreen(handle, ref origin))
        {
            return null;
        }

        int screenWidth = NativeMethods.GetSystemMetrics(0);
        int screenHeight = NativeMethods.GetSystemMetrics(1);
        bool fullscreen =
            client.Width >= screenWidth - 4 && client.Height >= screenHeight - 4;
        int topInset = fullscreen ? 0 : 21;
        int bottomInset = fullscreen ? 0 : 25;
        int height = Math.Max(1, client.Height - topInset - bottomInset);
        return new Rectangle(
            origin.X,
            origin.Y + topInset,
            Math.Max(1, client.Width),
            height);
    }

    public static GameWindowSnapshot? Inspect()
    {
        nint handle = Find();
        Rectangle? viewport = GetViewport(handle);
        if (handle == nint.Zero || viewport is null ||
            !NativeMethods.GetWindowRect(handle, out NativeRect windowRect))
        {
            return null;
        }

        long style = NativeMethods.GetWindowLongPtr(
            handle, NativeMethods.GwlStyle).ToInt64();
        long extendedStyle = NativeMethods.GetWindowLongPtr(
            handle, NativeMethods.GwlExStyle).ToInt64();
        const long wsPopup = unchecked((long)0x80000000);
        const long wsCaption = 0x00C00000;
        const long wsThickFrame = 0x00040000;
        bool fullscreenSized =
            (style & wsPopup) != 0 &&
            (style & wsCaption) == 0 &&
            (style & wsThickFrame) == 0 &&
            viewport.Value.Width >= NativeMethods.GetSystemMetrics(0) - 4 &&
            viewport.Value.Height >= NativeMethods.GetSystemMetrics(1) - 4;

        Process? process = Process.GetProcessesByName("pcsx2-qt")
            .FirstOrDefault(candidate => candidate.MainWindowHandle == handle);
        string? processPath = null;
        int? processId = null;
        if (process is not null)
        {
            processId = process.Id;
            try
            {
                processPath = process.MainModule?.FileName;
            }
            catch (Exception error) when (
                error is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                processPath = null;
            }
            finally
            {
                process.Dispose();
            }
        }

        return new GameWindowSnapshot(
            handle,
            Rectangle.FromLTRB(
                windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom),
            viewport.Value,
            style,
            extendedStyle,
            NativeMethods.GetDpiForWindow(handle),
            fullscreenSized,
            HasFocus(handle),
            processId,
            processPath);
    }
}
