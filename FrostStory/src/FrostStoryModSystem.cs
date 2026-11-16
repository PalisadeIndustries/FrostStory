using System;
using System.Collections.Generic;
using System.Linq;
using FrostStory.Generator;
using FrostStory.Coal;
using FrostStory.Steam;
using FrostStory.Stove;
using FrostStory.Tent;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace FrostStory;

public class FrostStoryModSystem : ModSystem
{
    public const string Domain = "froststory";
    private const string ConfigFile = "froststory.json";

    public FrostStoryConfig Config { get; private set; } = new();
    public GeneratorStagesConfig Stages { get; private set; } = new();

    private readonly Dictionary<string, ItemStack[]> stackCache = new();
    /// <summary>Server side: which fitting each cell of warm air round a pipe run belongs to.</summary>
    public readonly Dictionary<Vintagestory.API.MathTools.BlockPos, (Vintagestory.API.MathTools.BlockPos Node, bool Big)> WarmthOwners = new();

    /// <summary>Server side: which steam hub each cell of warm air round one belongs to, and how much of its warmth it gets.</summary>
    public readonly Dictionary<Vintagestory.API.MathTools.BlockPos, (Vintagestory.API.MathTools.BlockPos Hub, float Share)> HubWarmth = new();

    /// <summary>Server side: the middle of the keel of every wreck set down in this world. Kept with the save.</summary>
    public readonly List<Vintagestory.API.MathTools.BlockPos> Wrecks = new();

    private SteamNetwork? serverSteam;
    private SteamNetwork? clientSteam;

    /// <summary>The steam network on the side this world belongs to.</summary>
    public SteamNetwork SteamFor(IWorldAccessor world)
    {
        return world.Side == EnumAppSide.Server
            ? serverSteam ??= new SteamNetwork(world)
            : clientSteam ??= new SteamNetwork(world);
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("FrostStoryGeneratorConsole", typeof(BlockGeneratorConsole));
        api.RegisterBlockClass("FrostStorySteamConduit", typeof(BlockSteamConduit));
        api.RegisterBlockClass("FrostStorySteamEngine", typeof(BlockSteamEngine));
        api.RegisterBlockEntityBehaviorClass("FrostStorySteamEngine", typeof(BEBehaviorSteamEngine));
        api.RegisterBlockClass("FrostStorySteamMainFill", typeof(BlockSteamMainFill));
        api.RegisterBlockClass("FrostStorySteamMainStand", typeof(BlockSteamMainStand));
        api.RegisterBlockClass("FrostStoryPipeWarmth", typeof(BlockPipeWarmth));
        api.RegisterBlockEntityClass("FrostStorySteamNode", typeof(BlockEntitySteamNode));
        api.RegisterBlockClass("FrostStoryTentCloth", typeof(BlockTentCloth));
        api.RegisterBlockClass("FrostStoryTentFill", typeof(BlockTentFill));
        api.RegisterBlockEntityClass("FrostStorySteamValve", typeof(BlockEntitySteamValve));
        api.RegisterBlockEntityClass("FrostStorySteamGauge", typeof(BlockEntitySteamGauge));
        api.RegisterBlockClass("FrostStoryShipCell", typeof(FrostStory.Ship.BlockShipCell));
        api.RegisterBlockClass("FrostStoryPanel", typeof(FrostStory.Panels.BlockPanel));
        api.RegisterBlockClass("FrostStoryPanelCell", typeof(FrostStory.Panels.BlockPanelCell));
        api.RegisterBlockEntityClass("FrostStoryPanels", typeof(FrostStory.Panels.BlockEntityPanels));
        api.RegisterBlockClass("FrostStoryCoalHeap", typeof(BlockCoalHeap));
        api.RegisterBlockEntityClass("FrostStoryCoalHeap", typeof(BlockEntityCoalHeap));
        api.RegisterBlockEntityClass("FrostStoryBorer", typeof(BlockEntityBorer));
        api.RegisterBlockEntityClass("FrostStoryTentRod", typeof(BlockEntityTentRod));
        api.RegisterBlockClass("FrostStoryTentStove", typeof(BlockTentStove));
        api.RegisterBlockClass("FrostStoryStoveFlue", typeof(BlockStoveFlue));
        api.RegisterBlockEntityClass("FrostStoryTentStove", typeof(BlockEntityTentStove));
        api.RegisterBlockEntityClass("FrostStoryGeneratorConsole", typeof(BlockEntityGeneratorConsole));
    }

    /// <summary>The wrecked freighter's plan, from config/wreck.json.</summary>
    public FrostStory.Ship.WreckPlan Wreck { get; private set; } = new();

    public override void AssetsLoaded(ICoreAPI api)
    {
        try
        {
            Wreck = api.Assets.TryGet(new AssetLocation(Domain, "config/wreck.json"))?.ToObject<FrostStory.Ship.WreckPlan>() ?? new FrostStory.Ship.WreckPlan();
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Could not read config/wreck.json: {0}", e.Message);
        }

        var asset = api.Assets.TryGet(new AssetLocation(Domain, "config/generatorstages.json"));
        if (asset == null)
        {
            Mod.Logger.Error("config/generatorstages.json is missing; generator stages will need no materials.");
            return;
        }

        try
        {
            Stages = asset.ToObject<GeneratorStagesConfig>() ?? new GeneratorStagesConfig();
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Could not read config/generatorstages.json: {0}", e.Message);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        try
        {
            Config = api.LoadModConfig<FrostStoryConfig>(ConfigFile) ?? new FrostStoryConfig();
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not read ModConfig/{0}, using defaults: {1}", ConfigFile, e.Message);
            Config = new FrostStoryConfig();
        }

        api.StoreModConfig(Config, ConfigFile);

        api.Event.SaveGameLoaded += () =>
        {
            Wrecks.Clear();
            var saved = api.WorldManager.SaveGame.GetData<int[]>("froststory:wrecks") ?? Array.Empty<int>();
            for (int i = 0; i + 2 < saved.Length; i += 3) Wrecks.Add(new Vintagestory.API.MathTools.BlockPos(saved[i], saved[i + 1], saved[i + 2], 0));
        };
        api.Event.GameWorldSave += () =>
        {
            api.WorldManager.SaveGame.StoreData("froststory:wrecks", Wrecks.SelectMany(k => new[] { k.X, k.Y, k.Z }).ToArray());
        };
        GeneratorCommands.Register(api, this);
    }

    /// <summary>Material list for a stage, with the server's cost multiplier applied.</summary>
    public StageRequirement[] RequirementsFor(string stageId)
    {
        var stage = Stages.Find(stageId);
        if (stage == null || !Config.RequireMaterials) return Array.Empty<StageRequirement>();

        return stage.Requirements
            .Select(r => new StageRequirement
            {
                Code = r.Code,
                Name = r.Name,
                Quantity = Math.Max(1, (int)Math.Ceiling(r.Quantity * Config.CostMultiplier))
            })
            .ToArray();
    }

    public float BuildSecondsFor(string stageId)
    {
        float seconds = Stages.Find(stageId)?.BuildSeconds ?? 60f;
        return Math.Max(1f, seconds / Math.Max(0.01f, Config.BuildSpeedMultiplier));
    }

    public static bool Matches(string requirementCode, CollectibleObject collectible)
    {
        return WildcardUtil.Match(new AssetLocation(requirementCode), collectible.Code);
    }

    /// <summary>Every item or block that satisfies a requirement code (which may contain wildcards).</summary>
    public ItemStack[] StacksFor(IWorldAccessor world, string requirementCode)
    {
        if (stackCache.TryGetValue(requirementCode, out var cached)) return cached;

        var stacks = world.Collectibles
            .Where(c => c?.Code != null && Matches(requirementCode, c))
            .Take(24)
            .Select(c => new ItemStack(c))
            .ToArray();

        stackCache[requirementCode] = stacks;
        return stacks;
    }
}

/// <summary>Server settings, stored in ModConfig/froststory.json.</summary>
public class FrostStoryConfig
{
    /// <summary>False lets every stage start without delivering materials.</summary>
    public bool RequireMaterials = true;

    /// <summary>Scales every stage's material cost.</summary>
    public float CostMultiplier = 1f;

    /// <summary>Scales construction speed; 2 builds twice as fast.</summary>
    public float BuildSpeedMultiplier = 1f;

    /// <summary>Warmth given off by each pipe, fitting or port carrying steam. A firepit is 10.</summary>
    public float PipeHeatStrength = 2f;

    /// <summary>False lets the generator run without coal.</summary>
    public bool RequireCoal = true;

    /// <summary>Coal the generator burns an hour just to stay lit.</summary>
    public float IdleCoalPerHour = 2f;

    /// <summary>Coal the generator burns an hour for each unit of steam its ports send out.</summary>
    public float CoalPerSteamHour = 0.1f;

    /// <summary>Coal the generator's bunker holds.</summary>
    public int BunkerCapacity = 4000;

    /// <summary>Coal a bucket conveyor line can bring the bunker in an hour.</summary>
    public float ConveyorCoalPerHour = 400f;

    /// <summary>Coal a steam borer raises an hour with no coal in the rock round it.</summary>
    public float BorerCoalPerHour = 30f;

    /// <summary>Extra coal an hour for each block of coal ore found when the borer was set down.</summary>
    public float BorerCoalPerOre = 0.2f;

    /// <summary>The most ore blocks that count towards a borer's rate.</summary>
    public int BorerOreCap = 300;

    /// <summary>Steam a live port on the generator's base supplies. A radiator draws 6, a pipe 0.25 a block.</summary>
    public float PortSupply = 40f;

    /// <summary>Steam a live large port supplies. A steam hub draws 30.</summary>
    public float MainPortSupply = 250f;

    /// <summary>Warmth of the air close to a steam hub; it falls to 40% at the edge of its reach.</summary>
    public float HubHeatStrength = 8f;

    /// <summary>How far a steam hub warms the ground round it, in blocks (4 to 32).</summary>
    public int HubRadius = 12;

    /// <summary>Warmth given off by a lit tent stove. A firepit is 10.</summary>
    public float StoveHeatStrength = 10f;

    /// <summary>How many times longer fuel lasts in a tent stove than in a firepit.</summary>
    public float StoveFuelEfficiency = 3f;
}
