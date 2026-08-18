// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

using System.Buffers.Binary;
using System.Numerics;

namespace TheWarriorsFreecam;

public sealed record PreflightResult(
    GameIdentity Identity,
    Vector3 CameraPosition,
    Vector3 PlayerPosition,
    bool RecoverableOrphanHook)
{
    public static PreflightResult Run(int port = BuildInfo.DefaultPinePort)
    {
        using PineClient client = PineClient.Connect(port);
        GameIdentity identity = GameIdentity.Read(client);
        identity.EnsureSupported();

        ValidateInstruction(
            client,
            GameAddresses.MainViewHook,
            GameAddresses.MainViewHookOriginal,
            "MapTriggers/native renderer hook");
        ValidateInstruction(
            client,
            GameAddresses.CameraBeginUpdateHook,
            GameAddresses.CameraBeginUpdateOriginal,
            "camera override hook");
        ValidateInstruction(
            client,
            GameAddresses.BootstrapHook,
            GameAddresses.BootstrapHookOriginal,
            "heap bootstrap hook");

        uint padHook = client.Read32(GameAddresses.PadRefreshCall);
        byte[] cave = client.ReadBlock(
            GameAddresses.InputCave, GameAddresses.InputCaveSize);
        uint marker = BinaryPrimitives.ReadUInt32LittleEndian(
            cave.AsSpan(
                checked((int)(GameAddresses.InputMarker - GameAddresses.InputCave)),
                sizeof(uint)));
        uint installedInstruction = Mips.JumpAndLink(GameAddresses.InputCave);
        bool orphan = marker == GameAddresses.InputMarkerValue &&
            padHook is var value &&
            (value == installedInstruction || value == GameAddresses.PadRefreshOriginal);

        if (padHook != GameAddresses.PadRefreshOriginal && !orphan)
        {
            throw new InvalidOperationException(
                $"Pad 1 refresh is modified: 0x{padHook:X8}.");
        }

        if (cave.Any(static value => value != 0) && !orphan)
        {
            throw new InvalidOperationException(
                "The temporary input code area is occupied by another tool.");
        }

        var camera = new CameraController(client);
        Vector3 player = PlayerController.ReadPlayerPosition(client);
        return new PreflightResult(identity, camera.Position, player, orphan);
    }

    private static void ValidateInstruction(
        PineClient client,
        uint address,
        uint expected,
        string label)
    {
        uint current = client.Read32(address);
        if (current != expected)
        {
            throw new InvalidOperationException(
                $"{label} is occupied at 0x{address:X8}: 0x{current:X8}. " +
                "Close other PINE tools and restart the game.");
        }
    }
}
