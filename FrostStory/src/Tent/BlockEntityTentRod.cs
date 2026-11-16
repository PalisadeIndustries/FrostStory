using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FrostStory.Tent;

/// <summary>
/// A sheet of tent cloth stretched between two edges. Corners are relative to the rod that holds the
/// panel: A0 and A1 are the ends of its own edge, B0 and B1 the ends of the far one, A0 opposite B0.
/// </summary>
public sealed class TentPanel
{
    public Vec3f A0 = new(), A1 = new(), B0 = new(), B1 = new();
    public BlockPos Other = new(0);      // the block the far edge is on
    public int Cost;

    public const int Floats = 12;
    public const int Ints = 4;

    /// <summary>A point on the sheet: u runs along the edges, v from the near edge to the far one. It sags a little mid-span.</summary>
    public Vec3f At(float u, float v)
    {
        float ax = A0.X + (A1.X - A0.X) * u, ay = A0.Y + (A1.Y - A0.Y) * u, az = A0.Z + (A1.Z - A0.Z) * u;
        float bx = B0.X + (B1.X - B0.X) * u, by = B0.Y + (B1.Y - B0.Y) * u, bz = B0.Z + (B1.Z - B0.Z) * u;
        float span = MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay) + (bz - az) * (bz - az));
        float sag = 0.05f * span * MathF.Sin(MathF.PI * v);
        return new Vec3f(ax + (bx - ax) * v, ay + (by - ay) * v - sag, az + (bz - az) * v);
    }

    public float EdgeLength => (A0.DistanceTo(A1) + B0.DistanceTo(B1)) / 2;
    public float Span => (A0.DistanceTo(B0) + A1.DistanceTo(B1)) / 2;

    /// <summary>Cells along the edges and across the span the sheet is drawn in.</summary>
    public (int Along, int Across) Grid => (Math.Clamp((int)MathF.Ceiling(EdgeLength), 1, 16), Math.Clamp((int)MathF.Ceiling(Span / 0.8f), 2, 16));
}

/// <summary>
/// A tent rod remembers the cloth panels stretched from it and draws them into the chunk mesh, with the
/// wind set to move everything but the two edges they hang from.
/// </summary>
public class BlockEntityTentRod : BlockEntity
{
    public readonly List<TentPanel> Panels = new();

    /// <summary>Rods whose panels end on this one, so they can be told when it goes.</summary>
    public readonly List<BlockPos> HeldBy = new();

    private Shape? piece;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Client)
            piece = Shape.TryGet(api, new AssetLocation("froststory", "shapes/block/generator/tentcloth-piece.json"));
    }

    public override void OnBlockRemoved()
    {
        if (Api?.Side == EnumAppSide.Server) TentPanels.RodRemoved(Api.World, this);
        base.OnBlockRemoved();
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (Panels.Count == 0 || piece == null) return false;

        tessThreadTesselator.TesselateShape(Block, piece, out MeshData square);
        if (square == null || square.VerticesCount < 8) return false;

        foreach (var panel in Panels)
        {
            var (along, across) = panel.Grid;
            for (int i = 0; i < along; i++)
            {
                for (int j = 0; j < across; j++)
                {
                    float u0 = i / (float)along, u1 = (i + 1) / (float)along;
                    float v0 = j / (float)across, v1 = (j + 1) / (float)across;
                    Vec3f c00 = panel.At(u0, v0), c10 = panel.At(u1, v0), c11 = panel.At(u1, v1), c01 = panel.At(u0, v1);

                    // The square has a north face then a south face, four corners each, in the engine's
                    // cube order. Pull them to this cell's corners; rows on an edge stay still in the wind.
                    var mesh = square.Clone();
                    bool near = j == 0, far = j == across - 1;
                    Corner(mesh, 0, c00, !near); Corner(mesh, 1, c01, !far); Corner(mesh, 2, c11, !far); Corner(mesh, 3, c10, !near);
                    Corner(mesh, 4, c10, !near); Corner(mesh, 5, c11, !far); Corner(mesh, 6, c01, !far); Corner(mesh, 7, c00, !near);
                    mesh.HasAnyWindModeSet = true;
                    mesher.AddMeshData(mesh);
                }
            }
        }

        return false;
    }

    private static void Corner(MeshData mesh, int vertex, Vec3f at, bool moves)
    {
        mesh.xyz[vertex * 3] = at.X;
        mesh.xyz[vertex * 3 + 1] = at.Y;
        mesh.xyz[vertex * 3 + 2] = at.Z;
        VertexFlags.SetWindMode(ref mesh.Flags[vertex], moves ? (int)EnumWindBitMode.WeakWind : (int)EnumWindBitMode.NoWind);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        var floats = new float[Panels.Count * TentPanel.Floats];
        var ints = new int[Panels.Count * TentPanel.Ints];
        for (int i = 0; i < Panels.Count; i++)
        {
            var p = Panels[i];
            int f = i * TentPanel.Floats, k = i * TentPanel.Ints;
            Vec3f[] corners = { p.A0, p.A1, p.B0, p.B1 };
            for (int c = 0; c < 4; c++)
            {
                floats[f + c * 3] = corners[c].X; floats[f + c * 3 + 1] = corners[c].Y; floats[f + c * 3 + 2] = corners[c].Z;
            }

            ints[k] = p.Other.X; ints[k + 1] = p.Other.Y; ints[k + 2] = p.Other.Z; ints[k + 3] = p.Cost;
        }

        var held = new int[HeldBy.Count * 3];
        for (int i = 0; i < HeldBy.Count; i++)
        {
            held[i * 3] = HeldBy[i].X; held[i * 3 + 1] = HeldBy[i].Y; held[i * 3 + 2] = HeldBy[i].Z;
        }

        tree["panelCorners"] = new FloatArrayAttribute(floats);
        tree["panels"] = new IntArrayAttribute(ints);
        tree["heldBy"] = new IntArrayAttribute(held);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        var floats = (tree["panelCorners"] as FloatArrayAttribute)?.value ?? Array.Empty<float>();
        var ints = (tree["panels"] as IntArrayAttribute)?.value ?? Array.Empty<int>();
        var held = (tree["heldBy"] as IntArrayAttribute)?.value ?? Array.Empty<int>();
        int dim = Pos?.dimension ?? 0;

        Panels.Clear();
        int count = Math.Min(floats.Length / TentPanel.Floats, ints.Length / TentPanel.Ints);
        for (int i = 0; i < count; i++)
        {
            int f = i * TentPanel.Floats, k = i * TentPanel.Ints;
            Vec3f V(int c) => new(floats[f + c * 3], floats[f + c * 3 + 1], floats[f + c * 3 + 2]);
            Panels.Add(new TentPanel { A0 = V(0), A1 = V(1), B0 = V(2), B1 = V(3), Other = new BlockPos(ints[k], ints[k + 1], ints[k + 2], dim), Cost = ints[k + 3] });
        }

        HeldBy.Clear();
        for (int i = 0; i + 2 < held.Length; i += 3) HeldBy.Add(new BlockPos(held[i], held[i + 1], held[i + 2], dim));

        if (Api?.Side == EnumAppSide.Client) Api.World.BlockAccessor.MarkBlockDirty(Pos);
    }
}
