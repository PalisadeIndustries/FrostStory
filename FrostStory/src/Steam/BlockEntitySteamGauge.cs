using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FrostStory.Steam;

/// <summary>
/// A gauge station's dial. The server reads the run every couple of seconds; the client draws a needle
/// for the steam that can reach this point, over a ring of marks: green from what everything beyond
/// draws up to a quarter more, red above that, plain below.
/// </summary>
public class BlockEntitySteamGauge : BlockEntity
{
    /// <summary>Full scale: a little over what one base port supplies.</summary>
    public const float FullScale = 60f;
    private const int Marks = 24;
    private const float Sweep = 240f * GameMath.DEG2RAD;

    private static readonly Vec3f BlockCentre = new(0.5f, 0.5f, 0.5f);

    public float Available { get; private set; }
    public float Demand { get; private set; }
    public float Ratio { get; private set; }

    /// <summary>Where the needle stands: the steam that can reach here, or less when the line is starved.</summary>
    public float Needle => Ratio < 0.995f ? Demand * Ratio : Available;

    /// <summary>The top of the green zone.</summary>
    public float GreenTop => Demand * 1.25f + 2f;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server) RegisterGameTickListener(Read, 2000, api.World.Rand.Next(2000));
    }

    private void Read(float dt)
    {
        var (available, demand, ratio) = Api.ModLoader.GetModSystem<FrostStoryModSystem>().SteamFor(Api.World).Reading(Pos);
        if (Math.Abs(available - Available) < 0.05f && Math.Abs(demand - Demand) < 0.05f && Math.Abs(ratio - Ratio) < 0.005f) return;

        Available = available;
        Demand = demand;
        Ratio = ratio;
        MarkDirty(true);
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        float yaw = Block.Variant["side"] switch { "west" => 90f, "south" => 180f, "east" => 270f, _ => 0f } * GameMath.DEG2RAD;

        MeshData? Piece(string name)
        {
            var shape = Vintagestory.API.Common.Shape.TryGet(Api, new AssetLocation("froststory", $"shapes/block/generator/{name}.json"));
            if (shape == null) return null;
            tessThreadTesselator.TesselateShape(Block, shape, out var mesh);
            return mesh;
        }

        // Each piece is drawn at the block centre, pointing up. It is moved out along the dial, swung
        // round the dial's middle, set on the north-facing dial, and turned with the block.
        void Place(MeshData? piece, float value, float radius, float depth)
        {
            if (piece == null) return;
            var mesh = piece.Clone();
            float angle = (Math.Clamp(value / FullScale, 0f, 1f) - 0.5f) * Sweep;
            mesh.Translate(0f, radius, 0f);
            mesh.Rotate(BlockCentre, 0f, 0f, angle);
            mesh.Translate(0f, 0.12f, depth);
            mesh.Rotate(BlockCentre, 0f, yaw, 0f);
            mesher.AddMeshData(mesh);
        }

        var plain = Piece("gaugemark-plain");
        var green = Piece("gaugemark-green");
        var red = Piece("gaugemark-red");
        for (int i = 0; i < Marks; i++)
        {
            float value = (i + 0.5f) / Marks * FullScale;
            Place(value > GreenTop ? red : value >= Demand && Demand > 0f ? green : plain, value, 0.2f, -0.44f);
        }

        Place(Piece("gaugeneedle"), Needle, 0f, -0.455f);
        return false;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat("available", Available);
        tree.SetFloat("demand", Demand);
        tree.SetFloat("ratio", Ratio);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Available = tree.GetFloat("available");
        Demand = tree.GetFloat("demand");
        Ratio = tree.GetFloat("ratio");
        if (Api?.Side == EnumAppSide.Client) Api.World.BlockAccessor.MarkBlockDirty(Pos);
    }
}
