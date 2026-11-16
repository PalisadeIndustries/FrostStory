using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FrostStory.Steam;

/// <summary>
/// Answers how well a conduit is fed with steam. Nothing is stored in the world and nothing ticks. A
/// conduit that is asked traces its whole run once, the run is worked out, and every conduit on it
/// remembers its answer for a few seconds.
///
/// Live ports supply steam and everything on the run draws some. Stop valves and gauges cut the run into
/// stretches. Starting from the ports, each stretch asks for what it and everything beyond it draws; a
/// valve passes no more than its setting allows. Where less arrives than is asked for, everything beyond
/// is fed in proportion. This treats the run as branching out from its ports; a ring is followed one way
/// round.
/// </summary>
public sealed class SteamNetwork
{
    private const int MaxRun = 4096;
    private const long RememberMs = 5000;
    private const int MaxRemembered = 60000;

    private readonly IWorldAccessor world;
    private readonly Dictionary<BlockPos, Entry> known = new();

    /// <summary>What is remembered about one conduit: how well fed it is (0 to 1), how much steam can
    /// reach it, and how much everything beyond it draws.</summary>
    private readonly record struct Entry(float Ratio, float Available, float Demand, long Expires);

    /// <summary>What a fully open stop valve passes; each of its eight steps is an eighth of this.</summary>
    public const float ValveCapacity = 40f;
    public const int ValveSteps = 8;

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

    public bool IsLive(BlockPos pos) => Ratio(pos) > 0f;

    /// <summary>How well the conduit here is fed: 0 is cold, 1 is all the steam it draws.</summary>
    public float Ratio(BlockPos pos) => Look(pos)?.Ratio ?? 0f;

    /// <summary>
    /// A gauge's reading: steam that can reach this point, what everything beyond it draws, and how well
    /// that draw is being met.
    /// </summary>
    public (float Available, float Demand, float Ratio) Reading(BlockPos pos)
    {
        var entry = Look(pos);
        return entry == null ? (0f, 0f, 0f) : (entry.Value.Available, entry.Value.Demand, entry.Value.Ratio);
    }

    private Entry? Look(BlockPos pos)
    {
        if (!Resolve(world.BlockAccessor, pos, out var first, out var anchor)) return null;

        long now = world.ElapsedMilliseconds;
        if (known.TryGetValue(anchor, out var entry) && entry.Expires > now) return entry;
        Trace(first, anchor, now);
        return known.TryGetValue(anchor, out entry) ? entry : null;
    }

    private void Trace(BlockSteamConduit first, BlockPos start, long now)
    {
        var ba = world.BlockAccessor;
        var mod = world.Api.ModLoader.GetModSystem<FrostStoryModSystem>();

        // ---- gather the run: every conduit joined to this one, and what joins what
        var index = new Dictionary<BlockPos, int> { [start.Copy()] = 0 };
        var nodes = new List<BlockPos> { start.Copy() };
        var links = new List<List<int>> { new() };
        var draw = new List<float>();
        var supply = new List<float>();
        var limit = new List<float>();
        var splits = new List<bool>();
        bool incomplete = false;

        int Add(BlockPos anchor)
        {
            if (index.TryGetValue(anchor, out int have)) return have;
            if (nodes.Count >= MaxRun) { incomplete = true; return -1; }
            index[anchor.Copy()] = nodes.Count;
            nodes.Add(anchor.Copy());
            links.Add(new List<int>());
            return nodes.Count - 1;
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            var anchor = nodes[i];
            var conduit = ba.GetBlock(anchor) as BlockSteamConduit;
            float drawn = conduit?.Draw ?? 0f;
            float passes = float.PositiveInfinity;
            if (conduit is { IsValve: true })
                passes = ValveCapacity * ((ba.GetBlockEntity(anchor) as BlockEntitySteamValve)?.Setting ?? ValveSteps) / ValveSteps;

            supply.Add(conduit is { IsLiveSource: true } ? (conduit.IsBig ? mod.Config.MainPortSupply : mod.Config.PortSupply) : 0f);
            limit.Add(passes);
            splits.Add(conduit != null && (conduit.IsValve || conduit.IsGauge));

            if (conduit == null) { draw.Add(0f); continue; }

            // pipe runs strung from this fitting
            if (ba.GetBlockEntity(anchor) is BlockEntitySteamNode node)
            {
                foreach (var pipeRun in node.Runs)
                {
                    if (node.Owns(pipeRun)) drawn += pipeRun.Cost * (pipeRun.Big ? BlockSteamConduit.MainDraw : BlockSteamConduit.PipeDraw);
                    if (ba.GetChunkAtBlockPos(pipeRun.Other) == null) { incomplete = true; continue; }
                    if (ba.GetBlock(pipeRun.Other) is not BlockSteamConduit { IsNode: true }) continue;

                    int far = Add(pipeRun.Other);
                    if (far >= 0 && !links[i].Contains(far)) { links[i].Add(far); links[far].Add(i); }
                }
            }

            draw.Add(drawn);

            foreach (var cell in conduit.Cells(anchor))
            {
                foreach (var face in BlockFacing.ALLFACES)
                {
                    var next = cell.AddCopy(face);
                    if (ba.GetChunkAtBlockPos(next) == null) { incomplete = true; continue; }
                    if (!Resolve(ba, next, out var neighbour, out var nextAnchor)) continue;
                    if (nextAnchor.Equals(anchor) || !Joined(conduit, anchor, neighbour, nextAnchor, face)) continue;

                    int other = Add(nextAnchor);
                    if (other >= 0 && !links[i].Contains(other)) { links[i].Add(other); links[other].Add(i); }
                }
            }
        }

        // ---- cut it into stretches: each valve and gauge is one of its own, the pipework between them the rest
        int count = nodes.Count;
        var stretchOf = new int[count];
        Array.Fill(stretchOf, -1);
        int stretches = 0;
        var fill = new Queue<int>();
        for (int i = 0; i < count; i++)
        {
            if (stretchOf[i] >= 0) continue;
            stretchOf[i] = stretches;
            if (!splits[i])
            {
                fill.Enqueue(i);
                while (fill.Count > 0)
                {
                    foreach (int j in links[fill.Dequeue()])
                    {
                        if (stretchOf[j] >= 0 || splits[j]) continue;
                        stretchOf[j] = stretches;
                        fill.Enqueue(j);
                    }
                }
            }

            stretches++;
        }

        var drawOf = new float[stretches];
        var supplyOf = new float[stretches];
        var limitOf = new float[stretches];
        var beside = new HashSet<int>[stretches];
        Array.Fill(limitOf, float.PositiveInfinity);
        for (int z = 0; z < stretches; z++) beside[z] = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int z = stretchOf[i];
            drawOf[z] += draw[i];
            supplyOf[z] += supply[i];
            if (splits[i]) limitOf[z] = limit[i];
            foreach (int j in links[i])
            {
                if (stretchOf[j] != z) beside[z].Add(stretchOf[j]);
            }
        }

        // ---- ask outward from the ports for what each stretch and everything beyond it draws
        var asked = new float[stretches];        // before its own valve cuts it down
        var beyond = new float[stretches];       // what the stretches further out ask of it
        var children = new List<int>[stretches];
        var seen = new bool[stretches];
        var roots = new List<int>();
        float total = 0f;
        for (int z = 0; z < stretches; z++)
        {
            children[z] = new List<int>();
            if (supplyOf[z] <= 0f) continue;
            roots.Add(z);
            seen[z] = true;
            total += supplyOf[z];
        }

        float Ask(int z)
        {
            foreach (int w in beside[z])
            {
                if (seen[w]) continue;
                seen[w] = true;
                children[z].Add(w);
                beyond[z] += Math.Min(limitOf[w], Ask(w));
            }

            return asked[z] = drawOf[z] + beyond[z];
        }

        float wanted = 0f;
        foreach (int root in roots) wanted += Ask(root);

        // ---- and hand it down: a stretch is fed as well as the one before it, less whatever its valve holds back
        var ratio = new float[stretches];
        var available = new float[stretches];

        void Feed(int z)
        {
            foreach (int w in children[z])
            {
                ratio[w] = limitOf[w] <= 0f ? 0f : ratio[z] * (asked[w] <= limitOf[w] ? 1f : limitOf[w] / asked[w]);
                available[w] = Math.Min(limitOf[w], available[z]);
                Feed(w);
            }
        }

        foreach (int root in roots)
        {
            ratio[root] = wanted <= total ? 1f : total / wanted;
            available[root] = total;
            Feed(root);
        }

        // A run that leaves the loaded world may still reach a port out there: keep what was known.
        if (roots.Count == 0 && incomplete && known.TryGetValue(start, out var before) && before.Ratio > 0f)
        {
            for (int z = 0; z < stretches; z++) ratio[z] = before.Ratio;
        }

        if (known.Count > MaxRemembered) known.Clear();
        long expires = now + RememberMs;
        for (int i = 0; i < count; i++)
        {
            int z = stretchOf[i];
            known[nodes[i]] = new Entry(ratio[z], available[z], beyond[z], expires);
        }
    }
}
