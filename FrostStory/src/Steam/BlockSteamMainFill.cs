using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FrostStory.Steam;

/// <summary>
/// The rest of a two-block-wide conduit: an unseen, solid cell that knows where its owner is and passes
/// breaking, picking and information on to it.
/// </summary>
public class BlockSteamMainFill : Block
{
    /// <summary>This cell's position relative to its owner's cell.</summary>
    public Vec3i Offset { get; private set; } = new(0, 0, 0);

    public static AssetLocation CodeFor(int x, int y, int z) => new("froststory", $"mainfill-o{x}{y}{z}");

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        string offset = Variant["offset"] ?? "o000";
        // "oXYZ" counts up from the owner's cell; "dXYZ" is Y cells below it
        if (offset.Length == 4) Offset = new Vec3i(offset[1] - '0', (offset[0] == 'd' ? -1 : 1) * (offset[2] - '0'), offset[3] - '0');
    }

    public BlockPos OwnerPos(BlockPos pos) => pos.AddCopy(-Offset.X, -Offset.Y, -Offset.Z);

    /// <summary>Where the owner is, looking in the world if need be (a large engine's cells are chained to it).</summary>
    public virtual BlockPos OwnerPos(IBlockAccessor ba, BlockPos pos) => OwnerPos(pos);

    protected BlockSteamConduit? Owner(IWorldAccessor world, BlockPos pos, out BlockPos ownerPos)
    {
        ownerPos = OwnerPos(world.BlockAccessor, pos);
        return world.BlockAccessor.GetBlock(ownerPos) as BlockSteamConduit;
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        var owner = Owner(world, pos, out var ownerPos);
        if (owner == null) base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        else if (!owner.IsPort) world.BlockAccessor.BreakBlock(ownerPos, byPlayer, dropQuantityMultiplier);
        // a generator port is not broken by way of its fill cells
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        var owner = Owner(world, blockSel.Position, out _);
        return owner != null ? owner.OnBlockInteractStart(world, byPlayer, blockSel) : base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var owner = Owner(world, pos, out var ownerPos);
        return owner != null ? owner.OnPickBlock(world, ownerPos) : base.OnPickBlock(world, pos);
    }

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
    {
        var owner = Owner(world, pos, out var ownerPos);
        return owner != null ? owner.GetPlacedBlockName(world, ownerPos) : base.GetPlacedBlockName(world, pos);
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        var owner = Owner(world, pos, out var ownerPos);
        return owner != null ? owner.GetPlacedBlockInfo(world, ownerPos, forPlayer) : base.GetPlacedBlockInfo(world, pos, forPlayer);
    }
}
