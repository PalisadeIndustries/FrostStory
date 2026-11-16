using FrostStory.Coal;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FrostStory.Ship;

/// <summary>Sets the wrecked freighter down in the world, bow to the east, frozen in up to its waterline.</summary>
public static class ShipBuilder
{
    /// <summary>
    /// Builds the wreck with the middle of its keel at <paramref name="keel"/>. Returns how many blocks went
    /// in, or -1 when part of the site is not loaded.
    /// </summary>
    public static int Place(IWorldAccessor world, BlockPos keel, bool ice)
    {
        var mod = world.Api.ModLoader.GetModSystem<FrostStoryModSystem>();
        var plan = mod.Wreck;
        var ba = world.BlockAccessor;
        var pos = keel.Copy();

        for (int x = plan.MinX; x <= plan.MaxX; x += 8)
        {
            pos.Set(keel.X + x, keel.Y, keel.Z);
            if (ba.GetChunkAtBlockPos(pos) == null) return -1;
        }

        pos.Set(keel.X + plan.MaxX, keel.Y, keel.Z);
        if (ba.GetChunkAtBlockPos(pos) == null) return -1;

        // clear the hull's whole volume and what stands over it, then build
        for (int i = 0; i + 3 < plan.Clear.Length; i += 4)
        {
            for (int z = plan.Clear[i + 1]; z <= plan.Clear[i + 2]; z++)
            {
                for (int y = 0; y <= plan.Clear[i + 3]; y++)
                {
                    pos.Set(keel.X + plan.Clear[i], keel.Y + y, keel.Z + z);
                    if (ba.GetBlock(pos).Id != 0) ba.SetBlock(0, pos);
                    ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
                }
            }
        }

        var blocks = new Block?[plan.Codes.Length];
        for (int i = 0; i < blocks.Length; i++) blocks[i] = world.GetBlock(new AssetLocation(plan.Codes[i]));

        int placed = 0;
        for (int i = 0; i + 4 < plan.Cells.Length; i += 5)
        {
            var block = blocks[plan.Cells[i + 3]];
            if (block == null) continue;
            pos.Set(keel.X + plan.Cells[i], keel.Y + plan.Cells[i + 1], keel.Z + plan.Cells[i + 2]);
            ba.SetBlock(block.Id, pos);
            placed++;

            // a bunker's coal is really there
            if (ba.GetBlockEntity(pos) is BlockEntityCoalHeap heap && int.TryParse(block.Variant["layers"], out int layers))
                heap.Set(layers * CoalPile.PerLayer);
        }

        if (ice)
        {
            var packed = world.GetBlock(new AssetLocation("game", "glacierice"));
            for (int i = 0; packed != null && i + 2 < plan.Ice.Length; i += 3)
            {
                pos.Set(keel.X + plan.Ice[i], keel.Y + plan.Ice[i + 1], keel.Z + plan.Ice[i + 2]);
                if (ba.GetChunkAtBlockPos(pos) == null) continue;
                var there = ba.GetBlock(pos);
                if (there.Id != 0 && there.Replaceable < 6000) continue;      // leave the ground alone
                ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
                ba.SetBlock(packed.Id, pos);
            }
        }

        if (!mod.Wrecks.Exists(k => k.Equals(keel))) mod.Wrecks.Add(keel.Copy());
        return placed;
    }
}
