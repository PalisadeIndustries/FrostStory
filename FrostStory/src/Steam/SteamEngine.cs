using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace FrostStory.Steam;

/// <summary>
/// A steam engine: a fitting on the steam network that turns a shaft on the game's own mechanical power
/// network. Pipes join it on every side but the shaft's, and pipe runs can be strung to it like any
/// fitting. Its shaft side takes an axle, a gear or anything else that joins mechanical power, from the
/// game or from another mod, and the engine drives that network for as long as it has steam.
///
/// The variant <c>side</c> is the side the shaft comes out of. What it draws comes from the block's
/// <c>steamDraw</c> attribute (20 if unset); its power from the <see cref="BEBehaviorSteamEngine"/>
/// properties. Another mod can make an engine of its own from JSON alone: give a block this class,
/// <c>FrostStorySteamNode</c> as its entity and the <c>FrostStorySteamEngine</c> behaviour.
/// </summary>
public class BlockSteamEngine : BlockSteamConduit, IMechanicalPowerBlock, IMPPowered
{
    public const float DefaultDraw = 20f;

    /// <summary>The side the shaft comes out of.</summary>
    public BlockFacing Shaft { get; private set; } = BlockFacing.NORTH;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        Shaft = BlockFacing.FromCode(Variant["side"]) ?? BlockFacing.NORTH;
        OpenAllBut(Shaft);
        if (Draw <= 0f) Draw = Attributes?["steamDraw"].AsFloat(DefaultDraw) ?? DefaultDraw;
    }

    // ------------------------------------------------------------------ mechanical power

    public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos)
    {
        return (world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>() as IMechanicalPowerDevice)?.Network;
    }

    public bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock) => face == Shaft;

    public void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {
    }

    // ------------------------------------------------------------------ placing

    /// <summary>
    /// Set beside an axle or gear that can take a drive, the shaft turns to meet it. Otherwise the shaft
    /// points the way the player is facing, so the axle goes on beyond the engine.
    /// </summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        BlockFacing? shaft = null;
        foreach (var face in BlockFacing.HORIZONTALS)
        {
            var next = blockSel.Position.AddCopy(face);
            if (world.BlockAccessor.GetBlock(next) is not IMechanicalPowerBlock other || other is IMPPowered) continue;
            if (!other.HasMechPowerConnectorAt(world, next, face.Opposite, null!)) continue;
            shaft = face;
            break;
        }

        shaft ??= byPlayer != null ? SuggestedHVOrientation(byPlayer, blockSel)[0] : Shaft;

        var turned = world.GetBlock(CodeWithVariant("side", shaft.Code)) ?? this;
        if (!turned.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;
        if (!turned.DoPlaceBlock(world, byPlayer, blockSel, itemstack)) return false;

        var beyond = blockSel.Position.AddCopy(shaft);
        if (world.BlockAccessor.GetBlock(beyond) is IMechanicalPowerBlock joined && joined.HasMechPowerConnectorAt(world, beyond, shaft.Opposite, null!))
            joined.DidConnectAt(world, beyond, shaft.Opposite);
        return true;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        return new ItemStack(world.GetBlock(CodeWithVariant("side", "north")) ?? this);
    }
}

/// <summary>
/// The engine's side of the mechanical power network: a rotor, like a windmill's, whose drive comes from
/// the steam reaching it. With all the steam it draws it turns its network towards <c>speed</c> with
/// <c>torque</c>; starved, or throttled by a valve upstream, it gives that share of both.
///
/// Properties (all optional): <c>torque</c> (1.5; a four-sail windmill is 1), <c>speed</c> (0.75; a
/// windmill in a gale reaches 0.6), <c>resistance</c> (0.004), <c>driveShape</c> (the turning part,
/// "{side}" replaced by the shaft side).
/// </summary>
public class BEBehaviorSteamEngine : BEBehaviorMPRotor
{
    private float torque = 1.5f;
    private float topSpeed = 0.75f;
    private float resistance = 0.004f;
    private string driveShape = "froststory:block/generator/steamengine-drive-{side}";

    /// <summary>How well fed with steam the engine is, from 0 to 1. Worked out by the server, sent to clients.</summary>
    public float Fed { get; private set; }

    protected override float Resistance => resistance;
    protected override double AccelerationFactor => 0.05;
    protected override float TargetSpeed => topSpeed * Fed;
    protected override float TorqueFactor => torque * Fed;

    public BEBehaviorSteamEngine(BlockEntity blockentity) : base(blockentity)
    {
        // a windmill drives out of its back; the engine drives out of the side its variant names
        OutFacingForNetworkDiscovery = ownFacing;
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        torque = properties["torque"].AsFloat(torque);
        topSpeed = properties["speed"].AsFloat(topSpeed);
        resistance = properties["resistance"].AsFloat(resistance);
        driveShape = properties["driveShape"].AsString(driveShape);

        base.Initialize(api, properties);

        if (api.Side == EnumAppSide.Server)
        {
            Blockentity.RegisterGameTickListener(_ => CheckSteam(), 1000);
            Blockentity.RegisterDelayedCallback(_ => CheckSteam(), 250);
        }
        else
        {
            Blockentity.RegisterGameTickListener(_ => Exhaust(), 400);
        }
    }

    /// <summary>
    /// As a windmill's rotor, but reversible. A windmill turns one way about its own shaft, so two facing
    /// each other on an axle fight to a standstill. An engine has reversing gear: it runs whichever way its
    /// network is already turning, and from rest every engine starts the same way, so any number of them
    /// on one network, at either end, all pull together.
    /// </summary>
    public override float GetTorque(long tick, float speed, out float resistance)
    {
        capableSpeed += (TargetSpeed - capableSpeed) * AccelerationFactor;

        // speed and the torque returned are in this node's frame; GearedRatio carries them to the network's
        float dir = speed > 0f ? 1f : speed < 0f ? -1f : GearedRatio < 0f ? -1f : 1f;
        float absSpeed = Math.Abs(speed);
        double excess = absSpeed - capableSpeed;

        resistance = excess > 0 ? (float)(Resistance * Math.Min(0.2, excess * excess * 80)) : 0f;
        return (float)Math.Max(0, capableSpeed - absSpeed) * TorqueFactor * dir;
    }

    /// <summary>One shape object per drive model, so all engines of a kind share one instanced renderer.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CompositeShape> DriveShapes = new();

    protected override CompositeShape GetShape()
    {
        var path = AssetLocation.Create(driveShape.Replace("{side}", ownFacing.Code), Block.Code.Domain);
        return DriveShapes.GetOrAdd(path.ToString(), _ => new CompositeShape { Base = path });
    }

    private void CheckSteam()
    {
        float now = Api.ModLoader.GetModSystem<FrostStoryModSystem>().SteamFor(Api.World).Ratio(Position);
        if (Math.Abs(now - Fed) < 0.01f && (now > 0f) == (Fed > 0f)) return;
        Fed = now;
        Blockentity.MarkDirty();
    }

    /// <summary>A puff of exhaust from over the cylinder while it works.</summary>
    private void Exhaust()
    {
        float speed = network?.Speed ?? 0f;
        if (Fed <= 0f || speed < 0.02f) return;

        var back = ownFacing.Opposite.Normalf;
        var at = new Vec3d(Position.X + 0.5 + back.X * 0.17, Position.InternalY + 1.0, Position.Z + 0.5 + back.Z * 0.17);
        var puff = new SimpleParticleProperties(
            1, 1 + 2 * Math.Min(1f, speed),
            ColorUtil.ToRgba(90, 235, 235, 240),
            at.AddCopy(-0.06, 0, -0.06), at.AddCopy(0.06, 0.05, 0.06),
            new Vec3f(-0.08f, 0.5f, -0.08f), new Vec3f(0.08f, 0.9f, 0.08f),
            1.6f, -0.01f, 0.4f, 0.9f,
            EnumParticleModel.Quad)
        {
            SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 2f),
            OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.QUADRATIC, -16f),
            WindAffectednes = 0.6f,
            WithTerrainCollision = false
        };
        Api.World.SpawnParticles(puff);
    }

    /// <summary>The engine body is an ordinary block model; only the drive is drawn by the mechanical power renderer.</summary>
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        base.OnTesselation(mesher, tesselator);
        return false;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
    {
        base.GetBlockInfo(forPlayer, sb);
        if (Fed <= 0f)
        {
            sb.AppendLine(Lang.Get("froststory:engine-nosteam"));
            return;
        }

        sb.AppendLine(Lang.Get("froststory:engine-output", (int)Math.Round(Fed * 100), Math.Round(torque * Fed, 2), Math.Round(topSpeed * Fed, 2)));
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat("steamFed", Fed);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        Fed = tree.GetFloat("steamFed");
    }
}
