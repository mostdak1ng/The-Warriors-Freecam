// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

using System.Drawing.Drawing2D;
using System.Numerics;

namespace TheWarriorsFreecam;

internal sealed class OverlayForm : Form
{
    private readonly SessionController session;
    private readonly System.Windows.Forms.Timer timer;
    private readonly Font headingFont = new("Segoe UI Semibold", 13f);
    private readonly Font bodyFont = new("Segoe UI", 10.5f);
    private readonly Font coordinateFont = new("Cascadia Mono", 10.5f);
    private readonly Font watermarkFont = new("Segoe UI Semibold", 9.5f);

    public OverlayForm(SessionController session)
    {
        this.session = session;
        AutoScaleMode = AutoScaleMode.None;
        Color transparencyColor = Color.FromArgb(1, 2, 3);
        BackColor = transparencyColor;
        TransparencyKey = transparencyColor;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        timer = new System.Windows.Forms.Timer
        {
            Interval = 16,
            Enabled = true,
        };
        timer.Tick += (_, _) => UpdateOverlay();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |=
                NativeMethods.WsExTransparent |
                NativeMethods.WsExToolWindow |
                NativeMethods.WsExLayered |
                NativeMethods.WsExNoActivate;
            return parameters;
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        SessionSnapshot state = session.Snapshot;
        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // ClearType blends glyph edges with the transparency key and produces
        // colored halos. Grayscale antialiasing against a near-black key keeps
        // the overlay clean on both borderless and windowed rendering.
        graphics.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        const int margin = 18;
        if (state.HudVisible)
        {
            DrawLeftStatus(graphics, state, margin);
            DrawCoordinates(graphics, state, ClientSize.Width - margin);
        }

        DrawRightAligned(
            graphics,
            BuildInfo.Watermark,
            watermarkFont,
            Color.FromArgb(235, 255, 255, 255),
            ClientSize.Width - margin,
            ClientSize.Height - margin - watermarkFont.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            headingFont.Dispose();
            bodyFont.Dispose();
            coordinateFont.Dispose();
            watermarkFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateOverlay()
    {
        nint gameWindow = GameWindow.Find();
        Rectangle? viewport = GameWindow.GetViewport(gameWindow);
        bool shouldShow =
            viewport is not null &&
            NativeMethods.IsWindowVisible(gameWindow) &&
            !NativeMethods.IsIconic(gameWindow) &&
            GameWindow.HasFocus(gameWindow);
        if (!shouldShow)
        {
            if (Visible)
            {
                Hide();
            }

            return;
        }

        Rectangle target = viewport!.Value;
        if (Bounds != target)
        {
            Bounds = target;
        }

        if (!Visible)
        {
            Show();
        }

        _ = NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HwndTopmost,
            target.Left,
            target.Top,
            target.Width,
            target.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        Invalidate();
    }

    private void DrawLeftStatus(
        Graphics graphics, SessionSnapshot state, int margin)
    {
        string heading = state.Mode switch
        {
            ControlMode.KeyboardAndMouse => state.PadSuppressed
                ? "FREECAM — KEYBOARD & MOUSE (PAD 1 CAPTURED)"
                : "FREECAM — KEYBOARD & MOUSE",
            ControlMode.Controller => "FREECAM — CONTROLLER (PAD 1 CAPTURED)",
            ControlMode.NormalCamera => "FREECAM — NORMAL GAME CAMERA",
            ControlMode.WaitingForWorld => "FREECAM — WAITING FOR GAMEPLAY",
            _ => "FREECAM",
        };
        int y = margin;
        DrawShadowed(graphics, heading, headingFont, Color.White, margin, y);
        y += headingFont.Height + 4;
        DrawShadowed(
            graphics,
            state.StatusText,
            bodyFont,
            state.Mode == ControlMode.WaitingForWorld
                ? Color.Gold
                : Color.FromArgb(230, 230, 230),
            margin,
            y);
        y += bodyFont.Height + 8;

        foreach (string line in ControlLines(state.Mode))
        {
            DrawShadowed(graphics, line, bodyFont, Color.White, margin, y);
            y += bodyFont.Height + 2;
        }

        y += 5;
        string carry = state.CarryActive
            ? "ON"
            : state.CarryPreference ? "ARMED" : "OFF";
        DrawShadowed(
            graphics,
            $"CARRY {carry}    GOD {(state.GodModeEnabled ? "ON" : "OFF")}    HUD ON",
            bodyFont,
            Color.FromArgb(180, 225, 255),
            margin,
            y);
    }

    private void DrawCoordinates(
        Graphics graphics, SessionSnapshot state, int right)
    {
        int y = 18;
        DrawRightAligned(
            graphics, "CAMERA", headingFont, Color.White, right, y);
        y += headingFont.Height + 2;
        foreach (string line in CoordinateLines(state.CameraPosition))
        {
            DrawRightAligned(
                graphics, line, coordinateFont, Color.White, right, y);
            y += coordinateFont.Height + 1;
        }

        y += 9;
        DrawRightAligned(
            graphics, "PLAYER", headingFont, Color.White, right, y);
        y += headingFont.Height + 2;
        foreach (string line in CoordinateLines(state.PlayerPosition))
        {
            DrawRightAligned(
                graphics, line, coordinateFont, Color.White, right, y);
            y += coordinateFont.Height + 1;
        }
    }

    private static string[] ControlLines(ControlMode mode) => mode switch
    {
        ControlMode.KeyboardAndMouse =>
        [
            "WASD Move   Mouse Look   Q/E Down/Up",
            "Shift Fast   Ctrl Precise",
            "V Normal   F8 Carry   G God   R Hide HUD   F10 Exit",
            "Select+L3 switches directly to controller control",
        ],
        ControlMode.Controller =>
        [
            "LS Move   RS Look   L1/R1 Down/Up",
            "L2 Fast   R2 Precise",
            "Select+L3 Normal   +R3 Carry   +B God   +Y Hide HUD",
            "Hold Select+Start for 1.5 seconds, then release both",
        ],
        ControlMode.NormalCamera =>
        [
            "V Keyboard & mouse Freecam",
            "Select+L3 Controller Freecam",
            "F8 Carry   G God   R Hide HUD   F10 Exit",
        ],
        ControlMode.WaitingForWorld =>
        [
            "Controls resume automatically after the loading screen.",
            "F10 Exit",
        ],
        _ => [],
    };

    private static string[] CoordinateLines(Vector3? position) => position is Vector3 value
        ?
        [
            $"X {value.X,10:0.000}",
            $"Y {value.Y,10:0.000}",
            $"Z {value.Z,10:0.000}",
        ]
        : ["X        ---", "Y        ---", "Z        ---"];

    private static void DrawRightAligned(
        Graphics graphics,
        string text,
        Font font,
        Color color,
        int right,
        int y)
    {
        SizeF size = graphics.MeasureString(text, font);
        DrawShadowed(graphics, text, font, color, right - size.Width, y);
    }

    private static void DrawShadowed(
        Graphics graphics,
        string text,
        Font font,
        Color color,
        float x,
        float y)
    {
        using var shadow = new SolidBrush(Color.FromArgb(235, 0, 0, 0));
        using var foreground = new SolidBrush(color);
        graphics.DrawString(text, font, shadow, x + 2, y + 2);
        graphics.DrawString(text, font, foreground, x, y);
    }
}
