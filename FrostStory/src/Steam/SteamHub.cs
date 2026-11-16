using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FrostStory.Steam;

/// <summary>
/// A steam hub warms the open ground round it. The game only feels for heat within three blocks of a
/// player, so the hub keeps unseen cells of warm air on a grid over the ground out to its radius; each
/// is a heat source while the hub has steam, weaker the further out it is.
/// </summary>
public static class SteamHub
{
    private const int Spacing = 4;

    private static IEnumerable<(BlockPos Cell, float Share)> Cells(IWorldAccessor world, BlockPos hub, int radius)
    {
        var ba = world.BlockAccessor;
        for (int dx = -radius; dx <= radius; dx += Spacing)
        {
            for (int dz = -radius; dz <= radius; dz += Spacing)
            {
                double distance = Math.Sqrt(dx * dx + dz * dz);
                if (distance > radius + 0.5 || (dx == 0 && dz == 0)) continue;

                // chest height over the ground: the first open cell with something to stand on, near the hub's level
                var pos = new BlockPos(hub.X + dx, hub.Y + 4, hub.Z + dz, hub.dimension);
                if (ba.GetChunkAtBlockPos(pos) == null) continue;
                for (int i = 0; i < 9; i++, pos.Down())
                {
                    if (!Open(world, pos) || Open(world, pos.DownCopy())) continue;
                    var cell = pos.UpCopy();
                    if (Open(world, cell)) yield return (cell, 1f - 0.6f * (float)(distance / radius));
                    break;
                }
            }
        }
    }

    private static bool Open(IWorldAccessor world, BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);
        return block.Id == 0 || block is BlockPipeWarmth;
    }

    /// <summary>Lays the hub's warm air afresh, following the ground as it is now.</summary>
    public static int Refresh(IWorldAccessor world, BlockPos hub)
    {
        var mod = world.Api.ModLoader.GetModSystem<FrostStoryModSystem>();
        var warmth = world.GetBlock(new AssetLocation("froststory", "pipewarmth"));
        if (warmth == null) return 0;

        Clear(world, hub);
        int count = 0;
        foreach (var (cell, share) in Cells(world, hub, Math.Clamp(mod.Config.HubRadius, Spacing, 32)))
        {
            if (mod.WarmthOwners.ContainsKey(cell) || mod.HubWarmth.ContainsKey(cell)) continue;     // a pipe run's, or another hub's
            if (world.BlockAccessor.GetBlock(cell).Id == 0) world.BlockAccessor.SetBlock(warmth.Id, cell);
            mod.HubWarmth[cell] = (hub.Copy(), share);
            count++;
        }

        return count;
    }

    public static void Clear(IWorldAccessor world, BlockPos hub)
    {
        var mod = world.Api.ModLoader.GetModSystem<FrostStoryModSystem>();
        var warmth = world.GetBlock(new AssetLocation("froststory", "pipewarmth"));
        var mine = new List<BlockPos>();
        foreach (var entry in mod.HubWarmth)
        {
            if (entry.Value.Hub.Equals(hub)) mine.Add(entry.Key);
        }

        foreach (var cell in mine)
        {
            mod.HubWarmth.Remove(cell);
            if (warmth != null && world.BlockAccessor.GetChunkAtBlockPos(cell) != null && world.BlockAccessor.GetBlock(cell).Id == warmth.Id)
                world.BlockAccessor.SetBlock(0, cell);
        }
    }
}
