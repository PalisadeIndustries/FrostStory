using FrostStory.Coal;
using FrostStory.Generator.Blueprint;
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
        var ba = world.BlockAccessor;
        var plan = ShipBlueprint.Build();
        var pos = keel.Copy();

        foreach (var p in plan)
        {
            pos.Set(keel.X + p.X, keel.Y + p.Y, keel.Z + p.Z);
            if (ba.GetChunkAtBlockPos(pos) == null) return -1;
        }

        // clear the inside of the hull and what stands on it, then build
        for (int x = -ShipBlueprint.HalfLength; x <= ShipBlueprint.HalfLength; x++)
        {
            for (int z = -ShipBlueprint.HalfBeam; z <= ShipBlueprint.HalfBeam; z++)
            {
                for (int y = 0; y <= ShipBlueprint.Deck + 22; y++)
                {
                    if (y <= ShipBlueprint.TopAt(x) + 1 && !ShipBlueprint.InHull(x, System.Math.Min(y, ShipBlueprint.TopAt(x)), z)) continue;
                    pos.Set(keel.X + x, keel.Y + y, keel.Z + z);
                    if (ba.GetBlock(pos).Id != 0) ba.SetBlock(0, pos);
                    ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
                }
            }
        }

        int placed = 0;
        foreach (var p in plan)
        {
            var block = world.GetBlock(new AssetLocation(p.Code!));
            if (block == null) continue;
            pos.Set(keel.X + p.X, keel.Y + p.Y, keel.Z + p.Z);
            ba.SetBlock(block.Id, pos);
            placed++;

            // a bunker's coal is really there
            if (ba.GetBlockEntity(pos) is BlockEntityCoalHeap heap && int.TryParse(block.Variant["layers"], out int layers))
                heap.Set(layers * CoalPile.PerLayer);
        }

        if (ice)
        {
            var packed = world.GetBlock(new AssetLocation("game", "glacierice"));
            if (packed != null)
            {
                foreach (var (x, y, z) in ShipBlueprint.Ice())
                {
                    pos.Set(keel.X + x, keel.Y + y, keel.Z + z);
                    if (ba.GetChunkAtBlockPos(pos) == null) continue;
                    var there = ba.GetBlock(pos);
                    if (there.Id != 0 && there.Replaceable < 6000) continue;      // leave the ground alone
                    ba.SetBlock(0, pos, BlockLayersAccess.Fluid);
                    ba.SetBlock(packed.Id, pos);
                }
            }
        }

        return placed;
    }
}
