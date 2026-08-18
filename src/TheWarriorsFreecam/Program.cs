// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace TheWarriorsFreecam;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(
            initiallyOwned: true,
            $"Local\\TheWarriorsFreecam-v{BuildInfo.Version}",
            out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "The Warriors Freecam is already running.",
                BuildInfo.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        using SessionLogger logger = SessionLogger.Create();
        LogEnvironment(logger);
        Application.ThreadException += (_, eventArgs) =>
        {
            logger.Error("ui_thread_exception", eventArgs.Exception);
            MessageBox.Show(
                $"An unexpected error occurred.\r\n\r\n{eventArgs.Exception.Message}" +
                $"\r\n\r\nLog:\r\n{logger.Path}",
                BuildInfo.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception error)
            {
                logger.Error("unhandled_exception", error, new
                {
                    eventArgs.IsTerminating,
                });
            }
        };

        try
        {
            Application.Run(new LauncherForm(logger));
            logger.Info("application_exit");
        }
        catch (Exception error)
        {
            logger.Error("application_fatal", error);
            MessageBox.Show(
                $"The application could not continue.\r\n\r\n{error.Message}" +
                $"\r\n\r\nLog:\r\n{logger.Path}",
                BuildInfo.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void LogEnvironment(SessionLogger logger)
    {
        string executable = Environment.ProcessPath ?? string.Empty;
        string? sha256 = null;
        try
        {
            if (File.Exists(executable))
            {
                using FileStream stream = File.OpenRead(executable);
                sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException)
        {
            logger.Warning("executable_hash_unavailable", new { error.Message });
        }

        using Process process = Process.GetCurrentProcess();
        logger.Info("application_start", new
        {
            product = BuildInfo.ProductName,
            version = BuildInfo.Version,
            license = BuildInfo.License,
            copyright = BuildInfo.Copyright,
            executable,
            executableSha256 = sha256,
            baseDirectory = AppContext.BaseDirectory,
            currentDirectory = Environment.CurrentDirectory,
            commandLine = Environment.CommandLine,
            processId = Environment.ProcessId,
            processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            framework = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            osVersion = Environment.OSVersion.VersionString,
            is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
            userName = Environment.UserName,
            userDomain = Environment.UserDomainName,
            machineName = Environment.MachineName,
            processorCount = Environment.ProcessorCount,
            workingSet = process.WorkingSet64,
            culture = CultureInfo.CurrentCulture.Name,
            uiCulture = CultureInfo.CurrentUICulture.Name,
            timeZone = TimeZoneInfo.Local.Id,
            localTime = DateTimeOffset.Now,
            utcTime = DateTimeOffset.UtcNow,
            screen = new
            {
                primary = Screen.PrimaryScreen?.Bounds,
                workingArea = Screen.PrimaryScreen?.WorkingArea,
                all = Screen.AllScreens.Select(screen => new
                {
                    screen.DeviceName,
                    screen.Bounds,
                    screen.WorkingArea,
                    screen.Primary,
                    screen.BitsPerPixel,
                }).ToArray(),
            },
            assembly = Assembly.GetExecutingAssembly().FullName,
            log = logger.Path,
        });
    }
}
