using System;
using FrostStory.Generator.Blueprint;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FrostStory.Ship;

/// <summary>
/// A cell of the wreck's hull: unseen, solid, and part of one of the hull's sections. The section's model
/// is drawn by its <c>shiphull</c> block. Cutting through any cell of a section brings the whole section
/// down for its iron.
/// </summary>
public class BlockShipCell : Block
{
    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        if (world.Side == EnumAppSide.Server && ShipSalvage.BreakSectionAt(world, pos, byPlayer)) return;
        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) => Vintagestory.API.Config.Lang.Get("froststory:block-shiphull-*");
}

public static class ShipSalvage
{
    /// <summary>The wreck a position belongs to: the middle of its keel, or null.</summary>
    public static BlockPos? WreckAt(IWorldAccessor world, BlockPos pos)
    {
        var mod = world.Api.ModLoader.GetModSystem<FrostStoryModSystem>();
        foreach (var keel in mod.Wrecks)
        {
            int dx = pos.X - keel.X, dy = pos.Y - keel.Y, dz = pos.Z - keel.Z;
            if (Math.Abs(dx) <= ShipBlueprint.HalfLength && Math.Abs(dz) <= ShipBlueprint.HalfBeam && dy >= 0 && dy <= ShipBlueprint.Deck + 10) return keel;
        }

        return null;
    }

    /// <summary>Breaks up the section of hull a cell is part of. False when the cell is not part of a known wreck.</summary>
    public static bool BreakSectionAt(IWorldAccessor world, BlockPos pos, IPlayer? byPlayer)
    {
        var keel = WreckAt(world, pos);
        if (keel == null) return false;
        int index = ShipBlueprint.SectionOf(pos.X - keel.X);
        if (index < 0) return false;

        var ba = world.BlockAccessor;
        var (from, to) = ShipBlueprint.Sections[index];
        int plate = 0, planks = 0;
        var cell = pos.Copy();
        for (int x = from; x <= to; x++)
        {
            for (int z = -ShipBlueprint.HalfBeam; z <= ShipBlueprint.HalfBeam; z++)
            {
                for (int y = 0; y <= ShipBlueprint.Deck + 10; y++)
                {
                    cell.Set(keel.X + x, keel.Y + y, keel.Z + z);
                    if (ba.GetBlock(cell) is not BlockShipCell part) continue;
                    if (part.Variant["type"] == "deck") planks++; else plate++;
                    ba.SetBlock(0, cell);
                }
            }
        }

        // what a section yields: most of its plate as plates, the offcuts as bits, the deck as planks
        Drop(world, pos, "game:metalplate-iron", plate / 14);
        Drop(world, pos, "game:metalbit-iron", plate / 3);
        Drop(world, pos, "game:plank-aged", planks / 3);
        world.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit"), pos, 0, byPlayer, true, 48f);
        (byPlayer as IServerPlayer)?.SendMessage(0, Vintagestory.API.Config.Lang.Get("froststory:ship-section-down", plate / 14), EnumChatType.Notification);
        return true;
    }

    private static void Drop(IWorldAccessor world, BlockPos at, string code, int count)
    {
        var item = world.GetItem(new AssetLocation(code));
        while (item != null && count > 0)
        {
            int n = Math.Min(count, item.MaxStackSize);
            world.SpawnItemEntity(new ItemStack(item, n), at.ToVec3d().Add(0.5, 0.5, 0.5));
            count -= n;
        }
    }
}
