using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace FrostStory.Steam;

/// <summary>
/// A steam engine many blocks in size: a mill engine, or bigger. The whole engine is drawn by this one
/// block, in the minimum corner of its bounds; every other cell it fills is an unseen, solid
/// <see cref="BlockEngineFill"/>. One of those cells, where the crankshaft comes out, is a
/// <see cref="BlockSteamEngineShaft"/>: that cell is the engine's place on the mechanical power network,
/// and its flywheel and shaft are drawn turning about it.
///
/// Steam comes in at one place only, the inlet on top: a steam main set on it, a pipe stood on it, or
/// a pipe run strung to it.
///
/// Everything about the shape comes from the block's attributes (per <c>side</c>, the side the shaft
/// comes out of), so another mod can add an engine of its own with JSON and models alone:
/// <code>
/// "engine": {
///   "size": [9, 5, 3],              bounds, in blocks
///   "cells": "000 100 ...",         the solid cells, as xyz offsets from this block (digits 0-9)
///   "shaft": [2, 2, 0],             the cell the shaft comes out of
///   "inlet": { "x": [6, 7], "z": [1, 2], "y": 4 },  cells a main or pipe stands on, and the level it stands at
///   "shaftBlock": "mymod:myshaft-north-{toward}",    the shaft cell's block ({toward} filled in); it carries the power
///   "fillBlock": "froststory:enginefill-{toward}"    optional: the other cells
/// }
/// </code>
/// </summary>
public class BlockLargeSteamEngine : BlockSteamConduit
{
    public BlockFacing Shaft { get; private set; } = BlockFacing.NORTH;
    public Vec3i ShaftCell { get; private set; } = new(0, 0, 0);

    private Vec3i[] occupied = Array.Empty<Vec3i>();
    private string shaftBlock = "";
    private string fillBlock = "froststory:enginefill-{toward}";
    private int inletX0, inletX1, inletZ0, inletZ1, inletY;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        Shaft = BlockFacing.FromCode(Variant["side"]) ?? BlockFacing.NORTH;
        IsBig = true;
        IsNode = true;
        OpenAllBut(BlockFacing.DOWN);      // only the inlet joins; see JoinsAt

        var spec = Attributes?["engine"];
        if (spec == null || !spec.Exists)
        {
            api.Logger.Error("[froststory] {0} has no engine attributes; it will not work.", Code);
            return;
        }

        var size = spec["size"].AsArray<int>(new[] { 1, 1, 1 });
        Size = new Vec3i(size[0], size[1], size[2]);
        var shaft = spec["shaft"].AsArray<int>(new[] { 0, 0, 0 });
        ShaftCell = new Vec3i(shaft[0], shaft[1], shaft[2]);

        var cells = new List<Vec3i>();
        foreach (var token in (spec["cells"].AsString("") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length == 3) cells.Add(new Vec3i(token[0] - '0', token[1] - '0', token[2] - '0'));
        }

        occupied = cells.ToArray();

        var inlet = spec["inlet"];
        var ix = inlet["x"].AsArray<int>(new[] { 0, 0 });
        var iz = inlet["z"].AsArray<int>(new[] { 0, 0 });
        (inletX0, inletX1, inletZ0, inletZ1) = (ix[0], ix[1], iz[0], iz[1]);
        inletY = inlet["y"].AsInt(Size.Y);
        shaftBlock = spec["shaftBlock"].AsString(shaftBlock);
        fillBlock = spec["fillBlock"].AsString(fillBlock);
    }

    // ------------------------------------------------------------------ steam

    /// <summary>Steam only joins at the inlet, so only its cells need be looked round.</summary>
    public override IEnumerable<BlockPos> Cells(BlockPos anchor)
    {
        for (int x = inletX0; x <= inletX1; x++)
            for (int z = inletZ0; z <= inletZ1; z++)
                yield return anchor.AddCopy(x, inletY - 1, z);
    }

    private bool OnInlet(BlockPos anchor, BlockPos cell)
    {
        int x = cell.X - anchor.X, y = cell.Y - anchor.Y, z = cell.Z - anchor.Z;
        return y == inletY - 1 && x >= inletX0 && x <= inletX1 && z >= inletZ0 && z <= inletZ1;
    }

    /// <summary>
    /// Whether a conduit beside one of this engine's cells is joined to it: it must stand on the inlet,
    /// and a main or main fitting must stand squarely within it.
    /// </summary>
    public bool JoinsAt(BlockPos anchor, BlockPos cell, BlockFacing face, BlockSteamConduit other, BlockPos otherAnchor)
    {
        if (other is BlockLargeSteamEngine || face != BlockFacing.UP || !OnInlet(anchor, cell)) return false;
        if (!other.ConnectsOn(BlockFacing.DOWN)) return false;
        if (!other.IsBig) return true;

        int x0 = otherAnchor.X - anchor.X, z0 = otherAnchor.Z - anchor.Z;
        return otherAnchor.Y - anchor.Y == inletY
               && x0 >= inletX0 && x0 + other.Size.X - 1 <= inletX1
               && z0 >= inletZ0 && z0 + other.Size.Z - 1 <= inletZ1;
    }

    /// <summary>Where a main or main fitting set on the inlet goes: square on it.</summary>
    public BlockPos? InletAnchorFor(BlockPos anchor, BlockFacing face, BlockSteamConduit placing)
    {
        if (face != BlockFacing.UP || !placing.IsBig) return null;
        int spareX = inletX1 - inletX0 + 1 - placing.Size.X, spareZ = inletZ1 - inletZ0 + 1 - placing.Size.Z;
        if (spareX < 0 || spareZ < 0) return null;
        return anchor.AddCopy(inletX0 + spareX / 2, inletY, inletZ0 + spareZ / 2);
    }

    public override IEnumerable<(BlockFacing Face, Vec3d Point)> RunPoints(BlockPos anchor, bool big)
    {
        yield return (BlockFacing.UP, new Vec3d(anchor.X + (inletX0 + inletX1 + 1) / 2.0, anchor.Y + inletY, anchor.Z + (inletZ0 + inletZ1 + 1) / 2.0));
    }

    // ------------------------------------------------------------------ placing and removing

    /// <summary>
    /// Click the ground where the shaft should come out: the engine stands beyond that spot, away from
    /// you, with its shaft over it pointing back at you.
    /// </summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var towardsPlayer = byPlayer != null ? SuggestedHVOrientation(byPlayer, blockSel)[0] : Shaft;
        if (world.GetBlock(CodeWithVariant("side", towardsPlayer.Code)) is not BlockLargeSteamEngine turned) return false;

        var target = blockSel.Position;
        var anchor = target.AddCopy(-turned.ShaftCell.X, 0, -turned.ShaftCell.Z);
        foreach (var cell in turned.AllCells(anchor))
        {
            if (!world.BlockAccessor.IsValidPos(cell) || !world.BlockAccessor.GetBlock(cell).IsReplacableBy(turned))
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
        return turned.DoPlaceBlock(world, byPlayer, sel, itemstack);
    }

    /// <summary>This block's own cell and every solid cell of the engine.</summary>
    public IEnumerable<BlockPos> AllCells(BlockPos anchor)
    {
        yield return anchor.Copy();
        foreach (var o in occupied)
        {
            if (o.X != 0 || o.Y != 0 || o.Z != 0) yield return anchor.AddCopy(o.X, o.Y, o.Z);
        }
    }

    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        if (world.Side == EnumAppSide.Server) LayCells(world, blockPos);
        base.OnBlockPlaced(world, blockPos, byItemStack);
    }

    /// <summary>
    /// Fills the engine's cells, each pointing at a neighbour one step nearer this block, so any of them
    /// can find its way here. The shaft's cell gets the shaft block.
    /// </summary>
    private void LayCells(IWorldAccessor world, BlockPos anchor)
    {
        var solid = new HashSet<Vec3i>(occupied);
        var reached = new HashSet<Vec3i> { new(0, 0, 0) };
        var queue = new Queue<Vec3i>();
        queue.Enqueue(new Vec3i(0, 0, 0));

        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var face in BlockFacing.ALLFACES)
            {
                var next = new Vec3i(at.X + face.Normali.X, at.Y + face.Normali.Y, at.Z + face.Normali.Z);
                if (!solid.Contains(next) || !reached.Add(next)) continue;
                queue.Enqueue(next);

                // it points back the way it was reached
                var toward = face.Opposite.Code;
                bool shaft = next.X == ShaftCell.X && next.Y == ShaftCell.Y && next.Z == ShaftCell.Z;
                var code = AssetLocation.Create((shaft ? shaftBlock : fillBlock).Replace("{toward}", toward), Code.Domain);
                var block = world.GetBlock(code);
                if (block != null) world.BlockAccessor.SetBlock(block.Id, anchor.AddCopy(next.X, next.Y, next.Z));
            }
        }

        int stray = 0;
        foreach (var cell in solid) if (!reached.Contains(cell)) stray++;
        if (stray > 0) world.Logger.Warning("[froststory] {0}: {1} of its cells are not joined to the rest and were left empty.", Code, stray);
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side == EnumAppSide.Server)
        {
            foreach (var cell in AllCells(pos))
            {
                if (!cell.Equals(pos) && world.BlockAccessor.GetBlock(cell) is BlockEngineFill) world.BlockAccessor.SetBlock(0, cell);
            }
        }

        base.OnBlockRemoved(world, pos);
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        return new ItemStack(world.GetBlock(CodeWithVariant("side", "north")) ?? this);
    }

    // ------------------------------------------------------------------ information

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        var text = new StringBuilder(base.GetPlacedBlockInfo(world, pos, forPlayer));
        var shaft = world.BlockAccessor.GetBlockEntity(pos.AddCopy(ShaftCell.X, ShaftCell.Y, ShaftCell.Z));
        shaft?.GetBehavior<BEBehaviorSteamEngine>()?.GetBlockInfo(forPlayer, text);
        return text.ToString().TrimEnd();
    }
}

/// <summary>
/// One unseen, solid cell of a large engine. Instead of an offset it holds the side towards a neighbour
/// one step nearer the engine's own block, so an engine of any size needs only six of these.
/// </summary>
public class BlockEngineFill : BlockSteamMainFill
{
    public BlockFacing Toward { get; private set; } = BlockFacing.DOWN;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        Toward = BlockFacing.FromCode(Variant["toward"]) ?? BlockFacing.DOWN;
    }

    public override BlockPos OwnerPos(IBlockAccessor ba, BlockPos pos)
    {
        var at = pos.Copy();
        for (int i = 0; i < 64; i++)
        {
            if (ba.GetBlock(at) is not BlockEngineFill fill) return at;
            at.Add(fill.Toward);
        }

        return at;
    }
}

/// <summary>
/// The cell of a large engine its crankshaft comes out of: part of the engine, and its place on the
/// mechanical power network. Its entity carries the engine's <see cref="BEBehaviorSteamEngine"/>.
/// </summary>
public class BlockSteamEngineShaft : BlockEngineFill, IMechanicalPowerBlock, IMPPowered
{
    public BlockFacing Shaft { get; private set; } = BlockFacing.NORTH;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        Shaft = BlockFacing.FromCode(Variant["side"]) ?? BlockFacing.NORTH;
    }

    public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos) =>
        (world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>() as IMechanicalPowerDevice)?.Network;

    public bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock) => face == Shaft;

    public void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {
    }
}
