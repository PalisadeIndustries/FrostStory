using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FrostStory.Steam;

/// <summary>
/// A stop valve's setting: 0 is shut, 8 fully open, and each step between lets an eighth of a pipe's
/// steam through. A pointer over the handwheel shows it: along the pipe when open, across it when shut.
/// </summary>
public class BlockEntitySteamValve : BlockEntity
{
    private static readonly Vec3f BlockCentre = new(0.5f, 0.5f, 0.5f);

    public int Setting { get; private set; } = SteamNetwork.ValveSteps;

    public void Turn(int steps) => Set(Setting + steps);

    public void Set(int setting)
    {
        setting = Math.Clamp(setting, 0, SteamNetwork.ValveSteps);
        if (setting == Setting) return;

        Setting = setting;
        MarkDirty(true);
        Api.ModLoader.GetModSystem<FrostStoryModSystem>().SteamFor(Api.World).Invalidate();
        Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/chute"), Pos, 0, null, true, 12, 0.6f);
    }

    public string Describe()
    {
        if (Setting == 0) return Lang.Get("froststory:valve-shut");
        if (Setting == SteamNetwork.ValveSteps) return Lang.Get("froststory:valve-open");
        return Lang.Get("froststory:valve-part", Setting, SteamNetwork.ValveSteps,
            Math.Round(SteamNetwork.ValveCapacity * Setting / SteamNetwork.ValveSteps, 1));
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        var shape = Vintagestory.API.Common.Shape.TryGet(Api, new AssetLocation("froststory", "shapes/block/generator/valvepointer.json"));
        if (shape == null) return false;

        tessThreadTesselator.TesselateShape(Block, shape, out var mesh);
        if (mesh == null) return false;

        // The pointer is drawn lying along the pipe of a north-south valve. Turn it for the setting,
        // then the way the block itself is turned.
        float shut = 1f - (float)Setting / SteamNetwork.ValveSteps;
        mesh.Rotate(BlockCentre, 0f, shut * GameMath.PIHALF, 0f);
        mesh.Translate(0f, 0.49f, 0f);
        string axis = Block.Variant["axis"];
        if (axis == "we") mesh.Rotate(BlockCentre, 0f, GameMath.PIHALF, 0f);
        else if (axis == "ud") mesh.Rotate(BlockCentre, GameMath.PIHALF, 0f, 0f);
        mesher.AddMeshData(mesh);
        return false;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("setting", Setting);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        int before = Setting;
        Setting = Math.Clamp(tree.GetInt("setting", SteamNetwork.ValveSteps), 0, SteamNetwork.ValveSteps);
        if (Api?.Side == EnumAppSide.Client && before != Setting)
        {
            Api.ModLoader.GetModSystem<FrostStoryModSystem>().SteamFor(Api.World).Invalidate();
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }
    }
}
