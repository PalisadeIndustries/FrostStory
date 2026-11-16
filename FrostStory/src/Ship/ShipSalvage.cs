using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FrostStory.Ship;

/// <summary>
/// A cell of the wreck: either unseen substance (a solid cell, or thin walls and a floor on the sides
/// of an open one) or the block a group's model hangs from. Cutting through any cell of a group brings
/// the whole group down for what it is made of.
/// </summary>
public class BlockShipCell : Block
{
    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        if (world.Side == EnumAppSide.Server && ShipSalvage.BreakGroupAt(world, pos, byPlayer)) return;
        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
    {
        // the name of what it is part of, where this side knows of the wreck
        var mod = world.Api.ModLoader.GetModSystem<FrostStoryModSystem>();
        var keel = ShipSalvage.WreckAt(world, pos);
        if (keel != null)
        {
            int group = mod.Wreck.GroupAt(pos.X - keel.X, pos.Y - keel.Y, pos.Z - keel.Z);
            if (group >= 0) return ShipSalvage.NameOf(mod.Wreck.Groups[group]);
        }

        return Lang.Get("froststory:wreck-part");
    }
}

public static class ShipSalvage
{
    /// <summary>"hull-3" is section 3 of the hull; "boiler-1" is a boiler.</summary>
    public static string NameOf(WreckGroup group)
    {
        string kind = group.Name.Split('-')[0];
        return Lang.Get("froststory:wreck-" + kind);
    }

    /// <summary>The wreck a position is a cell of: the middle of its keel, or null.</summary>
    public static BlockPos? WreckAt(IWorldAccessor world, BlockPos pos)
    {
        var mod = world.Api.ModLoader.GetModSystem<FrostStoryModSystem>();
        var plan = mod.Wreck;
        foreach (var keel in mod.Wrecks)
        {
            int dx = pos.X - keel.X, dy = pos.Y - keel.Y, dz = pos.Z - keel.Z;
            if (dx < plan.MinX || dx > plan.MaxX || Math.Abs(dz) > plan.HalfBeam + 1 || dy < 0 || dy > plan.Height) continue;
            if (plan.GroupAt(dx, dy, dz) >= 0) return keel;      // wrecks may lie close together
        }

        return null;
    }

    /// <summary>Breaks up the group a cell is part of. False when the cell is not part of a known wreck.</summary>
    public static bool BreakGroupAt(IWorldAccessor world, BlockPos pos, IPlayer? byPlayer)
    {
        var keel = WreckAt(world, pos);
        if (keel == null) return false;
        var plan = world.Api.ModLoader.GetModSystem<FrostStoryModSystem>().Wreck;
        int index = plan.GroupAt(pos.X - keel.X, pos.Y - keel.Y, pos.Z - keel.Z);
        if (index < 0) return false;

        var ba = world.BlockAccessor;
        var cell = pos.Copy();
        foreach (var (x, y, z) in plan.CellsOf(index))
        {
            cell.Set(keel.X + x, keel.Y + y, keel.Z + z);
            if (ba.GetBlock(cell) is BlockShipCell) ba.SetBlock(0, cell);
        }

        var group = plan.Groups[index];
        foreach (var drop in group.Drops) Drop(world, pos, drop.Code, drop.Quantity);
        world.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit"), pos, 0, byPlayer, true, 48f);
        (byPlayer as IServerPlayer)?.SendMessage(0, Lang.Get("froststory:wreck-down", NameOf(group)), EnumChatType.Notification);
        return true;
    }

    private static void Drop(IWorldAccessor world, BlockPos at, string code, int count)
    {
        var location = new AssetLocation(code);
        CollectibleObject? thing = world.GetItem(location);
        thing ??= world.GetBlock(location);
        while (thing != null && thing.Id != 0 && count > 0)
        {
            int n = Math.Min(count, thing.MaxStackSize);
            world.SpawnItemEntity(new ItemStack(thing, n), at.ToVec3d().Add(0.5, 0.5, 0.5));
            count -= n;
        }
    }
}
