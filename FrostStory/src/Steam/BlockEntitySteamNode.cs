using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FrostStory.Steam;

/// <summary>
/// A pipe run strung between two fittings, as one of them remembers it. Both ends hold a copy, each from
/// its own side. Points are relative to the holder's own block, so the tube can be drawn without looking
/// anything up in the world.
/// </summary>
public sealed class SteamRun
{
    public BlockPos Other = new(0);
    public BlockFacing Face = BlockFacing.NORTH;         // the side of this fitting the run leaves by
    public BlockFacing OtherFace = BlockFacing.NORTH;    // the side of the other fitting it arrives at
    public Vec3f Start = new();                          // where it leaves this fitting
    public Vec3f End = new();                            // where it meets the other
    public bool Big;
    public int Cost;                                     // pipes it took, handed back when it is taken down

    public const int FloatsPerRun = 6;
    public const int IntsPerRun = 7;
}

/// <summary>
/// What makes a fitting or port a place pipe runs can be strung from. It remembers its runs, draws the
/// ones it owns as curved tubes in the chunk mesh, and takes them down when the block goes.
/// </summary>
public class BlockEntitySteamNode : BlockEntity
{
    public readonly List<SteamRun> Runs = new();

    private static readonly Vec3f BlockCentre = new(0.5f, 0.5f, 0.5f);

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side != EnumAppSide.Server) return;
        SteamRuns.RegisterWarmth(api.World, this);

        if (Block is BlockSteamConduit { IsHub: true })
        {
            // lay the hub's warm air once the block and its neighbours are in, then keep it following the ground
            RegisterDelayedCallback(_ => SteamHub.Refresh(Api.World, Pos), 500);
            RegisterGameTickListener(_ => SteamHub.Refresh(Api.World, Pos), 60000);
        }
    }

    /// <summary>Each run is drawn, and has its warm air kept, by the end that sorts first.</summary>
    public bool Owns(SteamRun run)
    {
        if (Pos.X != run.Other.X) return Pos.X < run.Other.X;
        if (Pos.Y != run.Other.Y) return Pos.Y < run.Other.Y;
        return Pos.Z < run.Other.Z;
    }

    public SteamRun? RunTo(BlockPos other)
    {
        foreach (var run in Runs)
        {
            if (run.Other.Equals(other)) return run;
        }

        return null;
    }

    public void Changed()
    {
        MarkDirty(true);
        Api?.ModLoader.GetModSystem<FrostStoryModSystem>().SteamFor(Api.World).Invalidate();
    }

    public override void OnBlockRemoved()
    {
        if (Api?.Side == EnumAppSide.Server)
        {
            SteamRuns.TakeDownAll(Api.World, this);
            if (Block is BlockSteamConduit { IsHub: true }) SteamHub.Clear(Api.World, Pos);
        }

        base.OnBlockRemoved();
    }

    // ------------------------------------------------------------------ drawing

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (Runs.Count == 0 || Block is not BlockSteamConduit conduit) return false;

        MeshData? small = null, large = null;
        foreach (var run in Runs)
        {
            if (!Owns(run)) continue;

            var shape = run.Big ? conduit.MainSegment : conduit.PipeSegment;
            if (shape == null) continue;

            ref MeshData? piece = ref run.Big ? ref large : ref small;
            if (piece == null) tessThreadTesselator.TesselateShape(Block, shape, out piece);
            if (piece == null) continue;

            AddTube(mesher, piece, run);
        }

        return false;
    }

    /// <summary>
    /// Lays copies of a short straight piece of pipe end to end along the run's curve. The piece is half
    /// a block long, lies along +Z and is centred on the block centre; each copy is stretched to its
    /// chord, pitched and turned to follow it, and moved into place.
    /// </summary>
    private static void AddTube(ITerrainMeshPool mesher, MeshData piece, SteamRun run)
    {
        var points = SteamRuns.Sample(run.Start, run.Face, run.End, run.OtherFace, run.Big);

        // Straight pieces round a bend open up on the outside of it. Each piece is run on past its ends
        // by what the bend there needs: the pipe's radius times the tangent of half the angle it turns.
        float radius = run.Big ? SteamRuns.MainRadius : SteamRuns.PipeRadius;
        float Reach(int at)
        {
            if (at <= 0 || at + 1 >= points.Count) return 0f;
            var u = (points[at] - points[at - 1]).Normalize();
            var v = (points[at + 1] - points[at]).Normalize();
            float half = MathF.Acos(Math.Clamp(u.Dot(v), -1f, 1f)) / 2f;
            return radius * MathF.Tan(Math.Min(half, 0.6f));
        }

        for (int i = 0; i + 1 < points.Count; i++)
        {
            Vec3f a = points[i], b = points[i + 1];
            float dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            float length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 1e-4f) continue;

            float before = Reach(i) + 0.01f, after = Reach(i + 1) + 0.01f;
            float shift = (after - before) / 2f / length;        // the piece's middle moves towards the longer overrun

            var mesh = piece.Clone();
            mesh.Scale(BlockCentre, 1f, 1f, (length + before + after) / SteamRuns.PieceLength);
            mesh.Rotate(BlockCentre, -MathF.Asin(Math.Clamp(dy / length, -1f, 1f)), 0f, 0f);       // pitch +Z up or down
            mesh.Rotate(BlockCentre, 0f, MathF.Atan2(dx, dz), 0f);                                 // then turn it to its heading
            mesh.Translate((a.X + b.X) / 2 + dx * shift - 0.5f, (a.Y + b.Y) / 2 + dy * shift - 0.5f, (a.Z + b.Z) / 2 + dz * shift - 0.5f);
            mesher.AddMeshData(mesh);
        }

        AddFlange(mesher, piece, run.Start, run.Face);
        AddFlange(mesher, piece, run.End, run.OtherFace);
    }

    /// <summary>A collar where the run meets a fitting: the same piece, squat and wide, facing out of the fitting.</summary>
    private static void AddFlange(ITerrainMeshPool mesher, MeshData piece, Vec3f at, BlockFacing face)
    {
        var n = face.Normalf;
        var mesh = piece.Clone();
        mesh.Scale(BlockCentre, 1.3f, 1.3f, 0.3f);
        mesh.Rotate(BlockCentre, -MathF.Asin(n.Y), 0f, 0f);
        mesh.Rotate(BlockCentre, 0f, MathF.Atan2(n.X, n.Z), 0f);
        mesh.Translate(at.X + n.X * 0.08f - 0.5f, at.Y + n.Y * 0.08f - 0.5f, at.Z + n.Z * 0.08f - 0.5f);
        mesher.AddMeshData(mesh);
    }

    // ------------------------------------------------------------------ persistence

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        var ints = new int[Runs.Count * SteamRun.IntsPerRun];
        var floats = new float[Runs.Count * SteamRun.FloatsPerRun];
        for (int i = 0; i < Runs.Count; i++)
        {
            var run = Runs[i];
            int k = i * SteamRun.IntsPerRun, f = i * SteamRun.FloatsPerRun;
            ints[k] = run.Other.X; ints[k + 1] = run.Other.Y; ints[k + 2] = run.Other.Z;
            ints[k + 3] = run.Face.Index; ints[k + 4] = run.OtherFace.Index;
            ints[k + 5] = run.Big ? 1 : 0; ints[k + 6] = run.Cost;
            floats[f] = run.Start.X; floats[f + 1] = run.Start.Y; floats[f + 2] = run.Start.Z;
            floats[f + 3] = run.End.X; floats[f + 4] = run.End.Y; floats[f + 5] = run.End.Z;
        }

        tree["runs"] = new IntArrayAttribute(ints);
        tree["runPoints"] = new FloatArrayAttribute(floats);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        var ints = (tree["runs"] as IntArrayAttribute)?.value ?? Array.Empty<int>();
        var floats = (tree["runPoints"] as FloatArrayAttribute)?.value ?? Array.Empty<float>();
        Runs.Clear();
        int count = Math.Min(ints.Length / SteamRun.IntsPerRun, floats.Length / SteamRun.FloatsPerRun);
        for (int i = 0; i < count; i++)
        {
            int k = i * SteamRun.IntsPerRun, f = i * SteamRun.FloatsPerRun;
            Runs.Add(new SteamRun
            {
                Other = new BlockPos(ints[k], ints[k + 1], ints[k + 2], Pos?.dimension ?? 0),
                Face = BlockFacing.ALLFACES[Math.Clamp(ints[k + 3], 0, 5)],
                OtherFace = BlockFacing.ALLFACES[Math.Clamp(ints[k + 4], 0, 5)],
                Big = ints[k + 5] != 0,
                Cost = ints[k + 6],
                Start = new Vec3f(floats[f], floats[f + 1], floats[f + 2]),
                End = new Vec3f(floats[f + 3], floats[f + 4], floats[f + 5])
            });
        }

        // the tube is part of the chunk mesh, so a change of runs means a redraw
        if (Api?.Side == EnumAppSide.Client) Api.World.BlockAccessor.MarkBlockDirty(Pos);
    }
}
