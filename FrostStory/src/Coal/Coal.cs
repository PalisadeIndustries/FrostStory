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
/// One block of a coal pile. It holds up to 512 lumps in eight layers and is as tall as it is full.
/// Take coal out with an empty hand, put it on with coal in hand; coal put on a pile rolls down to
/// where the pile is lowest, so piles grow into heaps of any size. Bucket conveyors draw from them.
/// </summary>
public class BlockCoalHeap : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityCoalHeap) return false;
        if (world.Side != EnumAppSide.Server) return true;

        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (slot?.Itemstack != null && CoalItems.IsCoal(slot.Itemstack.Collectible))
        {
            int left = CoalPile.Add(world, blockSel.Position, slot.StackSize);
            if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) { slot.TakeOut(slot.StackSize - left); slot.MarkDirty(); }
        }
        else if (slot?.Itemstack == null)
        {
            int taken = CoalPile.TakeFromColumn(world, blockSel.Position, 64);
            var item = world.GetItem(CoalItems.Raised);
            if (item != null && taken > 0)
            {
                var stack = new ItemStack(item, taken);
                if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true)) world.SpawnItemEntity(stack, blockSel.Position.ToVec3d().Add(0.5, 1, 0.5));
            }
        }

        return true;
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        var item = world.GetItem(CoalItems.Raised);
        int count = (world.BlockAccessor.GetBlockEntity(pos) as BlockEntityCoalHeap)?.Count ?? 0;
        var drops = new List<ItemStack>();
        while (item != null && count > 0)
        {
            int n = Math.Min(count, item.MaxStackSize);
            drops.Add(new ItemStack(item, n));
            count -= n;
        }

        return drops.ToArray();
    }
}

public class BlockEntityCoalHeap : BlockEntity
{
    public int Count { get; private set; }

    /// <summary>Sets what this block holds and makes it the right height; an empty one goes.</summary>
    public void Set(int count)
    {
        Count = Math.Clamp(count, 0, CoalPile.PerBlock);
        var ba = Api.World.BlockAccessor;
        if (Count == 0)
        {
            ba.SetBlock(0, Pos);
            return;
        }

        var wanted = Api.World.GetBlock(new AssetLocation("froststory", "coalheap-" + CoalPile.Layers(Count)));
        if (wanted != null && wanted.Id != Block.Id) ba.ExchangeBlock(wanted.Id, Pos);
        MarkDirty(true);
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
        Count = tree.GetInt("count");
    }
}

/// <summary>
/// Coal piles: how coal put on a pile finds its place, and how it is drawn off. A pile is made of heap
/// blocks, each up to eight layers of 64. Coal put on rolls downhill until the slope is gentle, so a
/// pile fed at one spot grows into a cone over whatever ground it stands on. Coal drawn off comes from
/// the top of the pile.
/// </summary>
public static class CoalPile
{
    public const int PerLayer = 64;
    public const int PerBlock = PerLayer * 8;
    private const int Slope = 5;          // layers a column may stand above its neighbour before coal rolls
    private const int DiagonalSlope = 7;
    private const int MaxPile = 8192;     // heap blocks followed when drawing off

    public static int Layers(int count) => Math.Clamp((count + PerLayer - 1) / PerLayer, 1, 8);

    private static BlockEntityCoalHeap? HeapAt(IBlockAccessor ba, BlockPos pos) => ba.GetBlockEntity(pos) as BlockEntityCoalHeap;

    private static bool Free(IBlockAccessor ba, BlockPos pos)
    {
        var block = ba.GetBlock(pos);
        return block.Id == 0 || block is BlockPipeWarmth;
    }

    /// <summary>Height of the coal in a cell, in layers from the bottom of the world.</summary>
    private static int Height(IBlockAccessor ba, BlockPos pos) => pos.Y * 8 + (HeapAt(ba, pos) is { } heap ? Layers(heap.Count) : 0);

    /// <summary>
    /// The cell coal would land in on this column near a height: the part-full heap block on top, or the
    /// free cell resting on the pile or the ground. Null where something is in the way.
    /// </summary>
    private static BlockPos? Surface(IBlockAccessor ba, int x, int z, BlockPos near)
    {
        var pos = new BlockPos(x, near.Y, z, near.dimension);
        if (ba.GetChunkAtBlockPos(pos) == null) return null;

        for (int i = 0; i < 48 && HeapAt(ba, pos) is { Count: >= PerBlock }; i++) pos.Up();
        if (HeapAt(ba, pos) != null) return pos;
        if (!Free(ba, pos)) return null;

        for (int i = 0; i < 24 && pos.Y > 1; i++)
        {
            var below = pos.DownCopy();
            if (HeapAt(ba, below) is { } heap) return heap.Count >= PerBlock ? pos : below;
            if (!Free(ba, below)) return pos;
            pos.Down();
        }

        return null;      // a drop too deep to fill
    }

    /// <summary>Puts coal on the pile at a spot. Returns what could not be placed.</summary>
    public static int Add(IWorldAccessor world, BlockPos at, int amount)
    {
        var ba = world.BlockAccessor;
        var block = world.GetBlock(new AssetLocation("froststory", "coalheap-1"));
        if (block == null) return amount;

        while (amount > 0)
        {
            var cell = Surface(ba, at.X, at.Z, at);
            if (cell == null) return amount;

            // roll downhill while a neighbouring column stands well below this one
            for (int step = 0; step < 96; step++)
            {
                BlockPos? lower = null;
                int here = Height(ba, cell), drop = 0;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var beside = Surface(ba, cell.X + dx, cell.Z + dz, cell);
                        if (beside == null) continue;

                        // a corner is further off, so it has to be lower still: that rounds the pile
                        int fall = here - Height(ba, beside) - (dx != 0 && dz != 0 ? DiagonalSlope : Slope);
                        if (fall > drop) { drop = fall; lower = beside; }
                    }
                }

                if (lower == null) break;
                cell = lower;
            }

            var heap = HeapAt(ba, cell);
            if (heap == null)
            {
                ba.SetBlock(block.Id, cell);
                heap = HeapAt(ba, cell);
                if (heap == null) return amount;
            }

            int toLayer = PerLayer - heap.Count % PerLayer;
            int put = Math.Min(amount, Math.Min(toLayer, PerBlock - heap.Count));
            if (put <= 0) return amount;
            heap.Set(heap.Count + put);
            amount -= put;
        }

        return 0;
    }

    /// <summary>Every heap block of the pile this one belongs to.</summary>
    private static List<BlockEntityCoalHeap> Pile(IBlockAccessor ba, BlockPos start)
    {
        var pile = new List<BlockEntityCoalHeap>();
        if (HeapAt(ba, start) is not { } first) return pile;

        var seen = new HashSet<BlockPos> { start.Copy() };
        var todo = new Queue<BlockEntityCoalHeap>();
        todo.Enqueue(first);
        while (todo.Count > 0 && pile.Count < MaxPile)
        {
            var heap = todo.Dequeue();
            pile.Add(heap);
            foreach (var face in BlockFacing.ALLFACES)
            {
                var next = heap.Pos.AddCopy(face);
                if (seen.Add(next) && HeapAt(ba, next) is { } other) todo.Enqueue(other);
            }
        }

        return pile;
    }

    public static long Total(IWorldAccessor world, BlockPos start)
    {
        long sum = 0;
        foreach (var heap in Pile(world.BlockAccessor, start)) sum += heap.Count;
        return sum;
    }

    /// <summary>Draws coal off the top of the pile this block belongs to, a layer at a time. Returns what came.</summary>
    public static int Take(IWorldAccessor world, BlockPos start, int amount)
    {
        if (amount <= 0) return 0;
        var ba = world.BlockAccessor;
        var pile = Pile(ba, start);
        pile.Sort((a, b) => Height(ba, b.Pos).CompareTo(Height(ba, a.Pos)));

        int got = 0;
        foreach (var heap in pile)
        {
            if (got >= amount) break;
            int take = Math.Min(amount - got, heap.Count);
            got += take;
            heap.Set(heap.Count - take);
        }

        return got;
    }

    /// <summary>Takes coal from the top of the column a block is in: what a player's hand reaches.</summary>
    public static int TakeFromColumn(IWorldAccessor world, BlockPos pos, int amount)
    {
        var ba = world.BlockAccessor;
        var top = pos.Copy();
        for (int i = 0; i < 64 && HeapAt(ba, top.UpCopy()) != null; i++) top.Up();
        if (HeapAt(ba, top) is not { } heap) return 0;
        int take = Math.Min(amount, heap.Count);
        heap.Set(heap.Count - take);
        return take;
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

        int left = whole;
        foreach (var spot in Spots())
        {
            left = CoalPile.Add(Api.World, spot, whole);
            if (left < whole) break;
        }

        bool was = blocked;
        blocked = left >= whole;
        raised = blocked ? Math.Min(raised, 1f) : raised - (whole - left);      // with nowhere to put it, it stops; it does not save up

        if (was != blocked) MarkDirty();
    }

    /// <summary>Where the borer tips its coal: east of it first, then the other sides.</summary>
    private IEnumerable<BlockPos> Spots()
    {
        yield return Pos.AddCopy(2, 0, 0);
        yield return Pos.AddCopy(-1, 0, 1);
        yield return Pos.AddCopy(1, 0, 2);
        yield return Pos.AddCopy(0, 0, -1);
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
/// same line. Whoever is at the end asks the line, and it draws from every coal pile the line touches.
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
        var heaps = new List<BlockPos>();
        line.Enqueue(end.Copy());

        while (line.Count > 0 && seen.Count < MaxLine)
        {
            var at = line.Dequeue();
            foreach (var face in BlockFacing.ALLFACES)
            {
                var next = at.AddCopy(face);
                if (!seen.Add(next) || ba.GetChunkAtBlockPos(next) == null) continue;
                if (IsConveyor(ba.GetBlock(next))) line.Enqueue(next);
                else if (!at.Equals(end) && ba.GetBlockEntity(next) is BlockEntityCoalHeap) heaps.Add(next);
            }
        }

        int got = 0;
        foreach (var heap in heaps)
        {
            got += CoalPile.Take(world, heap, amount - got);
            if (got >= amount) break;
        }

        return got;
    }
}
