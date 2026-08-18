// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

using System.Buffers.Binary;
using System.Numerics;
using TheWarriorsFreecam;

var tests = new List<(string Name, Action Run)>
{
    ("Build metadata", TestBuildMetadata),
    ("Supported identity", TestSupportedIdentity),
    ("Pad decoding", TestPadDecoding),
    ("Radial deadzone", TestRadialDeadzone),
    ("Quaternion rotation", TestQuaternionRotation),
    ("MIPS helpers", TestMipsHelpers),
    ("Pad hook blob", TestPadHookBlob),
    ("Pad capture policy", TestPadCapturePolicy),
    ("Native handle logging", TestNativeHandleLogging),
};
if (args.Contains("--live-preflight", StringComparer.OrdinalIgnoreCase))
{
    tests.Add(("Live read-only preflight", TestLivePreflight));
}
if (args.Contains("--live-hook-smoke", StringComparer.OrdinalIgnoreCase))
{
    tests.Add(("Live paused hook install/cleanup", TestLiveHookSmoke));
}
if (args.Contains("--live-session-smoke", StringComparer.OrdinalIgnoreCase))
{
    tests.Add(("Live paused camera/player restoration", TestLiveSessionSmoke));
}
if (args.Contains("--live-camera-handoff", StringComparer.OrdinalIgnoreCase))
{
    tests.Add(("Live paused FollowCamera handoff", TestLiveCameraHandoff));
}

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {name}: {error.Message}");
    }
}

Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed.");
return failures == 0 ? 0 : 1;

static void TestBuildMetadata()
{
    Equal("0.1.5", BuildInfo.Version);
    Equal("The Warriors Freecam", BuildInfo.ProductName);
    Equal("Freecam mod by mostdak1ng v0.1.5", BuildInfo.Watermark);
    Equal("GPL-3.0-only", BuildInfo.License);
    Equal(0x005D9150u, GameAddresses.CameraPriorityPointer);
    Equal(0x005D9158u, GameAddresses.FollowCameraPointer);
}

static void TestSupportedIdentity()
{
    var supported = new GameIdentity(
        "2.7.522", "The Warriors", "slus-21215", "1.03", "B99A75DE", PineStatus.Running);
    True(supported.IsSupported);
    supported.EnsureSupported();

    var wrong = supported with { Crc = "00000000" };
    True(!wrong.IsSupported);
    Throws<NotSupportedException>(wrong.EnsureSupported);
}

static void TestPadDecoding()
{
    byte[] raw = [0, 255, 127, 128, 0x28, 0x03];
    PadState pad = PadState.Decode(raw);
    Equal((byte)0, pad.LeftX);
    Equal((byte)255, pad.LeftY);
    True(pad.IsDown(PadButtons.Select | PadButtons.L3));
    True(pad.IsDown(PadButtons.Circle));
    True(!pad.IsDown(PadButtons.Start));
    Near(-1f, pad.LeftStick.X, 0.0001f);
    Near(-1f, pad.LeftStick.Y, 0.0001f);
    Throws<ArgumentException>(() => PadState.Decode(new byte[5]));
}

static void TestRadialDeadzone()
{
    Equal(Vector2.Zero, CameraMath.ApplyRadialDeadzone(Vector2.Zero, 0.10f));
    Equal(
        Vector2.Zero,
        CameraMath.ApplyRadialDeadzone(new Vector2(0.06f, 0.07f), 0.10f));
    Vector2 half = CameraMath.ApplyRadialDeadzone(new Vector2(0.55f, 0f), 0.10f);
    Near(0.5f, half.X, 0.0001f);
    Near(0f, half.Y, 0.0001f);
    Vector2 clamped = CameraMath.ApplyRadialDeadzone(new Vector2(2f, 0f), 0.10f);
    Near(1f, clamped.X, 0.0001f);
    Throws<ArgumentOutOfRangeException>(() =>
        CameraMath.ApplyRadialDeadzone(Vector2.One, 1f));
}

static void TestQuaternionRotation()
{
    Quaternion yaw = CameraMath.AxisAngle(Vector3.UnitZ, MathF.PI / 2f);
    Vector3 right = CameraMath.Rotate(yaw, Vector3.UnitX);
    Near(0f, right.X, 0.0001f);
    Near(1f, right.Y, 0.0001f);
    Near(0f, right.Z, 0.0001f);
    Quaternion normalized = CameraMath.Multiply(yaw, Quaternion.Identity);
    Near(1f, normalized.Length(), 0.0001f);
    Throws<InvalidDataException>(() =>
        CameraMath.NormalizeQuaternion(Quaternion.Zero));
}

static void TestMipsHelpers()
{
    Equal(0x0C142A04u, Mips.JumpAndLink(GameAddresses.InputCave));
    Equal(0x27BDFFE0u, Mips.AddImmediateUnsigned(29, 29, -0x20));
    Equal(0x03E00008u, Mips.JumpRegister(31));
}

static void TestPadHookBlob()
{
    byte[] blob = PadCaptureHook.BuildBlob();
    Equal(GameAddresses.InputCaveSize, blob.Length);
    uint[] words = Enumerable.Range(0, 24)
        .Select(index => BinaryPrimitives.ReadUInt32LittleEndian(
            blob.AsSpan(index * sizeof(uint), sizeof(uint))))
        .ToArray();
    Equal(Mips.LoadDoubleword(10, -0x5790, 9), words[4]);
    Equal(Mips.StoreDoubleword(10, -0x1B30, 8), words[5]);
    Equal(Mips.JumpAndLink(GameAddresses.PadRefreshFunction), words[6]);
    Equal(Mips.BranchEqual(10, 0, 7), words[13]);
    int branchTarget = 13 + 1 + (short)(words[13] & 0xFFFF);
    Equal(21, branchTarget);
    Equal(Mips.StoreDoubleword(10, -0x1B30, 8), words[18]);
    Equal(Mips.JumpRegister(31), words[22]);
    Equal(Mips.AddImmediateUnsigned(29, 29, 0x20), words[23]);

    int captureOffset = checked((int)(
        GameAddresses.InputCapture - GameAddresses.InputCave));
    int ttlOffset = checked((int)(GameAddresses.InputTtl - GameAddresses.InputCave));
    int markerOffset = checked((int)(
        GameAddresses.InputMarker - GameAddresses.InputCave));
    Equal(0x7F7F7F7Fu, BinaryPrimitives.ReadUInt32LittleEndian(
        blob.AsSpan(captureOffset, sizeof(uint))));
    Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(
        blob.AsSpan(ttlOffset, sizeof(uint))));
    Equal(GameAddresses.InputMarkerValue, BinaryPrimitives.ReadUInt32LittleEndian(
        blob.AsSpan(markerOffset, sizeof(uint))));
    Equal(60u, PadCaptureHook.SuppressionTtl);
}

static void TestLivePreflight()
{
    PreflightResult result = PreflightResult.Run();
    True(result.Identity.IsSupported);
    True(CameraMath.IsFinite(result.CameraPosition));
    True(CameraMath.IsFinite(result.PlayerPosition));
    Console.WriteLine(
        $"      PCSX2 {result.Identity.EmulatorVersion}; " +
        $"{result.Identity.Serial} v{result.Identity.GameVersion}; " +
        $"CRC {result.Identity.Crc.ToUpperInvariant()}; " +
        $"status {result.Identity.Status}");
}

static void TestLiveHookSmoke()
{
    using PineClient client = PineClient.Connect();
    GameIdentity identity = GameIdentity.Read(client);
    identity.EnsureSupported();
    Equal(PineStatus.Paused, identity.Status);
    Equal(GameAddresses.PadRefreshOriginal, client.Read32(GameAddresses.PadRefreshCall));
    True(client.ReadBlock(GameAddresses.InputCave, GameAddresses.InputCaveSize)
        .All(value => value == 0));

    using (var hook = new PadCaptureHook(client))
    {
        hook.Install();
        True(hook.IsInstalled);
        hook.EnsureOwnership();
        Equal(hook.InstalledInstruction, client.Read32(GameAddresses.PadRefreshCall));
        Equal(0u, hook.ReadTtl());
    }

    Equal(GameAddresses.PadRefreshOriginal, client.Read32(GameAddresses.PadRefreshCall));
    True(client.ReadBlock(GameAddresses.InputCave, GameAddresses.InputCaveSize)
        .All(value => value == 0));
}

static void TestNativeHandleLogging()
{
    string directory = Path.Combine(
        AppContext.BaseDirectory, "test-logs", Guid.NewGuid().ToString("N"));
    try
    {
        string path;
        using (SessionLogger logger = SessionLogger.Create(directory))
        {
            path = logger.Path;
            logger.Info("native_handle", new { handle = (nint)0x1234 });
        }

        string line = File.ReadAllText(path);
        True(line.Contains("\"handle\":\"0x1234\"", StringComparison.Ordinal));
        True(!line.Contains("serialization_failed", StringComparison.Ordinal));
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void TestPadCapturePolicy()
{
    True(PadCapturePolicy.ShouldSuppress(ControlMode.Controller, false));
    True(PadCapturePolicy.ShouldSuppress(ControlMode.Controller, true));
    True(!PadCapturePolicy.ShouldSuppress(ControlMode.KeyboardAndMouse, false));
    True(PadCapturePolicy.ShouldSuppress(ControlMode.KeyboardAndMouse, true));
    True(!PadCapturePolicy.ShouldSuppress(ControlMode.NormalCamera, true));
    True(!PadCapturePolicy.ShouldSuppress(ControlMode.WaitingForWorld, true));
}

static void TestLiveSessionSmoke()
{
    using PineClient client = PineClient.Connect();
    GameIdentity identity = GameIdentity.Read(client);
    identity.EnsureSupported();
    Equal(PineStatus.Paused, identity.Status);

    var camera = new CameraController(client);
    uint human = PlayerController.ResolvePlayerObject(client);
    ulong originalSafety = client.Read64(human + 0xE0);
    ulong forcedOriginalSafety = originalSafety | GameAddresses.PlayerGodModeBit;
    client.Write64(human + 0xE0, forcedOriginalSafety);
    try
    {
        using (var player = new PlayerController(client))
        {
            Equal(human, player.Human);
            ulong modifiedSafety = client.Read64(human + 0xE0);
            True((modifiedSafety & GameAddresses.PlayerGodModeBit) != 0);
            Equal(
                forcedOriginalSafety & ~GameAddresses.PlayerSafetyMask,
                modifiedSafety & ~GameAddresses.PlayerSafetyMask);
            player.SetGodMode(false);
            True((client.Read64(human + 0xE0) &
                GameAddresses.PlayerGodModeBit) == 0);
            player.SetGodMode(true);
            True((client.Read64(human + 0xE0) &
                GameAddresses.PlayerGodModeBit) != 0);
            camera.WritePose();
        }

        Equal(forcedOriginalSafety, client.Read64(human + 0xE0));
    }
    finally
    {
        client.Write64(human + 0xE0, originalSafety);
    }

    Equal(originalSafety, client.Read64(human + 0xE0));
}

static void TestLiveCameraHandoff()
{
    using PineClient client = PineClient.Connect();
    GameIdentity identity = GameIdentity.Read(client);
    identity.EnsureSupported();
    Equal(PineStatus.Paused, identity.Status);

    uint originalActive = client.Read32(GameAddresses.CameraObjectPointer);
    uint originalPriority = client.Read32(GameAddresses.CameraPriorityPointer);
    try
    {
        var camera = new CameraController(client);
        CameraHandoff handoff = camera.HandOffToFollow();
        Equal(originalActive, handoff.PreviousActiveCamera);
        Equal(originalPriority, handoff.PreviousPriorityCamera);
        Equal(GameAddresses.FollowCameraVtable, client.Read32(handoff.FollowCamera));
        Equal(handoff.FollowCamera, client.Read32(GameAddresses.CameraObjectPointer));
        Equal(handoff.FollowCamera, client.Read32(GameAddresses.CameraPriorityPointer));
        Equal(handoff.FollowCamera, camera.CameraObject);
    }
    finally
    {
        client.Write32Pair(
            GameAddresses.CameraObjectPointer,
            originalActive,
            GameAddresses.CameraPriorityPointer,
            originalPriority);
    }

    Equal(originalActive, client.Read32(GameAddresses.CameraObjectPointer));
    Equal(originalPriority, client.Read32(GameAddresses.CameraPriorityPointer));
}

static void True(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected condition to be true.");
    }
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException(
            $"Expected {expected}, received {actual}.");
    }
}

static void Near(float expected, float actual, float tolerance)
{
    if (MathF.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException(
            $"Expected {expected} ± {tolerance}, received {actual}.");
    }
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected exception {typeof(TException).Name}.");
}
