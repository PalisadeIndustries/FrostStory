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

    /// <summary>True for heating pipes: they lie on the ground, join whatever pipe is beside them, and radiate more.</summary>
    public bool IsHeatPipe { get; private set; }

    /// <summary>
    /// True for distributors: a steam main comes in at <see cref="Inlet"/> and four small pipes leave at
    /// <see cref="Outlet"/>, one from each quarter of that side.
    /// </summary>
    public bool IsDistributor { get; private set; }
    public BlockFacing Inlet { get; private set; } = BlockFacing.WEST;
    public BlockFacing Outlet { get; private set; } = BlockFacing.EAST;

    /// <summary>
    /// True for radiators: two blocks long, with a hookup behind each block at a right angle to its
    /// length (so it stands along the wall its pipe comes through), and much warmer than that pipe.
    /// </summary>
    public bool IsRadiator { get; private set; }
    public BlockFacing Back { get; private set; } = BlockFacing.NORTH;

    /// <summary>
    /// True for underfloor grates: a floor block to walk on, with a heating coil under the grating. Fed
    /// from underneath or from the side; grates laid side by side pass steam along.
    /// </summary>
    public bool IsFloorGrate { get; private set; }

    /// <summary>
    /// True for steam hubs: a heater that hangs from a steam main coming down onto it, and warms the
    /// open ground below and around (see <see cref="SteamHub"/>). It takes a main on top and nothing else.
    /// </summary>
    public bool IsHub { get; private set; }

    /// <summary>Blocks a conduit hangs below the part of it that steam flows through.</summary>
    private int hangs;

    /// <summary>
    /// For the frame under a steam hub: how many levels of it stand on four legs, one in each corner of
    /// a 4 x 4 square, the block's own cell being the minimum corner. Nothing fills the middle.
    /// </summary>
    private int legLevels;

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
        IsNode = kind is "pipejunction" or "mainjunction" or "steamport" or "mainport" or "distributor" or "wallpass" or "radiator" or "floorgrate" or "steamhub";
        if (IsNode && api.Side == EnumAppSide.Client)
        {
            PipeSegment = Vintagestory.API.Common.Shape.TryGet(api, new AssetLocation("froststory", "shapes/block/generator/pipeseg.json"));
            MainSegment = Vintagestory.API.Common.Shape.TryGet(api, new AssetLocation("froststory", "shapes/block/generator/mainseg.json"));
        }

        switch (kind)
        {
            case "steampipe":
            case "wallpass":      // a solid block of wall with a mount on each of two opposite sides
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

            case "heatpipe":
                IsHeatPipe = true;
                string joined = Variant["conn"] ?? "none";
                if (joined != "none")
                {
                    foreach (char side in joined) connects[BlockFacing.FromFirstLetter(side).Index] = true;
                }

                break;

            case "distributor":
                IsBig = managesFill = IsDistributor = true;
                Inlet = BlockFacing.FromCode(Variant["side"]) ?? BlockFacing.WEST;
                bool drop = Variant["type"] == "drop";
                Outlet = drop ? BlockFacing.DOWN : Inlet.Opposite;
                Size = drop ? new Vec3i(2, 2, 2) : Inlet.Axis == EnumAxis.X ? new Vec3i(1, 2, 2) : new Vec3i(2, 2, 1);
                connects[Inlet.Index] = true;      // only the inlet takes a main
                break;

            case "floorgrate":
                IsFloorGrate = true;
                foreach (var face in BlockFacing.ALLFACES) connects[face.Index] = face != BlockFacing.UP;
                break;

            case "hubframe":
                IsBig = managesFill = IsStructural = true;
                legLevels = Variant["type"] == "head" ? 2 : 1;
                break;

            case "steamhub":
                IsBig = managesFill = IsHub = true;
                Size = new Vec3i(2, 6, 2);
                connects[BlockFacing.UP.Index] = true;
                break;

            case "radiator":
                IsBig = managesFill = IsRadiator = true;      // no openings of its own: mains never join it
                Back = BlockFacing.FromCode(Variant["side"]) ?? BlockFacing.NORTH;
                Size = Back.Axis == EnumAxis.X ? new Vec3i(1, 1, 2) : new Vec3i(2, 1, 1);
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
    public bool AcceptsTapOn(BlockFacing face)
    {
        if (IsDistributor) return face == Outlet;
        if (IsHub) return false;      // a hub takes a main, nothing smaller
        if (IsRadiator) return face == BlockFacing.DOWN || face == Back;
        return IsBig && !IsStructural && (!IsPort || connects[face.Index]);
    }

    /// <summary>
    /// The places a pipe run of the given bore can be strung from on this fitting, in world coordinates.
    /// Most fittings offer the middle of each open side. A distributor offers its inlet to a main, and the
    /// four quarters of its outlet side to small runs.
    /// </summary>
    private AssetLocation FillCode(int x, int y, int z) =>
        IsRadiator ? new AssetLocation("froststory", $"radiatorfill-o{x}{y}{z}")
        : IsHub || legLevels > 0 ? new AssetLocation("froststory", y < 0 ? $"hubfill-d{x}{-y}{z}" : $"hubfill-o{x}{y}{z}")
        : BlockSteamMainFill.CodeFor(x, y, z);

    /// <summary>Every cell the conduit fills, including anything hanging below the part steam flows through.</summary>
    public IEnumerable<BlockPos> Bulk(BlockPos anchor)
    {
        foreach (var o in BulkOffsets()) yield return anchor.AddCopy(o.X, o.Y, o.Z);
    }

    private IEnumerable<Vec3i> BulkOffsets()
    {
        if (legLevels > 0)
        {
            for (int y = 0; y < legLevels; y++)
                for (int x = 0; x <= 3; x += 3)
                    for (int z = 0; z <= 3; z += 3)
                        yield return new Vec3i(x, y, z);
            yield break;
        }

        for (int x = 0; x < Size.X; x++)
            for (int y = -hangs; y < Size.Y; y++)
                for (int z = 0; z < Size.Z; z++)
                    yield return new Vec3i(x, y, z);
    }

    public IEnumerable<(BlockFacing Face, Vec3d Point)> RunPoints(BlockPos anchor, bool big)
    {
        if (IsHub && !big) yield break;

        if (IsFloorGrate)
        {
            // one run, to the mount underneath
            if (!big) yield return (BlockFacing.DOWN, SteamRuns.AttachPoint(this, anchor, BlockFacing.DOWN));
            yield break;
        }

        if (IsRadiator)
        {
            // a small run to the hookup behind each of its two blocks
            if (big) yield break;
            var behind = SteamRuns.AttachPoint(this, anchor, Back);
            for (int i = -1; i <= 1; i += 2)
            {
                yield return (Back, Back.Axis == EnumAxis.X ? behind.AddCopy(0, 0, i * 0.5) : behind.AddCopy(i * 0.5, 0, 0));
            }

            yield break;
        }

        if (!IsDistributor)
        {
            foreach (var face in BlockFacing.ALLFACES)
            {
                if (connects[face.Index]) yield return (face, SteamRuns.AttachPoint(this, anchor, face));
            }

            yield break;
        }

        if (big)
        {
            yield return (Inlet, SteamRuns.AttachPoint(this, anchor, Inlet));
            yield break;
        }

        // one outlet in the middle of each of the four cells on the outlet side
        var mid = SteamRuns.AttachPoint(this, anchor, Outlet);
        var n = Outlet.Normali;
        for (int i = -1; i <= 1; i += 2)
        {
            for (int j = -1; j <= 1; j += 2)
            {
                yield return (Outlet, n.Y != 0 ? mid.AddCopy(i * 0.5, 0, j * 0.5) : n.X != 0 ? mid.AddCopy(0, i * 0.5, j * 0.5) : mid.AddCopy(j * 0.5, i * 0.5, 0));
            }
        }
    }

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
        return mod!.Config.PipeHeatStrength * (IsRadiator || IsHub ? 5f : IsBig || IsFloorGrate ? 3f : IsHeatPipe ? 2f : 1f);
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
        if (FirstCodePart() == "wallpass" && byPlayer != null)
        {
            // A passthrough goes through the wall the way the player is looking: one mount towards them, one away.
            var look = SuggestedHVOrientation(byPlayer, blockSel);
            string through = look[1] != null ? "ud" : look[0].Axis == EnumAxis.Z ? "ns" : "we";
            var turned = world.GetBlock(CodeWithVariant("axis", through)) ?? this;
            return turned.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode) && turned.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
        }

        if (!managesFill) return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);

        // A main lies along the face it is set against.
        var block = this;
        if (FirstCodePart() == "steammain")
        {
            string axis = blockSel.Face.Axis switch { EnumAxis.Y => "ud", EnumAxis.Z => "ns", _ => "we" };
            block = world.GetBlock(CodeWithParts(axis)) as BlockSteamConduit ?? this;
        }
        else if (IsRadiator && byPlayer != null)
        {
            // A radiator backs onto the wall it is set against; set on a floor, it backs away from the player.
            var back = blockSel.Face.IsHorizontal ? blockSel.Face.Opposite : SuggestedHVOrientation(byPlayer, blockSel)[0].Opposite;
            block = world.GetBlock(CodeWithVariant("side", back.Code)) as BlockSteamConduit ?? this;
        }
        else if (IsDistributor)
        {
            // A distributor's inlet faces what it is set against; set on a floor or ceiling, it faces the player.
            var inlet = blockSel.Face.IsHorizontal ? blockSel.Face.Opposite : SuggestedHVOrientation(byPlayer, blockSel)[0];
            block = world.GetBlock(CodeWithVariant("side", inlet.Code)) as BlockSteamConduit ?? this;
        }

        var anchor = block.AnchorFor(world, blockSel);
        foreach (var cell in block.Bulk(anchor))
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

    // ------------------------------------------------------------------ heating pipes join their neighbours

    /// <summary>
    /// Works out which sides a heating pipe at this position should be open on (wherever there is another
    /// heating pipe, or a pipe, fitting or port that opens towards it) and swaps in the matching variant.
    /// </summary>
    private void RefreshHeatPipe(IWorldAccessor world, BlockPos pos, BlockPos? gone = null)
    {
        var ba = world.BlockAccessor;
        string joined = "";
        foreach (var face in BlockFacing.HORIZONTALS)
        {
            var next = pos.AddCopy(face);
            if (gone != null && next.Equals(gone)) continue;
            if (!SteamNetwork.Resolve(ba, next, out var neighbour, out _)) continue;

            bool opens = neighbour.IsHeatPipe || (neighbour.IsBig ? neighbour.AcceptsTapOn(face.Opposite) : neighbour.ConnectsOn(face.Opposite));
            if (opens) joined += face.Code[0];
        }

        var wanted = world.GetBlock(CodeWithVariant("conn", joined.Length == 0 ? "none" : joined));
        if (wanted == null || wanted.Id == ba.GetBlock(pos).Id) return;

        ba.ExchangeBlock(wanted.Id, pos);
        ba.MarkBlockDirty(pos);
        mod?.SteamFor(world)?.Invalidate();
    }

    /// <summary>Lets the heating pipes beside a position take account of what has just changed there.</summary>
    private static void RefreshHeatPipesAround(IWorldAccessor world, BlockPos pos, bool gone)
    {
        foreach (var face in BlockFacing.HORIZONTALS)
        {
            var next = pos.AddCopy(face);
            if (world.BlockAccessor.GetBlock(next) is BlockSteamConduit { IsHeatPipe: true } pipe) pipe.RefreshHeatPipe(world, next, gone ? pos : null);
        }
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (IsHeatPipe && world.Side == EnumAppSide.Server) RefreshHeatPipe(world, pos);
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);

        if (world.Side == EnumAppSide.Server)
        {
            if (IsHeatPipe) RefreshHeatPipe(world, blockPos);
            RefreshHeatPipesAround(world, blockPos, false);
        }

        if (managesFill && world.Side == EnumAppSide.Server)
        {
            foreach (var o in BulkOffsets())
            {
                if (o.X == 0 && o.Y == 0 && o.Z == 0) continue;
                var fill = world.GetBlock(FillCode(o.X, o.Y, o.Z));
                if (fill != null) world.BlockAccessor.SetBlock(fill.Id, blockPos.AddCopy(o.X, o.Y, o.Z));
            }
        }

        mod?.SteamFor(world)?.Invalidate();
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        base.OnBlockRemoved(world, pos);
        if (world.Side == EnumAppSide.Server) RefreshHeatPipesAround(world, pos, true);

        if (managesFill && world.Side == EnumAppSide.Server)
        {
            foreach (var cell in Bulk(pos))
            {
                if (world.BlockAccessor.GetBlock(cell) is BlockSteamMainFill fill && cell.Equals(pos.AddCopy(fill.Offset.X, fill.Offset.Y, fill.Offset.Z)))
                    world.BlockAccessor.SetBlock(0, cell);
            }
        }

        mod?.SteamFor(world)?.Invalidate();
    }
}
