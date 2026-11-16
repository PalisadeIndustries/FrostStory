using System;
using System.Collections.Generic;
using System.Text;
using FrostStory.Steam;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FrostStory.Coal;

/// <summary>What counts as coal for the generator's bunker and the coal heap.</summary>
public static class CoalItems
{
    private static readonly HashSet<string> Fuel = new() { "charcoal", "coke", "ore-bituminouscoal", "ore-lignite", "ore-anthracite" };

    /// <summary>What a heap hands out and a borer raises.</summary>
    public static readonly AssetLocation Raised = new("game", "ore-bituminouscoal");

    public static bool IsCoal(CollectibleObject? item) => item?.Code != null && item.Code.Domain == "game" && Fuel.Contains(item.Code.Path);

    /// <summary>True for coal still in the rock.</summary>
    public static bool IsCoalOre(Block block)
    {
        string? path = block.Code?.Path;
        return path != null && path.StartsWith("ore-") && (path.Contains("bituminouscoal") || path.Contains("lignite") || path.Contains("anthracite"));
    }
}

/// <summary>
/// A heap of coal. One block holds any amount; the heap is drawn bigger as it fills. Take coal out
/// with an empty hand, put it in with coal in hand. Bucket conveyors draw from it.
/// </summary>
public class BlockCoalHeap : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityCoalHeap heap) return false;
        if (world.Side != EnumAppSide.Server) return true;

        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (slot?.Itemstack != null && CoalItems.IsCoal(slot.Itemstack.Collectible))
        {
            heap.Add(slot.StackSize);
            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) { slot.TakeOutWhole(); slot.MarkDirty(); }
        }
        else if (slot?.Itemstack == null)
        {
            heap.GiveTo(byPlayer, 64);
        }

        return true;
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        // a big heap is dug away a stack at a time; the block goes with the last of it
        if (world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityCoalHeap heap)
        {
            heap.Spill(64);
            if (heap.Count > 0) return;
        }

        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }
}

public class BlockEntityCoalHeap : BlockEntity
{
    public int Count { get; private set; }

    public void Add(int amount)
    {
        if (amount <= 0) return;
        Count = (int)Math.Min(int.MaxValue, (long)Count + amount);
        MarkDirty(true);
    }

    /// <summary>Takes up to <paramref name="amount"/> out and says how much came.</summary>
    public int Take(int amount)
    {
        int taken = Math.Clamp(amount, 0, Count);
        if (taken == 0) return 0;
        Count -= taken;
        MarkDirty(true);
        return taken;
    }

    public void GiveTo(IPlayer player, int amount)
    {
        var item = Api.World.GetItem(CoalItems.Raised);
        int taken = Take(amount);
        if (item == null || taken == 0) return;
        var stack = new ItemStack(item, taken);
        if (!player.InventoryManager.TryGiveItemstack(stack, true)) Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.8, 0.5));
    }

    public void Spill(int amount)
    {
        var item = Api.World.GetItem(CoalItems.Raised);
        int taken = Take(amount);
        if (item != null && taken > 0) Api.World.SpawnItemEntity(new ItemStack(item, taken), Pos.ToVec3d().Add(0.5, 0.8, 0.5));
    }

    /// <summary>How big the heap is drawn: one for a few lumps, growing with the cube root of what it holds.</summary>
    public float Scale => Math.Clamp(0.6f + 0.3f * MathF.Cbrt(Count / 32f), 0.6f, 6f);

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        var shape = Vintagestory.API.Common.Shape.TryGet(Api, new AssetLocation("froststory", "shapes/block/generator/coalheap.json"));
        if (shape == null) return false;
        tessThreadTesselator.TesselateShape(Block, shape, out var mesh);
        if (mesh == null) return false;

        float s = Scale;
        mesh.Scale(new Vec3f(0.5f, 0f, 0.5f), s, s, s);
        mesher.AddMeshData(mesh);
        return true;       // in place of the block's own shape
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        dsc.AppendLine(Lang.Get("froststory:heap-count", Count));
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("count", Count);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        int before = Count;
        Count = tree.GetInt("count");
        if (Api?.Side == EnumAppSide.Client && before != Count) Api.World.BlockAccessor.MarkBlockDirty(Pos);
    }
}

/// <summary>
/// A steam borer raises coal onto a heap beside it for as long as it has steam. It raises a fixed
/// amount an hour, and more for coal in the rock round it: that is counted once, when the borer is set
/// down, and kept, so mining the seam afterwards costs nothing and the seam is never used up.
/// </summary>
public class BlockEntityBorer : BlockEntity
{
    public const int SurveyRadius = 12;
    public const int SurveyDepth = 32;

    /// <summary>Coal ore blocks found when it was set down; -1 until it has looked.</summary>
    public int OreCount { get; private set; } = -1;

    private float raised;          // part of a lump not yet on the heap
    private double lastHours;
    private bool blocked;

    private FrostStoryModSystem Mod => Api.ModLoader.GetModSystem<FrostStoryModSystem>();

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side != EnumAppSide.Server) return;

        lastHours = api.World.Calendar.TotalHours;
        if (OreCount < 0) RegisterDelayedCallback(_ => Survey(), 200);
        RegisterGameTickListener(_ =>
        {
            double now = Api.World.Calendar.TotalHours;
            Advance(Math.Clamp(now - lastHours, 0, 1));
            lastHours = now;
        }, 5000);
    }

    /// <summary>Counts the coal in the rock below and around. Done once; the answer is kept.</summary>
    public void Survey()
    {
        int found = 0;
        var min = Pos.AddCopy(-SurveyRadius, -SurveyDepth, -SurveyRadius);
        var max = Pos.AddCopy(SurveyRadius + 1, 3, SurveyRadius + 1);
        min.Y = Math.Max(1, min.Y);
        Api.World.BlockAccessor.WalkBlocks(min, max, (block, x, y, z) => { if (CoalItems.IsCoalOre(block)) found++; });
        OreCount = found;
        MarkDirty(true);
    }

    /// <summary>Lumps an hour with all the steam it draws.</summary>
    public float RatePerHour
    {
        get
        {
            var config = Mod.Config;
            return config.BorerCoalPerHour + config.BorerCoalPerOre * Math.Min(Math.Max(OreCount, 0), config.BorerOreCap);
        }
    }

    public float Fed => Mod.SteamFor(Api.World).Ratio(Pos);

    public void Advance(double hours)
    {
        if (hours <= 0) return;
        raised += (float)(RatePerHour * Fed * hours);
        int whole = (int)raised;
        if (whole <= 0) return;

        var heap = Heap();
        bool was = blocked;
        blocked = heap == null;
        if (heap != null)
        {
            heap.Add(whole);
            raised -= whole;
        }
        else
        {
            raised = Math.Min(raised, 1f);      // nowhere to put it: it stops, it does not save up
        }

        if (was != blocked) MarkDirty();
    }

    /// <summary>The heap beside the borer: east of it first, then the other sides. Starts one where there is room.</summary>
    private BlockEntityCoalHeap? Heap()
    {
        var ba = Api.World.BlockAccessor;
        var spots = new[] { Pos.AddCopy(2, 0, 0), Pos.AddCopy(2, 0, 1), Pos.AddCopy(-1, 0, 0), Pos.AddCopy(-1, 0, 1), Pos.AddCopy(0, 0, 2), Pos.AddCopy(1, 0, 2), Pos.AddCopy(0, 0, -1), Pos.AddCopy(1, 0, -1) };
        foreach (var spot in spots)
        {
            if (ba.GetBlockEntity(spot) is BlockEntityCoalHeap heap) return heap;
        }

        var block = Api.World.GetBlock(new AssetLocation("froststory", "coalheap"));
        if (block == null) return null;
        foreach (var spot in spots)
        {
            if (ba.GetBlock(spot).Id != 0 && ba.GetBlock(spot) is not BlockPipeWarmth) continue;
            ba.SetBlock(block.Id, spot);
            return ba.GetBlockEntity(spot) as BlockEntityCoalHeap;
        }

        return null;
    }

    public string Summary()
    {
        var sb = new StringBuilder();
        Describe(sb);
        return sb.ToString().Replace('\n', ' ').Trim();
    }

    private void Describe(StringBuilder sb)
    {
        float fed = Api.Side == EnumAppSide.Server || OreCount >= 0 ? Fed : 0f;
        sb.AppendLine(Lang.Get("froststory:borer-survey", Math.Max(OreCount, 0)));
        sb.AppendLine(Lang.Get("froststory:borer-rate", Math.Round(RatePerHour * fed, 1), Math.Round(RatePerHour, 1)));
        if (fed <= 0f) sb.AppendLine(Lang.Get("froststory:borer-nosteam"));
        if (blocked) sb.AppendLine(Lang.Get("froststory:borer-blocked"));
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        Describe(dsc);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("oreCount", OreCount);
        tree.SetFloat("raised", raised);
        tree.SetBool("blocked", blocked);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        OreCount = tree.GetInt("oreCount", -1);
        raised = tree.GetFloat("raised");
        blocked = tree.GetBool("blocked");
    }
}

/// <summary>
/// Bucket conveyors carry coal from heaps to whatever they end at. Any conveyor touching another is the
/// same line. Whoever is at the end asks the line, and it draws from every heap the line touches.
/// </summary>
public static class Conveyors
{
    private const int MaxLine = 512;

    public static bool IsConveyor(Block block) => block.Code?.Domain == "froststory" && block.Code.Path.StartsWith("bucketconveyor");

    /// <summary>Draws up to <paramref name="amount"/> coal along the conveyor lines that touch <paramref name="end"/>.</summary>
    public static int Collect(IWorldAccessor world, BlockPos end, int amount)
    {
        if (amount <= 0) return 0;
        var ba = world.BlockAccessor;
        var seen = new HashSet<BlockPos> { end.Copy() };
        var line = new Queue<BlockPos>();
        var heaps = new List<BlockEntityCoalHeap>();
        line.Enqueue(end.Copy());

        while (line.Count > 0 && seen.Count < MaxLine)
        {
            var at = line.Dequeue();
            foreach (var face in BlockFacing.ALLFACES)
            {
                var next = at.AddCopy(face);
                if (!seen.Add(next) || ba.GetChunkAtBlockPos(next) == null) continue;
                if (IsConveyor(ba.GetBlock(next))) line.Enqueue(next);
                else if (!at.Equals(end) && ba.GetBlockEntity(next) is BlockEntityCoalHeap heap) heaps.Add(heap);
            }
        }

        int got = 0;
        foreach (var heap in heaps)
        {
            got += heap.Take(amount - got);
            if (got >= amount) break;
        }

        return got;
    }
}
