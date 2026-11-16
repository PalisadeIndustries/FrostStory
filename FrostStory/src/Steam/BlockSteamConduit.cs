using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FrostStory.Steam;

/// <summary>
/// Everything steam flows through: pipes and pipe fittings (one block), steam mains and main fittings
/// (two blocks across), and the generator's steam ports of both sizes.
///
/// A conduit joins its neighbour where both have an opening on the shared face. Two large conduits must
/// meet squarely, bore to bore. A small pipe may tap a main or a main fitting anywhere, and may butt
/// against the mouth of a large port. A conduit whose run reaches a live port is warm.
///
/// A large conduit lives in its minimum-corner cell; the rest of its volume is <see cref="BlockSteamMainFill"/>.
/// </summary>
public class BlockSteamConduit : Block, IHeatSource
{
    private readonly bool[] connects = new bool[6];
    private FrostStoryModSystem? mod;

    /// <summary>True for a generator port that is delivering steam.</summary>
    public bool IsLiveSource { get; private set; }

    public bool IsPort { get; private set; }

    /// <summary>True for the two-block-wide conduits.</summary>
    public bool IsBig { get; private set; }

    /// <summary>True for fittings and ports: the places pipe runs are strung between.</summary>
    public bool IsNode { get; private set; }

    /// <summary>The straight pieces pipe runs are drawn from (client only).</summary>
    public Vintagestory.API.Common.Shape? PipeSegment { get; private set; }
    public Vintagestory.API.Common.Shape? MainSegment { get; private set; }

    /// <summary>True for the posts of a main stand: they carry a fitting but no steam.</summary>
    public bool IsStructural { get; private set; }

    /// <summary>Cells occupied, counted from this block's own cell.</summary>
    public Vec3i Size { get; private set; } = new(1, 1, 1);

    /// <summary>Mains and main fittings place and remove their own filler cells; ports are laid by the blueprint.</summary>
    private bool managesFill;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        mod = api.ModLoader.GetModSystem<FrostStoryModSystem>();

        string kind = FirstCodePart();
        IsNode = kind is "pipejunction" or "mainjunction" or "steamport" or "mainport";
        if (IsNode && api.Side == EnumAppSide.Client)
        {
            PipeSegment = Vintagestory.API.Common.Shape.TryGet(api, new AssetLocation("froststory", "shapes/block/generator/pipeseg.json"));
            MainSegment = Vintagestory.API.Common.Shape.TryGet(api, new AssetLocation("froststory", "shapes/block/generator/mainseg.json"));
        }

        switch (kind)
        {
            case "steampipe":
                OpenAlong(Variant["axis"]);
                break;

            case "steammain":
                IsBig = managesFill = true;
                OpenAlong(Variant["axis"]);
                Size = Variant["axis"] switch
                {
                    "ud" => new Vec3i(2, 1, 2),
                    "ns" => new Vec3i(2, 2, 1),
                    _ => new Vec3i(1, 2, 2)
                };
                break;

            case "mainpost":
                IsBig = managesFill = IsStructural = true;
                Size = new Vec3i(2, 1, 2);
                break;

            case "mainjunction":
                IsBig = managesFill = true;
                Size = new Vec3i(2, 2, 2);
                for (int i = 0; i < 6; i++) connects[i] = true;
                break;

            case "steamport":
            case "mainport":
                IsPort = true;
                IsLiveSource = Variant["state"] == "live";
                var outlet = BlockFacing.FromCode(Variant["side"]);
                if (outlet != null) connects[outlet.Index] = true;
                if (FirstCodePart() == "mainport")
                {
                    IsBig = true;
                    Size = outlet?.Axis == EnumAxis.X ? new Vec3i(1, 2, 2) : new Vec3i(2, 2, 1);
                }

                break;

            default:
                for (int i = 0; i < 6; i++) connects[i] = true;
                break;
        }
    }

    private void OpenAlong(string axis)
    {
        foreach (var face in BlockFacing.ALLFACES)
        {
            connects[face.Index] = axis switch
            {
                "ud" => face.IsVertical,
                "ns" => face.Axis == EnumAxis.Z,
                _ => face.Axis == EnumAxis.X
            };
        }
    }

    public bool ConnectsOn(BlockFacing face) => connects[face.Index];

    /// <summary>Whether a small pipe may join on this face: anywhere on a main, only at the mouth of a port.</summary>
    public bool AcceptsTapOn(BlockFacing face) => IsBig && !IsStructural && (!IsPort || connects[face.Index]);

    public IEnumerable<BlockPos> Cells(BlockPos anchor)
    {
        for (int x = 0; x < Size.X; x++)
            for (int y = 0; y < Size.Y; y++)
                for (int z = 0; z < Size.Z; z++)
                    yield return anchor.AddCopy(x, y, z);
    }

    // ------------------------------------------------------------------ heat and info

    private bool IsWarm(IWorldAccessor world, BlockPos pos)
    {
        var network = mod?.SteamFor(world);
        return network != null && (IsLiveSource || network.IsLive(pos));
    }

    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        if (!IsWarm(world, heatSourcePos)) return 0f;
        return mod!.Config.PipeHeatStrength * (IsBig ? 3f : 1f);
    }

    public string SteamInfo(IWorldAccessor world, BlockPos pos)
    {
        string key = IsPort
            ? (IsLiveSource ? "froststory:steam-port-live" : "froststory:steam-port-cold")
            : (IsWarm(world, pos) ? "froststory:steam-pipe-live" : "froststory:steam-pipe-cold");
        return Lang.Get(key);
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        var sb = new StringBuilder(base.GetPlacedBlockInfo(world, pos, forPlayer));
        if (IsStructural) return sb.ToString();
        if (sb.Length > 0 && sb[^1] != '\n') sb.AppendLine();
        sb.AppendLine(SteamInfo(world, pos));
        return sb.ToString();
    }

    // ------------------------------------------------------------------ stringing runs

    /// <summary>
    /// Clicking a fitting or port with pipes in hand strings a run: first one end, then the other. Small
    /// pipes make a small run, steam mains a large one. (Sneak to set a pipe block against it instead.)
    /// </summary>
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        string? held = byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack?.Block?.FirstCodePart();
        if (!IsNode || (held != "steampipe" && held != "steammain"))
            return base.OnBlockInteractStart(world, byPlayer, blockSel);

        if (world.Side == EnumAppSide.Server && byPlayer is IServerPlayer player)
            SteamRuns.Click(world, player, blockSel.Position, held == "steammain");
        return true;
    }

    // ------------------------------------------------------------------ placing and removing

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (!managesFill) return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);

        // A main lies along the face it is set against.
        var block = this;
        if (FirstCodePart() == "steammain")
        {
            string axis = blockSel.Face.Axis switch { EnumAxis.Y => "ud", EnumAxis.Z => "ns", _ => "we" };
            block = world.GetBlock(CodeWithParts(axis)) as BlockSteamConduit ?? this;
        }

        var anchor = block.AnchorFor(world, blockSel);
        foreach (var cell in block.Cells(anchor))
        {
            if (!world.BlockAccessor.IsValidPos(cell) || !world.BlockAccessor.GetBlock(cell).IsReplacableBy(block))
            {
                failureCode = "notreplaceable";
                return false;
            }

            if (byPlayer != null && !world.Claims.TryAccess(byPlayer, cell, EnumBlockAccessFlags.BuildOrBreak))
            {
                failureCode = "claimed";
                return false;
            }
        }

        var sel = blockSel.Clone();
        sel.Position = anchor;
        return block.DoPlaceBlock(world, byPlayer, sel, itemstack);
    }

    /// <summary>
    /// Where this conduit's own cell goes for a click. Set against the open end of another large conduit it
    /// lines up bore to bore; otherwise it grows away from the face that was clicked.
    /// </summary>
    public BlockPos AnchorFor(IWorldAccessor world, BlockSelection blockSel)
    {
        var face = blockSel.Face;
        var target = blockSel.Position;
        var n = face.Normali;

        if (blockSel.DidOffset
            && SteamNetwork.Resolve(world.BlockAccessor, target.AddCopy(face.Opposite), out var against, out var againstAnchor)
            && against.IsBig && against.ConnectsOn(face) && ConnectsOn(face.Opposite))
        {
            int along = n.X + n.Y + n.Z > 0 ? Along(against.Size, face.Axis) : -Along(Size, face.Axis);
            var lined = againstAnchor.AddCopy(n.X * System.Math.Abs(along), n.Y * System.Math.Abs(along), n.Z * System.Math.Abs(along));
            if (SteamNetwork.MeetSquarely(against, againstAnchor, this, lined, face)) return lined;
        }

        // Grow away from the clicked face, so the conduit does not reach back into what it was set against.
        return target.AddCopy(
            n.X < 0 ? -(Size.X - 1) : 0,
            n.Y < 0 ? -(Size.Y - 1) : 0,
            n.Z < 0 ? -(Size.Z - 1) : 0);
    }

    private static int Along(Vec3i size, EnumAxis axis) => axis switch { EnumAxis.X => size.X, EnumAxis.Y => size.Y, _ => size.Z };

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);

        if (managesFill && world.Side == EnumAppSide.Server)
        {
            for (int x = 0; x < Size.X; x++)
                for (int y = 0; y < Size.Y; y++)
                    for (int z = 0; z < Size.Z; z++)
                    {
                        if (x + y + z == 0) continue;
                        var fill = world.GetBlock(BlockSteamMainFill.CodeFor(x, y, z));
                        if (fill != null) world.BlockAccessor.SetBlock(fill.Id, blockPos.AddCopy(x, y, z));
                    }
        }

        mod?.SteamFor(world)?.Invalidate();
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        base.OnBlockRemoved(world, pos);

        if (managesFill && world.Side == EnumAppSide.Server)
        {
            foreach (var cell in Cells(pos))
            {
                if (world.BlockAccessor.GetBlock(cell) is BlockSteamMainFill fill && cell.Equals(pos.AddCopy(fill.Offset.X, fill.Offset.Y, fill.Offset.Z)))
                    world.BlockAccessor.SetBlock(0, cell);
            }
        }

        mod?.SteamFor(world)?.Invalidate();
    }
}
