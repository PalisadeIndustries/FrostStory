using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FrostStory.Steam;

/// <summary>
/// Answers one question: is this pipe connected to a live steam port? Nothing is stored in the world
/// and nothing ticks. A pipe that is asked traces its run once, and every pipe on that run remembers
/// the answer for a few seconds.
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

    /// <summary>Forget everything; called whenever a pipe or port is placed, removed or switched.</summary>
    public void Invalidate() => known.Clear();

    public bool IsLive(BlockPos pos)
    {
        long now = world.ElapsedMilliseconds;
        if (known.TryGetValue(pos, out var entry) && entry.Expires > now) return entry.Live;
        return Trace(pos, now);
    }

    private bool Trace(BlockPos start, long now)
    {
        var ba = world.BlockAccessor;
        if (ba.GetBlock(start) is not BlockSteamConduit first) return false;

        var run = new HashSet<BlockPos> { start.Copy() };
        var queue = new Queue<BlockPos>();
        queue.Enqueue(start.Copy());

        bool live = first.IsLiveSource;
        bool incomplete = false;

        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            if (ba.GetBlock(pos) is not BlockSteamConduit conduit) continue;

            foreach (var face in BlockFacing.ALLFACES)
            {
                if (!conduit.ConnectsOn(face)) continue;

                var next = pos.AddCopy(face);
                if (run.Contains(next)) continue;
                if (ba.GetChunkAtBlockPos(next) == null) { incomplete = true; continue; }
                if (ba.GetBlock(next) is not BlockSteamConduit neighbour || !neighbour.ConnectsOn(face.Opposite)) continue;
                if (run.Count >= MaxRun) { incomplete = true; continue; }

                run.Add(next);
                queue.Enqueue(next);
                live |= neighbour.IsLiveSource;
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
