// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

namespace TheWarriorsFreecam;

public static class PadCapturePolicy
{
    public static bool ShouldSuppress(
        ControlMode mode,
        bool capturePadInKeyboardMode) =>
        mode == ControlMode.Controller ||
        (mode == ControlMode.KeyboardAndMouse && capturePadInKeyboardMode);
}
