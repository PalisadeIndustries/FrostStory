using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FrostStory.Steam;

/// <summary>
/// A gauge station's three dials. The server reads the run every couple of seconds; the client draws
/// each dial's marks and needle. Left: the steam that can reach this point, over marks that are green
/// from what everything beyond draws up to a quarter more and red above that. Middle: what everything
/// beyond draws. Right: how well that draw is being met.
/// </summary>
public class BlockEntitySteamGauge : BlockEntity
{
    /// <summary>Full scale: a little over what one base port supplies.</summary>
    public const float FullScale = 60f;
    private const int Marks = 12;

    // the three dials, as offsets from the block centre on a station whose panel faces north
    public const float SupplyX = 0.29f, DrawX = 0f, FedX = -0.29f;
    public const float DialY = 0.2f, MarkRadius = 0.088f, MarkDepth = -0.315f, NeedleDepth = -0.325f;
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

        // Each piece is drawn at the block centre, pointing up. It is moved out along its dial, swung
        // round the dial's middle, set on that dial (they face north), and turned with the block.
        void Place(MeshData? piece, float fraction, float radius, float dialX, float depth)
        {
            if (piece == null) return;
            var mesh = piece.Clone();
            mesh.Translate(0f, radius, 0f);
            mesh.Rotate(BlockCentre, 0f, 0f, (Math.Clamp(fraction, 0f, 1f) - 0.5f) * Sweep);
            mesh.Translate(dialX, DialY, depth);
            mesh.Rotate(BlockCentre, 0f, yaw, 0f);
            mesher.AddMeshData(mesh);
        }

        var plain = Piece("gaugemark-plain");
        var green = Piece("gaugemark-green");
        var red = Piece("gaugemark-red");
        var needle = Piece("gaugeneedle");

        for (int i = 0; i < Marks; i++)
        {
            float at = (i + 0.5f) / Marks;
            float value = at * FullScale;

            // supply: green from what is drawn beyond up to a quarter more, red above
            Place(value > GreenTop ? red : value >= Demand && Demand > 0f ? green : plain, at, MarkRadius, SupplyX, MarkDepth);
            // draw: a plain scale
            Place(plain, at, MarkRadius, DrawX, MarkDepth);
            // fed: red under half, green at the top
            Place(at < 0.5f ? red : at > 0.9f ? green : plain, at, MarkRadius, FedX, MarkDepth);
        }

        Place(needle, Needle / FullScale, 0f, SupplyX, NeedleDepth);
        Place(needle, Demand / FullScale, 0f, DrawX, NeedleDepth);
        Place(needle, Available > 0f ? Ratio : 0f, 0f, FedX, NeedleDepth);
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
