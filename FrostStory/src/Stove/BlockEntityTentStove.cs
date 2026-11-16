using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FrostStory.Stove;

/// <summary>Keeps a tent stove's fuel, burns it down, and puts the fire out when it is gone or the flue is blocked.</summary>
public class BlockEntityTentStove : BlockEntity
{
    public const float MaxFuelSeconds = 1800f;
    private const int MaxFlue = 32;

    public float FuelSeconds { get; private set; }

    private FrostStoryModSystem Mod => Api.ModLoader.GetModSystem<FrostStoryModSystem>();
    private bool IsLit => Api.World.BlockAccessor.GetBlock(Pos) is BlockTentStove { IsLit: true };

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server) RegisterGameTickListener(Burn, 1000);
        else RegisterGameTickListener(Smoke, 400);
    }

    /// <summary>The top of the flue standing on the stove, or null when there is none.</summary>
    public BlockPos? FlueTop()
    {
        var ba = Api.World.BlockAccessor;
        var pos = Pos.UpCopy();
        if (ba.GetBlock(pos) is not BlockStoveFlue) return null;
        for (int i = 0; i < MaxFlue && ba.GetBlock(pos.UpCopy()) is BlockStoveFlue; i++) pos.Up();
        return pos;
    }

    /// <summary>Null when the stove can draw; otherwise the lang key of what is wrong.</summary>
    public string? FlueProblem()
    {
        var top = FlueTop();
        if (top == null) return "stove-noflue";
        return Api.World.BlockAccessor.GetRainMapHeightAt(top) > top.Y ? "stove-flueblocked" : null;
    }

    /// <summary>Puts one piece of fuel from the slot on the fire. Returns a lang key when it will not go in.</summary>
    public string? AddFuel(ItemSlot slot, bool consume)
    {
        var props = slot.Itemstack?.Collectible.CombustibleProps;
        if (props == null) return "stove-notfuel";

        float seconds = props.BurnDuration * Mod.Config.StoveFuelEfficiency;
        if (FuelSeconds > 0 && FuelSeconds + seconds > MaxFuelSeconds) return "stove-full";

        FuelSeconds += seconds;
        if (consume)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        MarkDirty();
        return null;
    }

    public void AddFuelSeconds(float seconds)
    {
        FuelSeconds = Math.Max(0, FuelSeconds + seconds);
        MarkDirty();
    }

    public string? TryLight()
    {
        if (FuelSeconds <= 0) return "stove-nofuel";
        string? problem = FlueProblem();
        if (problem != null) return problem;
        SetLit(true);
        return null;
    }

    private void SetLit(bool lit)
    {
        var ba = Api.World.BlockAccessor;
        var block = ba.GetBlock(Pos);
        var wanted = Api.World.GetBlock(block.CodeWithVariant("state", lit ? "lit" : "cold"));
        if (wanted != null && wanted.Id != block.Id) ba.ExchangeBlock(wanted.Id, Pos);
        MarkDirty(true);
    }

    private void Burn(float dt)
    {
        if (!IsLit) return;

        FuelSeconds -= dt;
        if (FuelSeconds <= 0 || FlueProblem() != null)
        {
            FuelSeconds = Math.Max(0, FuelSeconds);
            SetLit(false);
        }
        else if ((int)FuelSeconds % 10 == 0)
        {
            MarkDirty();
        }
    }

    private void Smoke(float dt)
    {
        if (!IsLit) return;
        var top = FlueTop();
        if (top == null) return;

        var min = new Vec3d(top.X + 0.4, top.Y + 1.0, top.Z + 0.4);
        var smoke = new SimpleParticleProperties(
            1, 2,
            ColorUtil.ToRgba(120, 70, 70, 70),
            min, min.AddCopy(0.2, 0.1, 0.2),
            new Vec3f(-0.08f, 0.5f, -0.08f), new Vec3f(0.08f, 0.9f, 0.08f),
            4f, -0.01f, 0.4f, 0.8f,
            EnumParticleModel.Quad)
        {
            SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 2f),
            OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.QUADRATIC, -16f),
            WindAffectednes = 0.8f,
            WithTerrainCollision = false
        };

        Api.World.SpawnParticles(smoke);
    }

    public string Summary()
    {
        var sb = new StringBuilder();
        Describe(sb);
        return sb.ToString().Replace('\n', ' ').Trim();
    }

    private void Describe(StringBuilder sb)
    {
        sb.AppendLine(Lang.Get(IsLit ? "froststory:stove-burning" : "froststory:stove-out"));
        if (FuelSeconds > 0) sb.AppendLine(Lang.Get("froststory:stove-fuel", (int)Math.Ceiling(FuelSeconds / 60f)));
        else sb.AppendLine(Lang.Get("froststory:stove-nofuel"));

        string? problem = FlueProblem();
        if (problem != null) sb.AppendLine(Lang.Get("froststory:" + problem));
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        Describe(dsc);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat("fuelSeconds", FuelSeconds);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        FuelSeconds = tree.GetFloat("fuelSeconds");
    }
}
