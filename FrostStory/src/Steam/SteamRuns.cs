using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FrostStory.Steam;

/// <summary>
/// Stringing pipe runs between fittings: the geometry of a run, putting one up and taking it down, and the
/// two-click way a player does it. A run leaves each fitting square to one of its sides and curves between.
/// </summary>
public static class SteamRuns
{
    /// <summary>Length of the straight piece a run is drawn from (shapes pipeseg and mainseg).</summary>
    public const float PieceLength = 0.5f;

    public const float MaxLengthSmall = 24f;
    public const float MaxLengthBig = 32f;

    private static readonly Dictionary<string, (BlockPos Pos, long At)> pending = new();
    private const long PendingMs = 60000;

    // ------------------------------------------------------------------ geometry

    /// <summary>The middle of one side of a fitting, in world coordinates.</summary>
    public static Vec3d AttachPoint(BlockSteamConduit conduit, BlockPos anchor, BlockFacing face)
    {
        var size = conduit.Size;
        var n = face.Normali;
        return new Vec3d(
            anchor.X + size.X / 2.0 + n.X * size.X / 2.0,
            anchor.Y + size.Y / 2.0 + n.Y * size.Y / 2.0,
            anchor.Z + size.Z / 2.0 + n.Z * size.Z / 2.0);
    }

    private static float Handle(Vec3f a, Vec3f b, bool big)
    {
        float distance = a.DistanceTo(b);
        return Math.Clamp(distance * 0.45f, big ? 1.5f : 0.75f, 8f);
    }

    /// <summary>A point on the run: a cubic curve that leaves each end along that side's normal.</summary>
    public static Vec3f PointAt(Vec3f start, BlockFacing face, Vec3f end, BlockFacing otherFace, bool big, float t)
    {
        float h = Handle(start, end, big);
        Vec3f n0 = face.Normalf, n1 = otherFace.Normalf;
        float u = 1 - t;
        float b0 = u * u * u, b1 = 3 * u * u * t, b2 = 3 * u * t * t, b3 = t * t * t;
        return new Vec3f(
            b0 * start.X + b1 * (start.X + n0.X * h) + b2 * (end.X + n1.X * h) + b3 * end.X,
            b0 * start.Y + b1 * (start.Y + n0.Y * h) + b2 * (end.Y + n1.Y * h) + b3 * end.Y,
            b0 * start.Z + b1 * (start.Z + n0.Z * h) + b2 * (end.Z + n1.Z * h) + b3 * end.Z);
    }

    public static float Length(Vec3f start, BlockFacing face, Vec3f end, BlockFacing otherFace, bool big)
    {
        float length = 0;
        var previous = start;
        for (int i = 1; i <= 24; i++)
        {
            var p = PointAt(start, face, end, otherFace, big, i / 24f);
            length += previous.DistanceTo(p);
            previous = p;
        }

        return length;
    }

    /// <summary>Points along the run, about one piece apart.</summary>
    public static List<Vec3f> Sample(Vec3f start, BlockFacing face, Vec3f end, BlockFacing otherFace, bool big)
    {
        int pieces = Math.Clamp((int)MathF.Ceiling(Length(start, face, end, otherFace, big) / (PieceLength * 0.9f)), 2, 160);
        var points = new List<Vec3f>(pieces + 1);
        for (int i = 0; i <= pieces; i++) points.Add(PointAt(start, face, end, otherFace, big, i / (float)pieces));
        return points;
    }

    // ------------------------------------------------------------------ putting up and taking down

    private static BlockEntitySteamNode? NodeAt(IWorldAccessor world, BlockPos pos, out BlockSteamConduit conduit, out BlockPos anchor)
    {
        if (!SteamNetwork.Resolve(world.BlockAccessor, pos, out conduit, out anchor) || !conduit.IsNode) return null;
        return world.BlockAccessor.GetBlockEntity(anchor) as BlockEntitySteamNode;
    }

    /// <summary>The free side of a fitting that faces a point best; ports have only their outlet.</summary>
    private static BlockFacing? BestFace(BlockSteamConduit conduit, BlockPos anchor, BlockEntitySteamNode node, Vec3d towards)
    {
        BlockFacing? best = null;
        double bestDot = -0.25;      // never leave by a side that faces away
        foreach (var face in BlockFacing.ALLFACES)
        {
            if (!conduit.ConnectsOn(face)) continue;

            bool taken = false;
            foreach (var run in node.Runs) taken |= run.Face == face;
            if (taken) continue;

            var from = AttachPoint(conduit, anchor, face);
            var dir = towards.SubCopy(from).Normalize();
            double dot = dir.X * face.Normali.X + dir.Y * face.Normali.Y + dir.Z * face.Normali.Z;
            if (dot > bestDot)
            {
                bestDot = dot;
                best = face;
            }
        }

        return best;
    }

    /// <summary>
    /// Strings a run between two fittings, or takes the existing one down. Returns a lang key (and
    /// arguments) describing what happened; <paramref name="cost"/> is how many pipes a new run takes.
    /// With <paramref name="available"/> below the cost nothing is built.
    /// </summary>
    public static string Toggle(IWorldAccessor world, BlockPos first, BlockPos second, bool big, int available, out int cost, out bool built)
    {
        cost = 0;
        built = false;

        var a = NodeAt(world, first, out var aConduit, out var aPos);
        var b = NodeAt(world, second, out var bConduit, out var bPos);
        if (a == null || b == null) return "froststory:run-notfitting";
        if (aPos.Equals(bPos)) return "froststory:run-same";

        var existing = a.RunTo(bPos);
        if (existing != null)
        {
            cost = -existing.Cost;
            TakeDown(world, a, existing);
            return "froststory:run-removed";
        }

        if (big && (!aConduit.IsBig || !bConduit.IsBig)) return "froststory:run-needsbig";

        var aMid = aPos.ToVec3d().Add(aConduit.Size.X / 2.0, aConduit.Size.Y / 2.0, aConduit.Size.Z / 2.0);
        var bMid = bPos.ToVec3d().Add(bConduit.Size.X / 2.0, bConduit.Size.Y / 2.0, bConduit.Size.Z / 2.0);
        var aFace = BestFace(aConduit, aPos, a, bMid);
        var bFace = BestFace(bConduit, bPos, b, aMid);
        if (aFace == null || bFace == null) return "froststory:run-noside";

        var aPoint = AttachPoint(aConduit, aPos, aFace);
        var bPoint = AttachPoint(bConduit, bPos, bFace);

        // each end keeps the run in its own block's coordinates
        Vec3f Local(Vec3d p, BlockPos origin) => new((float)(p.X - origin.X), (float)(p.Y - origin.Y), (float)(p.Z - origin.Z));
        var forA = new SteamRun { Other = bPos.Copy(), Face = aFace, OtherFace = bFace, Start = Local(aPoint, aPos), End = Local(bPoint, aPos), Big = big };
        var forB = new SteamRun { Other = aPos.Copy(), Face = bFace, OtherFace = aFace, Start = Local(bPoint, bPos), End = Local(aPoint, bPos), Big = big };

        float length = Length(forA.Start, aFace, forA.End, bFace, big);
        if (length > (big ? MaxLengthBig : MaxLengthSmall)) return "froststory:run-toolong";

        cost = Math.Max(1, (int)MathF.Ceiling(length));
        if (available < cost) return "froststory:run-needpipes";

        forA.Cost = forB.Cost = cost;
        a.Runs.Add(forA);
        b.Runs.Add(forB);
        RegisterWarmth(world, a);
        RegisterWarmth(world, b);
        a.Changed();
        b.Changed();
        built = true;
        return "froststory:run-built";
    }

    private static void TakeDown(IWorldAccessor world, BlockEntitySteamNode node, SteamRun run)
    {
        ClearWarmth(world, node, run);
        node.Runs.Remove(run);
        node.Changed();

        if (world.BlockAccessor.GetBlockEntity(run.Other) is BlockEntitySteamNode other)
        {
            var back = other.RunTo(node.Pos);
            if (back != null)
            {
                ClearWarmth(world, other, back);
                other.Runs.Remove(back);
                other.Changed();
            }
        }
    }

    /// <summary>A fitting is going: take down every run on it and drop the pipes they were made of.</summary>
    public static void TakeDownAll(IWorldAccessor world, BlockEntitySteamNode node)
    {
        foreach (var run in node.Runs.ToArray())
        {
            DropPipes(world, node.Pos, run.Big, run.Cost);
            TakeDown(world, node, run);
        }
    }

    public static void DropPipes(IWorldAccessor world, BlockPos at, bool big, int count)
    {
        var block = world.GetBlock(new AssetLocation("froststory", big ? "steammain-we" : "steampipe-ud"));
        while (block != null && count > 0)
        {
            int n = Math.Min(count, block.MaxStackSize);
            world.SpawnItemEntity(new ItemStack(block, n), at.ToVec3d().Add(0.5, 0.5, 0.5));
            count -= n;
        }
    }

    // ------------------------------------------------------------------ warm air along a run

    /// <summary>
    /// The air cells a run passes through, a couple of blocks apart. Each gets an unseen block that is warm
    /// while the run carries steam, so a run heats the space it crosses the way a line of pipe blocks would.
    /// </summary>
    private static IEnumerable<BlockPos> WarmthCells(BlockEntitySteamNode node, SteamRun run)
    {
        var seen = new HashSet<BlockPos>();
        var points = Sample(run.Start, run.Face, run.End, run.OtherFace, run.Big);
        for (int i = 2; i < points.Count - 2; i += 3)
        {
            var p = points[i];
            var cell = new BlockPos(
                node.Pos.X + (int)MathF.Floor(p.X), node.Pos.Y + (int)MathF.Floor(p.Y), node.Pos.Z + (int)MathF.Floor(p.Z), node.Pos.dimension);
            if (seen.Add(cell)) yield return cell;
        }
    }

    /// <summary>Places (or re-registers after a load) the warm cells of every run this fitting owns.</summary>
    public static void RegisterWarmth(IWorldAccessor world, BlockEntitySteamNode node)
    {
        var warmth = world.GetBlock(new AssetLocation("froststory", "pipewarmth"));
        var mod = world.Api.ModLoader.GetModSystem<FrostStoryModSystem>();
        if (warmth == null) return;

        foreach (var run in node.Runs)
        {
            if (!node.Owns(run)) continue;
            foreach (var cell in WarmthCells(node, run))
            {
                if (world.BlockAccessor.GetChunkAtBlockPos(cell) == null) continue;
                int id = world.BlockAccessor.GetBlock(cell).Id;
                if (id == 0) world.BlockAccessor.SetBlock(warmth.Id, cell);
                else if (id != warmth.Id) continue;
                mod.WarmthOwners[cell] = (node.Pos.Copy(), run.Big);
            }
        }
    }

    private static void ClearWarmth(IWorldAccessor world, BlockEntitySteamNode node, SteamRun run)
    {
        if (!node.Owns(run)) return;

        var warmth = world.GetBlock(new AssetLocation("froststory", "pipewarmth"));
        var mod = world.Api.ModLoader.GetModSystem<FrostStoryModSystem>();
        foreach (var cell in WarmthCells(node, run))
        {
            mod.WarmthOwners.Remove(cell);
            if (warmth != null && world.BlockAccessor.GetChunkAtBlockPos(cell) != null && world.BlockAccessor.GetBlock(cell).Id == warmth.Id)
                world.BlockAccessor.SetBlock(0, cell);
        }
    }

    // ------------------------------------------------------------------ the player's two clicks

    /// <summary>
    /// A player holding pipes has clicked a fitting. The first click marks it; the second, on another
    /// fitting, strings a run between the two (or takes down the run that is already there).
    /// </summary>
    public static void Click(IWorldAccessor world, IServerPlayer player, BlockPos clicked, bool big)
    {
        if (!SteamNetwork.Resolve(world.BlockAccessor, clicked, out var conduit, out var anchor) || !conduit.IsNode) return;

        long now = world.ElapsedMilliseconds;
        if (!pending.TryGetValue(player.PlayerUID, out var start) || now - start.At > PendingMs || start.Pos.Equals(anchor))
        {
            bool cancel = pending.TryGetValue(player.PlayerUID, out start) && start.Pos.Equals(anchor) && now - start.At <= PendingMs;
            if (cancel) pending.Remove(player.PlayerUID);
            else pending[player.PlayerUID] = (anchor.Copy(), now);
            Tell(player, cancel ? "froststory:run-cancelled" : "froststory:run-started");
            return;
        }

        pending.Remove(player.PlayerUID);

        var slot = player.InventoryManager.ActiveHotbarSlot;
        bool creative = player.WorldData.CurrentGameMode == EnumGameMode.Creative;
        int available = creative ? int.MaxValue : slot?.StackSize ?? 0;

        string result = Toggle(world, start.Pos, anchor, big, available, out int cost, out bool built);
        if (built && !creative && slot != null)
        {
            slot.TakeOut(cost);
            slot.MarkDirty();
        }
        else if (cost < 0 && !creative)
        {
            DropPipes(world, anchor, big, -cost);
        }

        Tell(player, result, Math.Abs(cost));
        if (built) world.PlaySoundAt(new AssetLocation("game:sounds/block/chute"), anchor, 0, null);
    }

    private static void Tell(IServerPlayer player, string key, params object[] args)
    {
        player.SendMessage(GlobalConstants.CurrentChatGroup, Lang.Get(key, args), EnumChatType.Notification);
    }
}
