// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 mostdak1ng

namespace TheWarriorsFreecam;

public sealed class WorldTimeController
{
    public const float DefaultScale = 0.0001f;

    private readonly PineClient client;
    private readonly float scale;
    private uint? originalBits;
    private uint? targetBits;

    public WorldTimeController(PineClient client, float scale = DefaultScale)
    {
        ValidateScale(scale);
        this.client = client;
        this.scale = scale;
    }

    public bool IsPaused => originalBits.HasValue;

    public WorldTimeChange Pause()
    {
        if (originalBits is uint existingOriginal && targetBits is uint existingTarget)
        {
            return CreateChange(existingOriginal, existingTarget, scale);
        }

        uint original = client.Read32(GameAddresses.WorldTimestep);
        WorldTimeChange change = Calculate(original, scale);
        originalBits = change.OriginalBits;
        targetBits = change.TargetBits;
        try
        {
            client.Write32(GameAddresses.WorldTimestep, change.TargetBits);
            uint actual = client.Read32(GameAddresses.WorldTimestep);
            if (actual != change.TargetBits)
            {
                throw new InvalidOperationException(
                    "World timestep pause did not verify: " +
                    $"expected 0x{change.TargetBits:X8}, received 0x{actual:X8}.");
            }
        }
        catch (Exception pauseError)
        {
            try
            {
                _ = Restore();
            }
            catch (Exception restoreError)
            {
                throw new AggregateException(
                    "World pause failed and its original timestep could not be restored.",
                    pauseError,
                    restoreError);
            }

            throw;
        }

        return change;
    }

    public void Enforce()
    {
        if (targetBits is not uint target)
        {
            return;
        }

        if (client.Read32(GameAddresses.WorldTimestep) != target)
        {
            client.Write32(GameAddresses.WorldTimestep, target);
        }
    }

    public float? Restore()
    {
        if (originalBits is not uint original)
        {
            return null;
        }

        client.Write32(GameAddresses.WorldTimestep, original);
        uint actual = client.Read32(GameAddresses.WorldTimestep);
        if (actual != original)
        {
            throw new InvalidOperationException(
                "World timestep restoration did not verify: " +
                $"expected 0x{original:X8}, received 0x{actual:X8}.");
        }

        originalBits = null;
        targetBits = null;
        return BitConverter.UInt32BitsToSingle(original);
    }

    public static WorldTimeChange Calculate(uint originalBits, float scale)
    {
        ValidateScale(scale);
        float original = BitConverter.UInt32BitsToSingle(originalBits);
        if (!float.IsFinite(original) || original <= 0f)
        {
            throw new InvalidDataException(
                "Native world timestep must be a finite positive value: " +
                $"0x{originalBits:X8}.");
        }

        float target = original * scale;
        if (!float.IsFinite(target) || target < 0f)
        {
            throw new InvalidDataException("Scaled world timestep is invalid.");
        }

        return new WorldTimeChange(
            originalBits,
            BitConverter.SingleToUInt32Bits(target),
            original,
            target,
            scale);
    }

    private static WorldTimeChange CreateChange(
        uint originalBits, uint targetBits, float scale) => new(
            originalBits,
            targetBits,
            BitConverter.UInt32BitsToSingle(originalBits),
            BitConverter.UInt32BitsToSingle(targetBits),
            scale);

    private static void ValidateScale(float scale)
    {
        if (!float.IsFinite(scale) || scale < 0f || scale >= 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "World time scale must be finite and in [0, 1).");
        }
    }
}

public readonly record struct WorldTimeChange(
    uint OriginalBits,
    uint TargetBits,
    float OriginalTimestep,
    float TargetTimestep,
    float Scale);
