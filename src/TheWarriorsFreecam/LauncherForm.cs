// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

using System.Diagnostics;

namespace TheWarriorsFreecam;

internal sealed class LauncherForm : Form
{
    private readonly SessionLogger logger;
    private readonly Label statusLabel;
    private readonly CheckBox warningCheckBox;
    private readonly CheckBox padCaptureCheckBox;
    private readonly Button startButton;
    private readonly Button recheckButton;
    private readonly Button logsButton;
    private SessionController? session;
    private OverlayForm? overlay;
    private bool preflightPassed;
    private bool allowClose;

    public LauncherForm(SessionLogger logger)
    {
        this.logger = logger;
        Text = $"{BuildInfo.ProductName} v{BuildInfo.Version}";
        ClientSize = new Size(700, 560);
        MinimumSize = new Size(716, 599);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 26, 31);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;

        var title = new Label
        {
            AutoSize = true,
            Text = "THE WARRIORS FREECAM",
            Font = new Font("Segoe UI Semibold", 22f),
            ForeColor = Color.White,
            Location = new Point(28, 24),
        };
        var version = new Label
        {
            AutoSize = true,
            Text = $"v{BuildInfo.Version}  •  by mostdak1ng  •  GPL-3.0-only",
            ForeColor = Color.FromArgb(170, 180, 195),
            Location = new Point(31, 69),
        };
        var requirement = new Label
        {
            AutoSize = false,
            Text = "Supported game: USA SLUS-21215 • version 1.03 • CRC B99A75DE\r\n" +
                "Primary display mode: borderless fullscreen. Windowed mode is also supported.",
            ForeColor = Color.FromArgb(210, 215, 225),
            Location = new Point(31, 105),
            Size = new Size(638, 48),
        };

        var warningPanel = new Panel
        {
            BackColor = Color.FromArgb(47, 39, 23),
            Location = new Point(30, 166),
            Size = new Size(640, 116),
        };
        var warningTitle = new Label
        {
            AutoSize = true,
            Text = "SAVE STATE WARNING",
            Font = new Font("Segoe UI Semibold", 11f),
            ForeColor = Color.FromArgb(255, 211, 105),
            Location = new Point(15, 12),
        };
        warningCheckBox = new CheckBox
        {
            AutoSize = false,
            Text = "I created a backup save state before starting. I will not create or load " +
                "save states while the mod is running.",
            ForeColor = Color.White,
            Location = new Point(17, 42),
            Size = new Size(605, 58),
            UseVisualStyleBackColor = true,
        };
        warningCheckBox.CheckedChanged += (_, _) => RefreshStartAvailability();
        warningPanel.Controls.Add(warningTitle);
        warningPanel.Controls.Add(warningCheckBox);

        padCaptureCheckBox = new CheckBox
        {
            AutoSize = false,
            Text = "Capture Pad 1 in keyboard/mouse Freecam " +
                "(enable when PCSX2 maps Pad 1 to keyboard keys).",
            ForeColor = Color.White,
            Location = new Point(31, 298),
            Size = new Size(638, 42),
            UseVisualStyleBackColor = true,
            Checked = false,
        };

        statusLabel = new Label
        {
            AutoSize = false,
            Text = "Checking PCSX2 and the running game…",
            ForeColor = Color.FromArgb(190, 200, 215),
            Location = new Point(31, 351),
            Size = new Size(638, 48),
        };

        startButton = CreateButton("Start Freecam", new Point(30, 417), 190, true);
        recheckButton = CreateButton("Recheck", new Point(232, 417), 132, false);
        logsButton = CreateButton("Open Logs", new Point(376, 417), 132, false);
        Button exitButton = CreateButton("Exit", new Point(520, 417), 150, false);
        startButton.Enabled = false;
        startButton.Click += async (_, _) => await StartSessionAsync();
        recheckButton.Click += async (_, _) => await RunPreflightAsync();
        logsButton.Click += (_, _) => OpenLogs();
        exitButton.Click += (_, _) => Close();

        var footer = new Label
        {
            AutoSize = false,
            Text = "Start mode: keyboard & mouse Freecam • Carry off • God mode on\r\n" +
                "Clean exit: F10 or hold Select+Start for 1.5 seconds, then release both.",
            ForeColor = Color.FromArgb(150, 160, 175),
            Location = new Point(31, 485),
            Size = new Size(638, 45),
        };

        Controls.AddRange(
        [
            title,
            version,
            requirement,
            warningPanel,
            padCaptureCheckBox,
            statusLabel,
            startButton,
            recheckButton,
            logsButton,
            exitButton,
            footer,
        ]);
        Shown += async (_, _) => await RunPreflightAsync();
        FormClosing += OnFormClosing;
    }

    private static Button CreateButton(
        string text, Point location, int width, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Location = location,
            Size = new Size(width, 42),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary
                ? Color.FromArgb(35, 112, 196)
                : Color.FromArgb(51, 55, 65),
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = primary
            ? Color.FromArgb(68, 151, 238)
            : Color.FromArgb(80, 85, 96);
        return button;
    }

    private async Task RunPreflightAsync()
    {
        SetBusy(true);
        statusLabel.ForeColor = Color.FromArgb(190, 200, 215);
        statusLabel.Text = "Checking PINE, game identity, camera, player, and hook safety…";
        try
        {
            PreflightResult result = await Task.Run(() => PreflightResult.Run());
            GameWindowSnapshot? window = GameWindow.Inspect();
            if (window is null)
            {
                throw new InvalidOperationException(
                    "The PCSX2 game window named 'The Warriors' was not found.");
            }

            preflightPassed = true;
            statusLabel.ForeColor = Color.FromArgb(126, 231, 150);
            statusLabel.Text = result.RecoverableOrphanHook
                ? "Ready. A recoverable hook from an interrupted Freecam session was found; " +
                    "it will be cleaned safely on start."
                : $"Ready. PCSX2 is connected; supported game detected. " +
                    $"Display: {(window.FullscreenSized ? "fullscreen-sized" : "windowed")}. " +
                    "Use borderless mode for the HUD.";
            logger.Info("preflight_passed", new { result, window });
        }
        catch (Exception error)
        {
            preflightPassed = false;
            statusLabel.ForeColor = Color.FromArgb(255, 135, 135);
            statusLabel.Text = FriendlyPreflightError(error);
            logger.Error("preflight_failed", error, new { gameWindow = GameWindow.Inspect() });
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StartSessionAsync()
    {
        if (!preflightPassed || !warningCheckBox.Checked || session is not null)
        {
            return;
        }

        await RunPreflightAsync();
        if (!preflightPassed)
        {
            return;
        }

        SetBusy(true);
        logger.Info("warning_acknowledged", new
        {
            backupSaveStateConfirmed = true,
            noSaveStatesWhileRunningConfirmed = true,
            capturePadInKeyboardMode = padCaptureCheckBox.Checked,
        });
        session = new SessionController(logger, padCaptureCheckBox.Checked);
        overlay = new OverlayForm(session);
        Hide();
        overlay.Show();
        session.Start();
        try
        {
            await session.Completion;
            allowClose = true;
            Close();
        }
        catch (Exception error)
        {
            logger.Error("session_ui_failure", error);
            overlay.Close();
            overlay.Dispose();
            overlay = null;
            session.Dispose();
            session = null;
            Show();
            statusLabel.ForeColor = Color.FromArgb(255, 135, 135);
            statusLabel.Text =
                "Freecam stopped after an error. The game state cleanup was attempted; " +
                "open the log before retrying.";
            MessageBox.Show(
                this,
                $"Freecam stopped after an error.\r\n\r\n{error.GetBaseException().Message}" +
                $"\r\n\r\nDetailed log:\r\n{logger.Path}",
                BuildInfo.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        recheckButton.Enabled = !busy && session is null;
        padCaptureCheckBox.Enabled = !busy && session is null;
        RefreshStartAvailability(busy);
    }

    private void RefreshStartAvailability(bool busy = false) =>
        startButton.Enabled =
            !busy && session is null && preflightPassed && warningCheckBox.Checked;

    private void OpenLogs()
    {
        string directory = Path.GetDirectoryName(logger.Path) ?? AppContext.BaseDirectory;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{logger.Path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception error)
        {
            logger.Error("open_logs_failed", error, new { directory });
            MessageBox.Show(
                this,
                $"Logs are stored in:\r\n{directory}",
                "Log folder",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (allowClose || session is null)
        {
            return;
        }

        eventArgs.Cancel = true;
        session.RequestStop();
    }

    private static string FriendlyPreflightError(Exception error)
    {
        string message = error.GetBaseException().Message;
        if (error.GetBaseException() is System.Net.Sockets.SocketException)
        {
            return "PCSX2 PINE was not reachable on port 28011. Start PCSX2, " +
                "enable PINE, load the supported game, and press Recheck.";
        }

        return $"Not ready: {message}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            overlay?.Dispose();
            session?.Dispose();
        }

        base.Dispose(disposing);
    }
}
