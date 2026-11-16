using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FrostStory.Tent;

/// <summary>
/// Tent cloth, as carried. It is not set down as a block: click a tent rod, then another rod or the edge
/// of any block, and a panel is stretched between them.
/// </summary>
public class BlockTentCloth : Block
{
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        if (blockSel == null || !firstEvent) return;

        handling = EnumHandHandling.PreventDefault;
        if (api.Side == EnumAppSide.Server && (byEntity as EntityPlayer)?.Player is IServerPlayer player)
            TentPanels.Click(api.World, player, blockSel, slot);
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        failureCode = "__ignore__";
        return false;
    }
}

/// <summary>
/// The unseen substance of a cloth wall: it has no collision and anything can be built into it, but it
/// stops rain and counts as a wall when the game works out whether a space is an enclosed, warm room.
/// </summary>
public class BlockTentFill : Block
{
    public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type) => 1;
}
