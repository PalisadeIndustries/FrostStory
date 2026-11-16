using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FrostStory.Stove;

/// <summary>
/// A small cast-iron stove for a tent or a hut. It burns whatever the firepit burns, is lit like a
/// firepit, and only draws with a flue above it that reaches open sky.
/// </summary>
public class BlockTentStove : Block, IHeatSource, IIgnitable
{
    private FrostStoryModSystem? mod;

    public bool IsLit { get; private set; }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        mod = api.ModLoader.GetModSystem<FrostStoryModSystem>();
        IsLit = Variant["state"] == "lit";
    }

    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        return IsLit ? mod!.Config.StoveHeatStrength : 0f;
    }

    public static bool IsFuel(ItemStack? stack)
    {
        var props = stack?.Collectible.CombustibleProps;
        return props != null && props.BurnDuration > 0 && props.BurnTemperature > 0;
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (byPlayer == null) return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);

        // the fire door faces whoever sets it down
        var door = SuggestedHVOrientation(byPlayer, blockSel)[0];
        var turned = world.GetBlock(CodeWithVariant("side", door.Code)) ?? this;
        return turned.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode) && turned.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (!IsFuel(slot?.Itemstack) || world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityTentStove stove)
            return base.OnBlockInteractStart(world, byPlayer, blockSel);

        if (world.Side == EnumAppSide.Server && byPlayer is IServerPlayer player)
        {
            string? problem = stove.AddFuel(slot!, byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative);
            if (problem != null) player.SendIngameError(problem, Vintagestory.API.Config.Lang.Get("froststory:" + problem));
        }

        return true;
    }

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
    {
        if (IsLit || byEntity.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityTentStove stove || stove.FuelSeconds <= 0)
            return EnumIgniteState.NotIgnitablePreventDefault;
        return secondsIgniting > 3 ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        handling = EnumHandling.PreventDefault;
        if (byEntity.World.Side != EnumAppSide.Server || byEntity.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityTentStove stove) return;

        string? problem = stove.TryLight();
        if (problem != null && (byEntity as EntityPlayer)?.Player is IServerPlayer player)
            player.SendIngameError(problem, Vintagestory.API.Config.Lang.Get("froststory:" + problem));
    }

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting) => EnumIgniteState.NotIgnitable;
}

/// <summary>A length of stove flue. It is a thin pipe, but it closes the hole it makes in a tent or a roof.</summary>
public class BlockStoveFlue : Block
{
    public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type) => 1;
}
