using System;
using System.Collections.Generic;
using FrostStory.Ship;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FrostStory.Expedition;

/// <summary>
/// Finds a place for the start wreck near the world spawn, sets her down with <see cref="ShipBuilder"/>,
/// packs ice under her where the ground falls away, and finds the spot on the ice beside the hole in her
/// port side.
/// </summary>
public static class ExpeditionWreck
{
    /// <summary>Along the keel, the middle of the hole stove in her port side (see BREACH in tools/gen_ship.py).</summary>
    public const int HoleX = 16;

    /// <summary>How far round the hull the ground is looked at, beyond the plan's own extent.</summary>
    private const int Margin = 7;

    /// <summary>The chunk columns that cover every site within <paramref name="distance"/> of <paramref name="centre"/>.</summary>
    public static (int X1, int Z1, int X2, int Z2) ChunkArea(BlockPos centre, int distance, WreckPlan plan)
    {
        int cs = GlobalConstants.ChunkSize;
        int x1 = centre.X - distance + plan.MinX - Margin, x2 = centre.X + distance + plan.MaxX + Margin;
        int reachZ = distance + plan.HalfBeam + Margin;
        return (Floor(x1, cs), Floor(centre.Z - reachZ, cs), Floor(x2, cs), Floor(centre.Z + reachZ, cs));
    }

    /// <summary>The chunk columns the wreck itself covers, laid with her keel at <paramref name="keel"/>.</summary>
    public static (int X1, int Z1, int X2, int Z2) ChunkAreaAt(BlockPos keel, WreckPlan plan) => ChunkArea(keel, 0, plan);

    private static int Floor(int v, int cs) => (int)Math.Floor(v / (double)cs);

    /// <summary>Whether every chunk column of an area is loaded (and so generated) at the heights that matter.</summary>
    public static bool AreaLoaded(ICoreServerAPI api, (int X1, int Z1, int X2, int Z2) area)
    {
        var ba = api.World.BlockAccessor;
        int top = Math.Max(0, api.WorldManager.MapSizeY / GlobalConstants.ChunkSize - 1);
        for (int cx = area.X1; cx <= area.X2; cx++)
        {
            for (int cz = area.Z1; cz <= area.Z2; cz++)
            {
                if (api.WorldManager.GetMapChunk(cx, cz) == null) return false;
                if (ba.GetChunk(cx, 0, cz) == null || ba.GetChunk(cx, top, cz) == null) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The best place within <paramref name="distance"/> of <paramref name="centre"/> to lay her: level ground,
    /// dry rather than open water, near the spawn. Returns the middle of the keel, so that the ice comes up
    /// to the ground, or null when the area is not loaded.
    /// </summary>
    public static BlockPos? FindSite(ICoreServerAPI api, BlockPos centre, int distance, WreckPlan plan, out string summary)
    {
        summary = "";
        var ba = api.World.BlockAccessor;
        var heights = new Dictionary<(int, int), (int H, bool Water)>();
        var pos = new BlockPos(0, 0, 0, 0);

        (int H, bool Water) Ground(int x, int z)
        {
            if (heights.TryGetValue((x, z), out var known)) return known;
            pos.Set(x, 0, z);
            int h = ba.GetTerrainMapheightAt(pos);
            pos.Y = h + 1;
            bool water = ba.GetBlock(pos, BlockLayersAccess.Fluid)?.IsLiquid() == true;
            return heights[(x, z)] = (h, water);
        }

        int seaLevel = api.World.SeaLevel;
        BlockPos? best = null;
        double bestScore = double.MaxValue;
        float bestRough = 0, bestWater = 0;
        var sample = new List<int>(256);

        for (int dx = -distance; dx <= distance; dx += 4)
        {
            for (int dz = -distance; dz <= distance; dz += 4)
            {
                if (dx * dx + dz * dz > distance * distance) continue;
                int cx = centre.X + dx, cz = centre.Z + dz;
                sample.Clear();
                int water = 0;
                for (int x = plan.MinX - Margin + 1; x <= plan.MaxX + Margin; x += 6)
                {
                    for (int z = -plan.HalfBeam - Margin; z <= plan.HalfBeam + Margin; z += 4)
                    {
                        var (h, wet) = Ground(cx + x, cz + z);
                        if (h <= 0) continue;
                        sample.Add(h);
                        if (wet || h < seaLevel - 1) water++;
                    }
                }

                if (sample.Count == 0) continue;
                sample.Sort();
                int median = sample[sample.Count / 2];
                double rough = 0;
                foreach (int h in sample) rough += Math.Abs(h - median);
                rough /= sample.Count;
                float wetShare = water / (float)sample.Count;
                double score = rough + 6 * wetShare + Math.Sqrt(dx * dx + dz * dz) / 32.0;
                if (score >= bestScore) continue;

                bestScore = score;
                bestRough = (float)rough;
                bestWater = wetShare;
                best = new BlockPos(cx, median - plan.Waterline, cz, 0);
            }
        }

        if (best != null)
        {
            int maxY = api.WorldManager.MapSizeY - plan.Height - 2;
            best.Y = Math.Clamp(best.Y, 2, Math.Max(2, maxY));
            summary = $"keel at {best.X} {best.Y} {best.Z}, {Math.Sqrt((best.X - centre.X) * (best.X - centre.X) + (best.Z - centre.Z) * (best.Z - centre.Z)):0} blocks from spawn, ground off level by {bestRough:0.0} blocks on average, {bestWater * 100:0}% water";
        }

        return best;
    }

    /// <summary>
    /// Lays the wreck with her keel at <paramref name="keel"/>, frozen in, then packs ice below the ice sheet and
    /// the hull wherever the ground under them is lower, at most <paramref name="fillDepth"/> below the keel.
    /// Returns the blocks of the wreck, or -1 when part of the site is not loaded.
    /// </summary>
    public static int Lay(IWorldAccessor world, BlockPos keel, int fillDepth)
    {
        int placed = ShipBuilder.Place(world, keel, true);
        if (placed < 0 || fillDepth <= 0) return placed;

        var plan = world.Api.ModLoader.GetModSystem<FrostStoryModSystem>().Wreck;
        var ice = world.GetBlock(new AssetLocation("game", "glacierice"));
        if (ice == null) return placed;

        // the lowest thing set down in each column
        var lowest = new Dictionary<(int, int), int>();
        void Note(int x, int y, int z)
        {
            if (!lowest.TryGetValue((x, z), out int y0) || y < y0) lowest[(x, z)] = y;
        }

        for (int i = 0; i + 4 < plan.Cells.Length; i += 5) Note(plan.Cells[i], plan.Cells[i + 1], plan.Cells[i + 2]);
        for (int i = 0; i + 2 < plan.Ice.Length; i += 3) Note(plan.Ice[i], plan.Ice[i + 1], plan.Ice[i + 2]);

        var ba = world.BlockAccessor;
        var pos = keel.Copy();
        int packed = 0;
        foreach (var ((x, z), y0) in lowest)
        {
            for (int y = y0 - 1; y >= -fillDepth; y--)
            {
                pos.Set(keel.X + x, keel.Y + y, keel.Z + z);
                if (pos.Y <= 0 || ba.GetChunkAtBlockPos(pos) == null) break;
                var there = ba.GetBlock(pos);
                if (there.Id != 0 && there.Replaceable < 6000) break;     // the ground
                ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
                ba.SetBlock(ice.Id, pos);
                packed++;
            }
        }

        world.Logger.Notification("[froststory] Expedition wreck: packed {0} blocks of ice under the hull and the ice sheet.", packed);
        return placed;
    }

    /// <summary>Where a survivor stands on the ice two blocks off the hole in her port side.</summary>
    public static BlockPos SpawnBeside(IWorldAccessor world, BlockPos keel, WreckPlan plan)
    {
        var ba = world.BlockAccessor;
        int x = keel.X + HoleX, z = keel.Z - plan.HalfBeam - 2;
        var pos = new BlockPos(x, 0, z, 0);
        for (int y = keel.Y + plan.Waterline + 16; y >= Math.Max(1, keel.Y - 8); y--)
        {
            pos.Y = y;
            var block = ba.GetBlock(pos);
            if (block.Id == 0 || block.IsLiquid()) continue;
            var boxes = block.GetCollisionBoxes(ba, pos);
            if (boxes == null || boxes.Length == 0) continue;
            return new BlockPos(x, y + 1, z, 0);
        }

        return new BlockPos(x, keel.Y + plan.Waterline + 1, z, 0);
    }
}
