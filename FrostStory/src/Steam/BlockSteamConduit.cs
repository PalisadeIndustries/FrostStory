using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FrostStory.Steam;

/// <summary>
/// Steam pipes, pipe fittings and the generator's steam ports. Pipes join end to end along their axis,
/// fittings join on every side, a port joins on its outlet. A pipe whose run reaches a live port is warm.
/// </summary>
public class BlockSteamConduit : Block, IHeatSource
{
    private readonly bool[] connects = new bool[6];
    private FrostStoryModSystem? mod;

    /// <summary>True for a generator port that is delivering steam.</summary>
    public bool IsLiveSource { get; private set; }

    public bool IsPort { get; private set; }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        mod = api.ModLoader.GetModSystem<FrostStoryModSystem>();

        switch (FirstCodePart())
        {
            case "steampipe":
                foreach (var face in BlockFacing.ALLFACES)
                {
                    connects[face.Index] = Variant["axis"] switch
                    {
                        "ud" => face.IsVertical,
                        "ns" => face.Axis == EnumAxis.Z,
                        _ => face.Axis == EnumAxis.X
                    };
                }

                break;

            case "steamport":
                IsPort = true;
                IsLiveSource = Variant["state"] == "live";
                var outlet = BlockFacing.FromCode(Variant["side"]);
                if (outlet != null) connects[outlet.Index] = true;
                break;

            default:
                for (int i = 0; i < 6; i++) connects[i] = true;
                break;
        }
    }

    public bool ConnectsOn(BlockFacing face) => connects[face.Index];

    private bool IsWarm(IWorldAccessor world, BlockPos pos)
    {
        var network = mod?.SteamFor(world);
        return network != null && (IsLiveSource || network.IsLive(pos));
    }

    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        return IsWarm(world, heatSourcePos) ? mod!.Config.PipeHeatStrength : 0f;
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        mod?.SteamFor(world)?.Invalidate();
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        base.OnBlockRemoved(world, pos);
        mod?.SteamFor(world)?.Invalidate();
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        var sb = new StringBuilder(base.GetPlacedBlockInfo(world, pos, forPlayer));
        if (sb.Length > 0 && sb[^1] != '\n') sb.AppendLine();

        string key = IsPort
            ? (IsLiveSource ? "froststory:steam-port-live" : "froststory:steam-port-cold")
            : (IsWarm(world, pos) ? "froststory:steam-pipe-live" : "froststory:steam-pipe-cold");
        sb.AppendLine(Lang.Get(key));
        return sb.ToString();
    }
}
