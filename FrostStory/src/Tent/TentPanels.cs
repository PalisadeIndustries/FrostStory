using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FrostStory.Tent;

/// <summary>
/// Stretching tent cloth: picking the edge a click means, putting a panel up between two edges and taking
/// it down, and the unseen cells that make a panel keep rain out and warmth in.
/// </summary>
public static class TentPanels
{
    public const float MaxEdge = 16f;
    public const float MaxSpan = 12f;
    private const int MaxRodRun = 16;

    private static readonly Dictionary<string, (BlockPos Rod, long At)> pending = new();
    private const long PendingMs = 60000;

    private static bool IsRod(Block block) => block.FirstCodePart() == "tentrod";

    /// <summary>
    /// The edge a rod stands for: the centre line of the whole straight pole it is part of, however many
    /// rods long.
    /// </summary>
    public static (Vec3d From, Vec3d To) RodEdge(IBlockAccessor ba, BlockPos pos)
    {
        var block = ba.GetBlock(pos);
        var step = block.Variant["axis"] switch { "ud" => new Vec3i(0, 1, 0), "ns" => new Vec3i(0, 0, 1), _ => new Vec3i(1, 0, 0) };

        int back = 0, forward = 0;
        while (back < MaxRodRun && ba.GetBlock(pos.AddCopy(-step.X * (back + 1), -step.Y * (back + 1), -step.Z * (back + 1))).Id == block.Id) back++;
        while (forward < MaxRodRun && ba.GetBlock(pos.AddCopy(step.X * (forward + 1), step.Y * (forward + 1), step.Z * (forward + 1))).Id == block.Id) forward++;

        var centre = new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5);
        return (centre.AddCopy(-step.X * (back + 0.5), -step.Y * (back + 0.5), -step.Z * (back + 0.5)),
                centre.AddCopy(step.X * (forward + 0.5), step.Y * (forward + 0.5), step.Z * (forward + 0.5)));
    }

    /// <summary>The edge of an ordinary block nearest to where it was clicked.</summary>
    public static (Vec3d From, Vec3d To) BlockEdge(BlockPos pos, Vec3d hit)
    {
        double[] h = { hit.X, hit.Y, hit.Z };
        var order = new[] { 0, 1, 2 };
        Array.Sort(order, (a, b) => Math.Min(h[a], 1 - h[a]).CompareTo(Math.Min(h[b], 1 - h[b])));

        // the two coordinates nearest a side snap to it; the third is the direction the edge runs
        double[] from = new double[3], to = new double[3];
        for (int i = 0; i < 2; i++) from[order[i]] = to[order[i]] = h[order[i]] < 0.5 ? 0 : 1;
        from[order[2]] = 0;
        to[order[2]] = 1;

        return (new Vec3d(pos.X + from[0], pos.Y + from[1], pos.Z + from[2]), new Vec3d(pos.X + to[0], pos.Y + to[1], pos.Z + to[2]));
    }

    /// <summary>
    /// Stretches a panel from a rod to another edge, or takes down the panel already there. Returns a lang
    /// key; cost is the cloth a new panel takes (negative: cloth handed back).
    /// </summary>
    public static string Toggle(IWorldAccessor world, BlockPos rodPos, BlockPos otherPos, (Vec3d From, Vec3d To) farEdge, int available, out int cost, out bool built)
    {
        cost = 0;
        built = false;
        var ba = world.BlockAccessor;
        if (!IsRod(ba.GetBlock(rodPos)) || ba.GetBlockEntity(rodPos) is not BlockEntityTentRod rod) return "froststory:tent-needrod";
        if (rodPos.Equals(otherPos)) return "froststory:tent-same";

        var existing = rod.Panels.Find(p => p.Other.Equals(otherPos));
        if (existing != null)
        {
            cost = -existing.Cost;
            TakeDown(world, rod, existing);
            return "froststory:tent-removed";
        }

        var near = RodEdge(ba, rodPos);

        // pair the ends so the sheet does not twist
        if (near.From.DistanceTo(farEdge.From) + near.To.DistanceTo(farEdge.To) > near.From.DistanceTo(farEdge.To) + near.To.DistanceTo(farEdge.From))
            farEdge = (farEdge.To, farEdge.From);

        Vec3f Local(Vec3d p) => new((float)(p.X - rodPos.X), (float)(p.Y - rodPos.Y), (float)(p.Z - rodPos.Z));
        var panel = new TentPanel { A0 = Local(near.From), A1 = Local(near.To), B0 = Local(farEdge.From), B1 = Local(farEdge.To), Other = otherPos.Copy() };

        if (panel.Span < 0.5f) return "froststory:tent-same";
        if (panel.EdgeLength > MaxEdge || panel.Span > MaxSpan) return "froststory:tent-toofar";

        cost = Math.Max(1, (int)MathF.Ceiling(panel.EdgeLength * panel.Span));
        if (available < cost) return "froststory:tent-needcloth";

        panel.Cost = cost;
        rod.Panels.Add(panel);
        rod.MarkDirty(true);
        if (ba.GetBlockEntity(otherPos) is BlockEntityTentRod farRod)
        {
            farRod.HeldBy.Add(rodPos.Copy());
            farRod.MarkDirty();
        }

        SetFill(world, rod, panel, true);
        built = true;
        return "froststory:tent-built";
    }

    private static void TakeDown(IWorldAccessor world, BlockEntityTentRod rod, TentPanel panel)
    {
        SetFill(world, rod, panel, false);
        rod.Panels.Remove(panel);
        rod.MarkDirty(true);
        foreach (var remaining in rod.Panels) SetFill(world, rod, remaining, true);     // panels that shared cells with it keep theirs

        if (world.BlockAccessor.GetBlockEntity(panel.Other) is BlockEntityTentRod far)
        {
            far.HeldBy.RemoveAll(p => p.Equals(rod.Pos));
            far.MarkDirty();
        }
    }

    /// <summary>A rod is going: its own panels come down, and so do panels other rods had stretched to it.</summary>
    public static void RodRemoved(IWorldAccessor world, BlockEntityTentRod rod)
    {
        var cloth = world.GetBlock(new AssetLocation("froststory", "tentcloth"));

        foreach (var panel in rod.Panels.ToArray())
        {
            Drop(world, cloth, rod.Pos, panel.Cost);
            TakeDown(world, rod, panel);
        }

        foreach (var holderPos in rod.HeldBy.ToArray())
        {
            if (world.BlockAccessor.GetBlockEntity(holderPos) is not BlockEntityTentRod holder) continue;
            foreach (var panel in holder.Panels.FindAll(p => p.Other.Equals(rod.Pos)))
            {
                Drop(world, cloth, rod.Pos, panel.Cost);
                TakeDown(world, holder, panel);
            }
        }
    }

    private static void Drop(IWorldAccessor world, Block? cloth, BlockPos at, int count)
    {
        while (cloth != null && count > 0)
        {
            int n = Math.Min(count, cloth.MaxStackSize);
            world.SpawnItemEntity(new ItemStack(cloth, n), at.ToVec3d().Add(0.5, 0.5, 0.5));
            count -= n;
        }
    }

    /// <summary>
    /// Fills (or clears) the air cells a panel passes through with an unseen block that stops rain and holds
    /// warmth, so cloth walls make a shelter the game treats as a room.
    /// </summary>
    private static void SetFill(IWorldAccessor world, BlockEntityTentRod rod, TentPanel panel, bool place)
    {
        var fill = world.GetBlock(new AssetLocation("froststory", "tentfill"));
        if (fill == null) return;

        var ba = world.BlockAccessor;
        var seen = new HashSet<BlockPos>();
        int along = Math.Max(2, (int)MathF.Ceiling(panel.EdgeLength / 0.4f)), across = Math.Max(2, (int)MathF.Ceiling(panel.Span / 0.4f));
        for (int i = 0; i <= along; i++)
        {
            for (int j = 0; j <= across; j++)
            {
                var p = panel.At(i / (float)along, j / (float)across);
                var cell = new BlockPos(rod.Pos.X + (int)MathF.Floor(p.X), rod.Pos.Y + (int)MathF.Floor(p.Y), rod.Pos.Z + (int)MathF.Floor(p.Z), rod.Pos.dimension);
                if (!seen.Add(cell) || ba.GetChunkAtBlockPos(cell) == null) continue;

                int id = ba.GetBlock(cell).Id;
                if (place && id == 0) ba.SetBlock(fill.Id, cell);
                else if (!place && id == fill.Id) ba.SetBlock(0, cell);
            }
        }
    }

    // ------------------------------------------------------------------ the player's two clicks

    /// <summary>
    /// A player holding tent cloth has clicked a block. The first click, on a rod, marks the edge the cloth
    /// hangs from; the second, on another rod or on any block's edge, stretches it there.
    /// </summary>
    public static void Click(IWorldAccessor world, IServerPlayer player, BlockSelection sel, ItemSlot slot)
    {
        var ba = world.BlockAccessor;
        var clicked = ba.GetBlock(sel.Position);
        long now = world.ElapsedMilliseconds;

        if (!pending.TryGetValue(player.PlayerUID, out var start) || now - start.At > PendingMs)
        {
            if (!IsRod(clicked))
            {
                Tell(player, "froststory:tent-needrod");
                return;
            }

            pending[player.PlayerUID] = (sel.Position.Copy(), now);
            Tell(player, "froststory:tent-started");
            return;
        }

        pending.Remove(player.PlayerUID);
        if (start.Rod.Equals(sel.Position))
        {
            Tell(player, "froststory:tent-cancelled");
            return;
        }

        var far = IsRod(clicked) ? RodEdge(ba, sel.Position) : BlockEdge(sel.Position, sel.HitPosition);
        bool creative = player.WorldData.CurrentGameMode == EnumGameMode.Creative;
        string result = Toggle(world, start.Rod, sel.Position, far, creative ? int.MaxValue : slot.StackSize, out int cost, out bool built);

        if (built && !creative)
        {
            slot.TakeOut(cost);
            slot.MarkDirty();
        }
        else if (cost < 0 && !creative)
        {
            Drop(world, world.GetBlock(new AssetLocation("froststory", "tentcloth")), sel.Position, -cost);
        }

        Tell(player, result, Math.Abs(cost));
        if (built) world.PlaySoundAt(new AssetLocation("game:sounds/block/cloth"), sel.Position, 0, null);
    }

    private static void Tell(IServerPlayer player, string key, params object[] args)
    {
        player.SendMessage(GlobalConstants.CurrentChatGroup, Lang.Get(key, args), EnumChatType.Notification);
    }
}
