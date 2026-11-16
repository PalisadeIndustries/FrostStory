using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FrostStory.Panels;

/// <summary>
/// The building kit: thin panels that stand on the edges of a cell. A wall panel is a quarter of a block
/// thick against one side; a floor lies along the bottom, a ceiling along the top. One cell can hold a
/// panel on each of its six sides, so a corner cell carries two walls and a floor and the room inside
/// keeps its full size.
/// </summary>
public static class PanelKit
{
    public const float Wall = 0.25f;
    public const float Slab = 0.125f;

    public static bool IsFloor(string type) => type.StartsWith("floor");
    public static bool IsCeiling(string type) => type.StartsWith("ceiling");
    public static bool IsWall(string type) => !IsFloor(type) && !IsCeiling(type);
    public static bool IsDoor(string type) => type == "door";

    /// <summary>The part of the cell a panel on this side fills.</summary>
    public static Cuboidf BoxOf(BlockFacing side)
    {
        if (side == BlockFacing.NORTH) return new Cuboidf(0, 0, 0, 1, 1, Wall);
        if (side == BlockFacing.SOUTH) return new Cuboidf(0, 0, 1 - Wall, 1, 1, 1);
        if (side == BlockFacing.WEST) return new Cuboidf(0, 0, 0, Wall, 1, 1);
        if (side == BlockFacing.EAST) return new Cuboidf(1 - Wall, 0, 0, 1, 1, 1);
        if (side == BlockFacing.DOWN) return new Cuboidf(0, 0, 0, 1, Slab, 1);
        return new Cuboidf(0, 1 - Slab, 0, 1, 1, 1);
    }
}

/// <summary>A panel as it is carried. Setting it down adds it to the cell it is aimed at.</summary>
public class BlockPanel : Block
{
    public string Type => Variant["type"];

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var ba = world.BlockAccessor;
        var clicked = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position;
        var hit = blockSel.HitPosition;
        string type = Type;

        // First choice: the cell that was clicked, if it is a panel cell with room on the side aimed at.
        // Otherwise the open cell in front of the face that was clicked.
        BlockPos? target = null;
        BlockFacing? side = null;
        if (ba.GetBlockEntity(clicked) is BlockEntityPanels there)
        {
            side = SideFor(type, hit.X, hit.Z, null, there, 0.4);
            if (side != null && !(PanelKit.IsFloor(type) && blockSel.Face == BlockFacing.UP) && !(PanelKit.IsCeiling(type) && blockSel.Face == BlockFacing.DOWN)) target = clicked;
        }

        if (target == null)
        {
            target = clicked.AddCopy(blockSel.Face);
            var block = ba.GetBlock(target);
            var cell = ba.GetBlockEntity(target) as BlockEntityPanels;
            if (cell == null && !block.IsReplacableBy(this))
            {
                failureCode = "notreplaceable";
                return false;
            }

            var n = blockSel.Face.Normali;
            side = SideFor(type, hit.X - n.X, hit.Z - n.Z, blockSel.Face.IsHorizontal ? blockSel.Face.Opposite : null, cell, 2);
        }

        if (side == null)
        {
            failureCode = "froststory-panel-taken";
            return false;
        }

        if (byPlayer != null && !world.Claims.TryAccess(byPlayer, target, EnumBlockAccessFlags.BuildOrBreak))
        {
            failureCode = "claimed";
            return false;
        }

        if (world.Side != EnumAppSide.Server) return true;      // the server builds; the cell is sent back

        if (ba.GetBlockEntity(target) is not BlockEntityPanels panels)
        {
            var cellBlock = world.GetBlock(new AssetLocation("froststory", "panelcell"));
            if (cellBlock == null) return false;
            ba.SetBlock(cellBlock.Id, target);
            panels = ba.GetBlockEntity(target) as BlockEntityPanels;
            if (panels == null) return false;
        }

        panels.Set(side, type);
        world.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), target, 0, byPlayer);
        return true;
    }

    /// <summary>
    /// Which side of a cell a panel of this type goes on, for a point aimed at (in the cell's own
    /// coordinates). Floors and ceilings have one place. A wall goes on the nearest free side, never the
    /// one flat against the block that was clicked.
    /// </summary>
    private static BlockFacing? SideFor(string type, double x, double z, BlockFacing? against, BlockEntityPanels? cell, double within)
    {
        if (PanelKit.IsFloor(type)) return cell?.Has(BlockFacing.DOWN) == true ? null : BlockFacing.DOWN;
        if (PanelKit.IsCeiling(type)) return cell?.Has(BlockFacing.UP) == true ? null : BlockFacing.UP;

        x = Math.Clamp(x, 0, 1);
        z = Math.Clamp(z, 0, 1);
        BlockFacing? best = null;
        double nearest = within;
        foreach (var (side, distance) in new[] { (BlockFacing.NORTH, z), (BlockFacing.SOUTH, 1 - z), (BlockFacing.WEST, x), (BlockFacing.EAST, 1 - x) })
        {
            if (side == against || cell?.Has(side) == true || distance >= nearest) continue;
            nearest = distance;
            best = side;
        }

        return best;
    }
}

/// <summary>A cell that holds panels. What is there, and what is solid, comes from its block entity.</summary>
public class BlockPanelCell : Block
{
    private static readonly Cuboidf[] None = Array.Empty<Cuboidf>();

    private static BlockEntityPanels? At(IBlockAccessor ba, BlockPos pos) => ba.GetBlockEntity(pos) as BlockEntityPanels;

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) => At(blockAccessor, pos)?.Boxes(false) ?? None;

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) => At(blockAccessor, pos)?.Boxes(true) ?? None;

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        var panels = At(world.BlockAccessor, blockSel.Position);
        var side = panels?.SideOfBox(blockSel.SelectionBoxIndex);
        if (panels == null || side == null || !PanelKit.IsDoor(panels.TypeOn(side)!)) return base.OnBlockInteractStart(world, byPlayer, blockSel);

        if (world.Side == EnumAppSide.Server) panels.ToggleDoor(side);
        return true;
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        var panels = At(world.BlockAccessor, pos);
        if (panels == null || world.Side != EnumAppSide.Server)
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }

        // one panel at a time: the one aimed at; with nobody aiming, the lot
        var aimed = byPlayer?.CurrentBlockSelection;
        var side = aimed != null && aimed.Position.Equals(pos) ? panels.SideOfBox(aimed.SelectionBoxIndex) : null;
        bool creative = byPlayer?.WorldData.CurrentGameMode == EnumGameMode.Creative;
        foreach (var each in BlockFacing.ALLFACES)
        {
            if (side != null && each != side) continue;
            string? type = panels.TypeOn(each);
            if (type == null) continue;
            panels.Set(each, null);
            var item = world.GetBlock(new AssetLocation("froststory", "panel-" + type));
            if (item != null && !creative) world.SpawnItemEntity(new ItemStack(item), pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        world.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), pos, 0, byPlayer);
        if (panels.IsEmpty) world.BlockAccessor.SetBlock(0, pos);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f) => Array.Empty<ItemStack>();

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var panels = At(world.BlockAccessor, pos);
        foreach (var side in BlockFacing.ALLFACES)
        {
            string? type = panels?.TypeOn(side);
            var item = type == null ? null : world.GetBlock(new AssetLocation("froststory", "panel-" + type));
            if (item != null) return new ItemStack(item);
        }

        return base.OnPickBlock(world, pos);
    }

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) => Lang.Get("froststory:block-panelcell");

    /// <summary>A side with a panel on it keeps the warmth in, so a house of panels is a room.</summary>
    public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type)
    {
        return At(api.World.BlockAccessor, pos)?.Closed(facing) == true ? 1 : 0;
    }
}

public class BlockEntityPanels : BlockEntity
{
    private static readonly Vec3f BlockCentre = new(0.5f, 0.5f, 0.5f);

    private readonly string?[] types = new string?[6];
    private readonly bool[] open = new bool[6];
    private Cuboidf[]? solid, pick;
    private BlockFacing[]? order;

    public bool IsEmpty => Array.TrueForAll(types, t => t == null);
    public bool Has(BlockFacing side) => types[side.Index] != null;
    public string? TypeOn(BlockFacing side) => types[side.Index];

    /// <summary>Whether this side is shut: a panel that is not an open door.</summary>
    public bool Closed(BlockFacing side) => types[side.Index] != null && !open[side.Index];

    public void Set(BlockFacing side, string? type)
    {
        types[side.Index] = type;
        open[side.Index] = false;
        Changed();
    }

    public void ToggleDoor(BlockFacing side)
    {
        open[side.Index] = !open[side.Index];
        Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/door"), Pos, 0);
        Changed();
    }

    private void Changed()
    {
        solid = pick = null;
        MarkDirty(true);
    }

    /// <summary>The panels' boxes: for standing against (an open door has none), or for aiming at.</summary>
    public Cuboidf[] Boxes(bool collision)
    {
        if (solid == null || pick == null || order == null)
        {
            var all = new List<Cuboidf>();
            var shut = new List<Cuboidf>();
            var sides = new List<BlockFacing>();
            foreach (var side in BlockFacing.ALLFACES)
            {
                if (types[side.Index] == null) continue;
                var box = PanelKit.BoxOf(side);
                all.Add(box);
                sides.Add(side);
                if (!open[side.Index]) shut.Add(box);
            }

            pick = all.ToArray();
            solid = shut.ToArray();
            order = sides.ToArray();
        }

        return collision ? solid : pick;
    }

    /// <summary>The side the nth box from <see cref="Boxes"/> (for aiming) stands on.</summary>
    public BlockFacing? SideOfBox(int index)
    {
        Boxes(false);
        return order != null && index >= 0 && index < order.Length ? order[index] : null;
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        foreach (var side in BlockFacing.ALLFACES)
        {
            string? type = types[side.Index];
            if (type == null) continue;

            string name = "panel-" + type + (PanelKit.IsDoor(type) && open[side.Index] ? "-open" : "");
            var shape = Vintagestory.API.Common.Shape.TryGet(Api, new AssetLocation("froststory", $"shapes/block/generator/{name}.json"));
            if (shape == null) continue;
            tessThreadTesselator.TesselateShape(Block, shape, out var mesh);
            if (mesh == null) continue;

            // wall panels are drawn against the north side and turned to theirs
            float turn = side == BlockFacing.WEST ? 90 : side == BlockFacing.SOUTH ? 180 : side == BlockFacing.EAST ? 270 : 0;
            if (turn != 0) mesh.Rotate(BlockCentre, 0f, turn * GameMath.DEG2RAD, 0f);
            mesher.AddMeshData(mesh);
        }

        return true;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        foreach (var side in BlockFacing.ALLFACES)
        {
            if (types[side.Index] != null) dsc.AppendLine(Lang.Get("froststory:panel-on", Lang.Get("froststory:block-panel-" + types[side.Index]), Lang.Get("froststory:side-" + side.Code)));
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        for (int i = 0; i < 6; i++)
        {
            tree.SetString("p" + i, types[i] ?? "");
            tree.SetBool("o" + i, open[i]);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        for (int i = 0; i < 6; i++)
        {
            string type = tree.GetString("p" + i, "");
            types[i] = type.Length == 0 ? null : type;
            open[i] = tree.GetBool("o" + i);
        }

        solid = pick = null;
        if (Api?.Side == EnumAppSide.Client) Api.World.BlockAccessor.MarkBlockDirty(Pos);
    }
}
