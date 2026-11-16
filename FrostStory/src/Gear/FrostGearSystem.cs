using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace FrostStory.Gear;

/// <summary>
/// Fur gear and iron ice tools. The wearables are plain JSON on the vanilla clothing system; this
/// system registers the ice saw and the tooltip lines for the cold-weather attributes.
/// </summary>
public class FrostGearSystem : ModSystem
{
    /// <summary>Item attribute (0..1 per worn piece) the climate sums, capped at 1, against storm wind chill.</summary>
    public const string WindproofAttr = "froststoryWindproof";

    /// <summary>Item attribute (0..1) the climate uses to cut whiteout fog.</summary>
    public const string StormVisionAttr = "froststoryStormVision";

    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("FrostStoryIceSaw", typeof(ItemIceSaw));
        api.RegisterCollectibleBehaviorClass("FrostStoryGearInfo", typeof(CollectibleBehaviorGearInfo));
    }
}

/// <summary>Adds "Windproof" and "Storm vision" lines to the item tooltip.</summary>
public class CollectibleBehaviorGearInfo : CollectibleBehavior
{
    public CollectibleBehaviorGearInfo(CollectibleObject collObj) : base(collObj) { }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        var attrs = inSlot.Itemstack?.Collectible?.Attributes;
        if (attrs == null) return;
        float wind = attrs[FrostGearSystem.WindproofAttr].AsFloat(0);
        float vision = attrs[FrostGearSystem.StormVisionAttr].AsFloat(0);
        if (wind > 0) dsc.AppendLine(Lang.Get("froststory:gear-windproof", (int)Math.Round(wind * 100)));
        if (vision > 0) dsc.AppendLine(Lang.Get("froststory:gear-stormvision", (int)Math.Round(vision * 100)));
    }
}

/// <summary>
/// The ice saw cuts ice and packed snow out whole: lake ice, glacier ice and snow blocks drop as
/// the block itself, ready to build with, instead of melting into water or crumbling to snowballs.
/// Lake ice still leaves its water behind.
/// </summary>
public class ItemIceSaw : Item
{
    public static bool CutsWhole(Block block)
    {
        if (block == null || block.Id == 0) return false;
        if (block.BlockMaterial == EnumBlockMaterial.Ice) return true;
        return block.BlockMaterial == EnumBlockMaterial.Snow && block.Code?.Domain == "game" && block.Code.Path == "snowblock";
    }

    public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, float dropQuantityMultiplier = 1)
    {
        BlockPos pos = blockSel.Position;
        Block block = world.BlockAccessor.GetBlock(pos);
        if (!CutsWhole(block)) return base.OnBlockBrokenWith(world, byEntity, itemslot, blockSel, dropQuantityMultiplier);

        IPlayer? byPlayer = (byEntity as EntityPlayer)?.Player;
        if (world.Side == EnumAppSide.Server && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            ItemStack stack = block.OnPickBlock(world, pos) ?? new ItemStack(block);
            world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        // Break it as usual but without its own drops: lake ice turns back to water, snow drops no snowballs.
        block.OnBlockBroken(world, pos, byPlayer, 0f);

        if (DamagedBy != null && Array.IndexOf(DamagedBy, EnumItemDamageSource.BlockBreaking) >= 0)
        {
            DamageItem(world, byEntity, itemslot);
        }
        return true;
    }
}
