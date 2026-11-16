using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FrostStory.Steam;

/// <summary>
/// Warm air round a pipe run: an unseen block with no substance that anything can be built into. It is a
/// heat source while the run it belongs to carries steam.
/// </summary>
public class BlockPipeWarmth : Block, IHeatSource
{
    private FrostStoryModSystem? mod;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        mod = api.ModLoader.GetModSystem<FrostStoryModSystem>();
    }

    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        if (mod == null || !mod.WarmthOwners.TryGetValue(heatSourcePos, out var owner)) return 0f;
        if (!mod.SteamFor(world).IsLive(owner.Node)) return 0f;
        return mod.Config.PipeHeatStrength * (owner.Big ? 3f : 1f);
    }
}
