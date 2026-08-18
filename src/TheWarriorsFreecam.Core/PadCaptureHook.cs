// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

using System.Buffers.Binary;

namespace TheWarriorsFreecam;

public sealed class PadCaptureHook : IDisposable
{
    public const uint SuppressionTtl = 60;
    private readonly PineClient client;
    private bool installed;
    private bool disposed;

    public PadCaptureHook(PineClient client)
    {
        this.client = client;
    }

    public uint InstalledInstruction => Mips.JumpAndLink(GameAddresses.InputCave);

    public bool IsInstalled => installed;

    public static byte[] BuildBlob()
    {
        const int zero = 0;
        const int t0 = 8;
        const int t1 = 9;
        const int t2 = 10;
        const int stackPointer = 29;
        const int returnAddress = 31;

        // The game's refresh path treats its pad buffer as previous-frame state.
        // Restore the last physical sample before calling it, capture the newly
        // refreshed sample, then neutralize only the copy consumed by gameplay.
        // TTL is decremented in guest code, so input automatically returns if
        // the host process stops refreshing it.
        uint[] words =
        [
            Mips.AddImmediateUnsigned(stackPointer, stackPointer, -0x20),
            Mips.StoreDoubleword(returnAddress, 0x10, stackPointer),
            Mips.LoadUpperImmediate(t0, 0x005E),
            Mips.LoadUpperImmediate(t1, 0x0051),
            Mips.LoadDoubleword(t2, -0x5790, t1),
            Mips.StoreDoubleword(t2, -0x1B30, t0),
            Mips.JumpAndLink(GameAddresses.PadRefreshFunction),
            0,
            Mips.LoadUpperImmediate(t0, 0x005E),
            Mips.LoadUpperImmediate(t1, 0x0051),
            Mips.LoadDoubleword(t2, -0x1B30, t0),
            Mips.StoreDoubleword(t2, -0x5790, t1),
            Mips.LoadWord(t2, -0x5788, t1),
            Mips.BranchEqual(t2, zero, 7),
            Mips.AddImmediateUnsigned(t2, t2, -1),
            Mips.StoreWord(t2, -0x5788, t1),
            Mips.LoadUpperImmediate(t2, 0x7F7F),
            Mips.OrImmediate(t2, t2, 0x7F7F),
            Mips.StoreDoubleword(t2, -0x1B30, t0),
            Mips.StoreDoubleword(zero, -0x1B28, t0),
            Mips.StoreHalfword(zero, -0x1B20, t0),
            Mips.LoadDoubleword(returnAddress, 0x10, stackPointer),
            Mips.JumpRegister(returnAddress),
            Mips.AddImmediateUnsigned(stackPointer, stackPointer, 0x20),
        ];

        byte[] blob = new byte[GameAddresses.InputCaveSize];
        for (int index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                blob.AsSpan(index * sizeof(uint), sizeof(uint)), words[index]);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            blob.AsSpan(
                checked((int)(GameAddresses.InputCapture - GameAddresses.InputCave)),
                sizeof(uint)),
            0x7F7F7F7F);
        BinaryPrimitives.WriteUInt32LittleEndian(
            blob.AsSpan(
                checked((int)(GameAddresses.InputMarker - GameAddresses.InputCave)),
                sizeof(uint)),
            GameAddresses.InputMarkerValue);
        return blob;
    }

    public void ValidateExclusiveEnvironment()
    {
        ValidateInstruction(
            GameAddresses.MainViewHook,
            GameAddresses.MainViewHookOriginal,
            "MapTriggers/native renderer hook");
        ValidateInstruction(
            GameAddresses.CameraBeginUpdateHook,
            GameAddresses.CameraBeginUpdateOriginal,
            "camera override hook");
        ValidateInstruction(
            GameAddresses.BootstrapHook,
            GameAddresses.BootstrapHookOriginal,
            "heap bootstrap hook");
    }

    public void Install()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (installed)
        {
            return;
        }

        ValidateExclusiveEnvironment();
        uint hook = client.Read32(GameAddresses.PadRefreshCall);
        byte[] cave = client.ReadBlock(
            GameAddresses.InputCave, GameAddresses.InputCaveSize);
        bool ownMarker = BinaryPrimitives.ReadUInt32LittleEndian(
            cave.AsSpan(
                checked((int)(GameAddresses.InputMarker - GameAddresses.InputCave)),
                sizeof(uint))) == GameAddresses.InputMarkerValue;

        if (ownMarker && hook is var value &&
            (value == InstalledInstruction || value == GameAddresses.PadRefreshOriginal))
        {
            // Recover a previous crashed session before taking fresh ownership.
            client.Write32(GameAddresses.InputTtl, 0);
            if (hook == InstalledInstruction)
            {
                client.Write32(
                    GameAddresses.PadRefreshCall, GameAddresses.PadRefreshOriginal);
            }

            client.WriteBlock(
                GameAddresses.InputCave,
                new byte[GameAddresses.InputCaveSize]);
            hook = GameAddresses.PadRefreshOriginal;
            cave = new byte[GameAddresses.InputCaveSize];
        }

        if (hook != GameAddresses.PadRefreshOriginal)
        {
            throw new InvalidOperationException(
                "The game's pad refresh call is already modified: " +
                $"0x{hook:X8}. Close other tools and restart the game.");
        }

        if (cave.Any(static value => value != 0))
        {
            throw new InvalidOperationException(
                "The game's temporary input code area is occupied. " +
                "Close other tools and restart the game.");
        }

        byte[] blob = BuildBlob();
        try
        {
            client.WriteBlock(GameAddresses.InputCave, blob);
            client.Write32(GameAddresses.PadRefreshCall, InstalledInstruction);
            if (client.Read32(GameAddresses.PadRefreshCall) != InstalledInstruction)
            {
                throw new InvalidOperationException(
                    "The Pad 1 capture hook did not verify after installation.");
            }

            byte[] installedBlob = client.ReadBlock(
                GameAddresses.InputCave, GameAddresses.InputCaveSize);
            if (!installedBlob.AsSpan().SequenceEqual(blob))
            {
                throw new InvalidOperationException(
                    "The Pad 1 capture code did not verify after installation.");
            }

            installed = true;
        }
        catch
        {
            TryCleanupOwnedState();
            throw;
        }
    }

    public PadState ReadPad()
    {
        if (!installed)
        {
            throw new InvalidOperationException("Pad 1 capture is not installed.");
        }

        byte[] state = client.ReadBlock(GameAddresses.InputCapture, 0x10);
        return PadState.Decode(state);
    }

    public uint ReadTtl() => client.Read32(GameAddresses.InputTtl);

    public void RefreshSuppression() => client.Write32(
        GameAddresses.InputTtl, SuppressionTtl);

    public void ReleaseSuppression() => client.Write32(GameAddresses.InputTtl, 0);

    public void EnsureOwnership()
    {
        if (!installed)
        {
            throw new InvalidOperationException("Pad 1 capture is not installed.");
        }

        uint hook = client.Read32(GameAddresses.PadRefreshCall);
        uint marker = client.Read32(GameAddresses.InputMarker);
        if (hook != InstalledInstruction || marker != GameAddresses.InputMarkerValue)
        {
            throw new InvalidOperationException(
                "Pad 1 capture ownership was lost. A save state may have been " +
                $"loaded while the mod was running. Hook=0x{hook:X8}, " +
                $"marker=0x{marker:X8}.");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            TryCleanupOwnedState();
        }
        finally
        {
            disposed = true;
            installed = false;
        }
    }

    private void TryCleanupOwnedState()
    {
        try
        {
            client.Write32(GameAddresses.InputTtl, 0);
        }
        catch
        {
            // Continue: restoring the call site is the critical cleanup.
        }

        uint hook = client.Read32(GameAddresses.PadRefreshCall);
        uint marker = client.Read32(GameAddresses.InputMarker);
        if (hook == InstalledInstruction)
        {
            client.Write32(GameAddresses.PadRefreshCall, GameAddresses.PadRefreshOriginal);
            if (client.Read32(GameAddresses.PadRefreshCall) !=
                GameAddresses.PadRefreshOriginal)
            {
                throw new InvalidOperationException(
                    "Could not restore the native Pad 1 refresh call.");
            }
        }
        else if (hook != GameAddresses.PadRefreshOriginal)
        {
            throw new InvalidOperationException(
                $"Refusing to overwrite an unknown Pad 1 hook: 0x{hook:X8}.");
        }

        if (marker == GameAddresses.InputMarkerValue)
        {
            client.WriteBlock(
                GameAddresses.InputCave,
                new byte[GameAddresses.InputCaveSize]);
        }

        installed = false;
    }

    private void ValidateInstruction(uint address, uint expected, string label)
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
