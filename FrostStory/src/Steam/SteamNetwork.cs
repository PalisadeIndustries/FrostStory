using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FrostStory.Steam;

/// <summary>
/// Answers one question: is this conduit connected to a live steam port? Nothing is stored in the world
/// and nothing ticks. A conduit that is asked traces its run once, and every conduit on that run
/// remembers the answer for a few seconds.
/// </summary>
public sealed class SteamNetwork
{
    private const int MaxRun = 4096;
    private const long RememberMs = 5000;
    private const int MaxRemembered = 60000;

    private readonly IWorldAccessor world;
    private readonly Dictionary<BlockPos, (bool Live, long Expires)> known = new();

    public SteamNetwork(IWorldAccessor world)
    {
        this.world = world;
    }

    /// <summary>Forget everything; called whenever a conduit is placed, removed or switched.</summary>
    public void Invalidate() => known.Clear();

    /// <summary>The conduit occupying a cell, and the cell it lives in (a large conduit spans several).</summary>
    public static bool Resolve(IBlockAccessor ba, BlockPos pos, out BlockSteamConduit conduit, out BlockPos anchor)
    {
        var block = ba.GetBlock(pos);
        if (block is BlockSteamMainFill fill)
        {
            anchor = fill.OwnerPos(pos);
            block = ba.GetBlock(anchor);
        }
        else
        {
            anchor = pos;
        }

        conduit = (block as BlockSteamConduit)!;
        return conduit != null;
    }

    /// <summary>Two large conduits meet squarely when they share a whole face: same bore, lined up.</summary>
    public static bool MeetSquarely(BlockSteamConduit a, BlockPos aAnchor, BlockSteamConduit b, BlockPos bAnchor, BlockFacing faceOfA)
    {
        if (!a.ConnectsOn(faceOfA) || !b.ConnectsOn(faceOfA.Opposite)) return false;

        int[] aMin = { aAnchor.X, aAnchor.Y, aAnchor.Z }, aSize = { a.Size.X, a.Size.Y, a.Size.Z };
        int[] bMin = { bAnchor.X, bAnchor.Y, bAnchor.Z }, bSize = { b.Size.X, b.Size.Y, b.Size.Z };
        int d = (int)faceOfA.Axis;
        bool positive = faceOfA.Normali.X + faceOfA.Normali.Y + faceOfA.Normali.Z > 0;

        for (int i = 0; i < 3; i++)
        {
            if (i == d)
            {
                if (positive ? aMin[i] + aSize[i] != bMin[i] : bMin[i] + bSize[i] != aMin[i]) return false;
            }
            else if (aMin[i] != bMin[i] || aSize[i] != bSize[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool Joined(BlockSteamConduit a, BlockPos aAnchor, BlockSteamConduit b, BlockPos bAnchor, BlockFacing faceOfA)
    {
        if (a.IsBig && b.IsBig) return MeetSquarely(a, aAnchor, b, bAnchor, faceOfA);
        if (a.IsBig) return a.AcceptsTapOn(faceOfA) && b.ConnectsOn(faceOfA.Opposite);
        if (b.IsBig) return a.ConnectsOn(faceOfA) && b.AcceptsTapOn(faceOfA.Opposite);
        return a.ConnectsOn(faceOfA) && b.ConnectsOn(faceOfA.Opposite);
    }

    public bool IsLive(BlockPos pos)
    {
        if (!Resolve(world.BlockAccessor, pos, out var first, out var anchor)) return false;

        long now = world.ElapsedMilliseconds;
        if (known.TryGetValue(anchor, out var entry) && entry.Expires > now) return entry.Live;
        return Trace(first, anchor, now);
    }

    private bool Trace(BlockSteamConduit first, BlockPos start, long now)
    {
        var ba = world.BlockAccessor;
        var run = new HashSet<BlockPos> { start.Copy() };
        var queue = new Queue<BlockPos>();
        queue.Enqueue(start.Copy());

        bool live = first.IsLiveSource;
        bool incomplete = false;

        while (queue.Count > 0)
        {
            var anchor = queue.Dequeue();
            if (ba.GetBlock(anchor) is not BlockSteamConduit conduit) continue;

            foreach (var cell in conduit.Cells(anchor))
            {
                foreach (var face in BlockFacing.ALLFACES)
                {
                    var next = cell.AddCopy(face);
                    if (ba.GetChunkAtBlockPos(next) == null) { incomplete = true; continue; }
                    if (!Resolve(ba, next, out var neighbour, out var nextAnchor)) continue;
                    if (nextAnchor.Equals(anchor) || run.Contains(nextAnchor)) continue;
                    if (!Joined(conduit, anchor, neighbour, nextAnchor, face)) continue;
                    if (run.Count >= MaxRun) { incomplete = true; continue; }

                    run.Add(nextAnchor);
                    queue.Enqueue(nextAnchor);
                    live |= neighbour.IsLiveSource;
                }
            }
        }

        // A run that leaves the loaded world may still reach a port out there: keep what was known.
        if (!live && incomplete && known.TryGetValue(start, out var before)) live = before.Live;

        if (known.Count > MaxRemembered) known.Clear();
        long expires = now + RememberMs;
        foreach (var pos in run) known[pos] = (live, expires);

        return live;
    }
}
