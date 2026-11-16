using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FrostStory.Expedition;

/// <summary>
/// The expedition start of a Long Winter world: the wreck set down by the spawn when the world is created,
/// the starting kit on a player's first join, the first day's hints, and the cold-seasoned trait of the
/// expedition survivor class. Does nothing in a world without the <c>froststoryLongWinter</c> flag.
/// </summary>
public class ExpeditionSystem : ModSystem
{
    public const string LongWinterFlag = "froststoryLongWinter";
    public const string SurvivorClass = "expeditionsurvivor";
    public const string ColdSeasonedTrait = "coldseasoned";
    private const string ConfigFile = "froststory-expedition.json";
    private const string WreckKey = "froststory:expeditionWreck";

    public enum WreckState { None = 0, Pending = 1, Placed = 2 }

    public ExpeditionConfig Config { get; private set; } = new();
    public ExpeditionKitList Kit { get; private set; } = new();

    public WreckState State { get; private set; }
    public BlockPos? Keel { get; private set; }

    private ICoreServerAPI sapi = null!;
    private bool newWorld;
    private bool searching;
    private readonly Dictionary<string, float> lastBodyTemp = new();

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    /// <summary>Whether this world is a Long Winter world.</summary>
    public bool LongWinter => sapi?.World?.Config?.GetBool(LongWinterFlag, false) == true;

    public override void AssetsLoaded(ICoreAPI api)
    {
        try
        {
            Kit = api.Assets.TryGet(new AssetLocation(FrostStoryModSystem.Domain, "config/expeditionkit.json"))?.ToObject<ExpeditionKitList>() ?? new ExpeditionKitList();
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not read config/expeditionkit.json, the kit will be empty: {0}", e.Message);
            Kit = new ExpeditionKitList();
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        try
        {
            Config = api.LoadModConfig<ExpeditionConfig>(ConfigFile) ?? new ExpeditionConfig();
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not read ModConfig/{0}, using defaults: {1}", ConfigFile, e.Message);
            Config = new ExpeditionConfig();
        }

        Config.Clamp();
        api.StoreModConfig(Config, ConfigFile);

        api.Event.SaveGameLoaded += OnSaveGameLoaded;
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, OnRunGame);
        api.Event.PlayerReady += OnPlayerReady;
        ExpeditionCommands.Register(api, this);
    }

    private void OnSaveGameLoaded()
    {
        var saved = sapi.WorldManager.SaveGame.GetData<int[]>(WreckKey);
        if (saved != null && saved.Length >= 4)
        {
            State = (WreckState)saved[0];
            Keel = State == WreckState.Placed ? new BlockPos(saved[1], saved[2], saved[3], 0) : null;
        }

        newWorld = sapi.WorldManager.SaveGame.IsNew;
        if (newWorld && LongWinter && Config.Wreck && State == WreckState.None)
        {
            State = WreckState.Pending;      // kept until she is in, across restarts
            StoreState();
        }
    }

    private void OnRunGame()
    {
        if (!LongWinter) return;

        var resolved = ExpeditionKit.Resolve(sapi.World, Kit, Mod.Logger);
        var chars = sapi.ModLoader.GetModSystem<CharacterSystem>();
        bool hasClass = chars?.characterClasses?.Any(c => c.Code == SurvivorClass) == true;
        Mod.Logger.Notification("Expedition start: Long Winter world; kit {0} of {1} entries found; survivor class {2}; wreck {3}.",
            resolved.Count, Kit.Items.Length, hasClass ? "loaded" : "MISSING", State);
        if (!hasClass) Mod.Logger.Warning("Expedition start: the '{0}' character class did not load; check patches/expedition-classes.json.", SurvivorClass);

        sapi.Event.RegisterGameTickListener(ColdSeasonedTick, 2000);
        if (State == WreckState.Pending) sapi.Event.RegisterCallback(_ => BeginWreck(null), 1000);
    }

    // ------------------------------------------------------------------------------------------- the wreck

    /// <summary>
    /// Loads the ground round the world spawn and, once it is all there, sets the wreck down on the best site
    /// and moves the spawn beside her. <paramref name="report"/> hears how it went.
    /// </summary>
    public void BeginWreck(Action<string>? report)
    {
        if (searching)
        {
            report?.Invoke("The wreck's site is already being looked for.");
            return;
        }

        var plan = sapi.ModLoader.GetModSystem<FrostStoryModSystem>().Wreck;
        if (plan.CellCount == 0)
        {
            Mod.Logger.Error("Expedition wreck: the wreck's plan (config/wreck.json) is empty; she cannot be set down.");
            report?.Invoke("The wreck's plan is empty.");
            return;
        }

        var spawn = sapi.World.DefaultSpawnPosition?.AsBlockPos
            ?? new BlockPos(sapi.WorldManager.MapSizeX / 2, 0, sapi.WorldManager.MapSizeZ / 2, 0);
        var area = ExpeditionWreck.ChunkArea(spawn, Config.WreckDistance, plan);
        Mod.Logger.Notification("Expedition wreck: loading chunk columns {0},{1} to {2},{3} round the spawn at {4} {5}.", area.X1, area.Z1, area.X2, area.Z2, spawn.X, spawn.Z);
        sapi.WorldManager.LoadChunkColumnPriority(area.X1, area.Z1, area.X2, area.Z2, new ChunkLoadOptions { KeepLoaded = true });

        searching = true;
        long started = sapi.World.ElapsedMilliseconds;
        void Poll(float _)
        {
            if (!ExpeditionWreck.AreaLoaded(sapi, area))
            {
                if (sapi.World.ElapsedMilliseconds - started < 10 * 60 * 1000)
                {
                    sapi.Event.RegisterCallback(Poll, 1000);
                    return;
                }

                searching = false;
                Mod.Logger.Warning("Expedition wreck: the ground round the spawn did not load within ten minutes; trying again at the next start.");
                report?.Invoke("The ground round the spawn did not load in time.");
                return;
            }

            searching = false;
            try
            {
                string text = SetDown(spawn, plan, report);
                report?.Invoke(text);
            }
            catch (Exception e)
            {
                Mod.Logger.Error("Expedition wreck: setting her down failed: {0}", e);
                report?.Invoke("Setting the wreck down failed; see the server log.");
            }
        }

        sapi.Event.RegisterCallback(Poll, 1000);
    }

    private string SetDown(BlockPos spawn, Ship.WreckPlan plan, Action<string>? report)
    {
        var keel = ExpeditionWreck.FindSite(sapi, spawn, Config.WreckDistance, plan, out string summary);
        if (keel == null)
        {
            Mod.Logger.Warning("Expedition wreck: no site found round the spawn.");
            return "No site found round the spawn.";
        }

        Mod.Logger.Notification("Expedition wreck: site chosen, {0}.", summary);
        return Place(keel, report == null);
    }

    /// <summary>
    /// Sets the wreck down with her keel here, moves the world spawn beside the hole in her side and remembers her.
    /// With <paramref name="gather"/>, survivors already in a world created this session are moved to the new spawn.
    /// </summary>
    public string Place(BlockPos keel, bool gather)
    {
        var plan = sapi.ModLoader.GetModSystem<FrostStoryModSystem>().Wreck;
        int placed = ExpeditionWreck.Lay(sapi.World, keel, Config.WreckFillDepth);
        if (placed < 0)
        {
            Mod.Logger.Warning("Expedition wreck: part of the site at {0} is not loaded.", keel);
            return "Part of the site is in unloaded chunks.";
        }

        var stand = ExpeditionWreck.SpawnBeside(sapi.World, keel, plan);
        // a new world has no stored spawn yet (it is worked out from the map), and setting one needs it
        var save = sapi.WorldManager.SaveGame;
        if (save.DefaultSpawn == null) save.DefaultSpawn = new PlayerSpawnPos(stand.X, stand.Y, stand.Z);
        sapi.WorldManager.SetDefaultSpawnPosition(stand.X, stand.Y, stand.Z);
        State = WreckState.Placed;
        Keel = keel.Copy();
        StoreState();

        // whoever is already here in a world that is new this session starts beside her too
        if (gather && newWorld)
        {
            foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
            {
                if (player.Entity == null || player.WorldData.CurrentGameMode != EnumGameMode.Survival) continue;
                player.Entity.TeleportTo(stand.ToVec3d().Add(0.5, 0, 0.5));
            }
        }

        string text = $"The wreck is in: {placed} blocks, keel at {keel.X} {keel.Y} {keel.Z}; the world spawn is now at {stand.X} {stand.Y} {stand.Z}, beside the hole in her port side.";
        Mod.Logger.Notification("Expedition wreck: {0}", text);
        return text;
    }

    private void StoreState()
    {
        var k = Keel ?? new BlockPos(0, 0, 0, 0);
        sapi.WorldManager.SaveGame.StoreData(WreckKey, new[] { (int)State, k.X, k.Y, k.Z });
    }

    // ------------------------------------------------------------------------------------- kit and hints

    private void OnPlayerReady(IServerPlayer player)
    {
        if (!LongWinter || !Config.Kit) return;
        if (player.WorldData.CurrentGameMode != EnumGameMode.Survival) return;
        if (player.GetModData(ExpeditionKit.GivenKey, false)) return;

        GiveKit(player);
        if (Config.Guidance) ScheduleHints(player);
    }

    /// <summary>Hands the kit over and marks it as had.</summary>
    public int GiveKit(IServerPlayer player)
    {
        int given = ExpeditionKit.Give(player, Kit, Mod.Logger);
        player.SetModData(ExpeditionKit.GivenKey, true);
        player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "froststory:expedition-kit-given"), EnumChatType.Notification);
        Mod.Logger.Notification("Expedition kit: {0} stacks given to {1}.", given, player.PlayerName);
        return given;
    }

    public static void ResetKit(IServerPlayer player) => player.RemoveModdata(ExpeditionKit.GivenKey);

    private static readonly (int Seconds, string Key, bool NeedsWreck)[] Hints =
    {
        (6, "expedition-hint-arrival", true),
        (45, "expedition-hint-shelter", false),
        (100, "expedition-hint-stove", false),
        (170, "expedition-hint-salvage", true),
        (250, "expedition-hint-generator", false),
    };

    private void ScheduleHints(IServerPlayer player)
    {
        string uid = player.PlayerUID;
        foreach (var (seconds, key, needsWreck) in Hints)
        {
            if (needsWreck && State != WreckState.Placed) continue;
            sapi.Event.RegisterCallback(_ =>
            {
                if (sapi.World.PlayerByUid(uid) is not IServerPlayer p || p.ConnectionState != EnumClientState.Playing) return;
                p.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(p.LanguageCode, "froststory:" + key), EnumChatType.Notification);
            }, seconds * 1000);
        }
    }

    // ------------------------------------------------------------------------------------ cold-seasoned

    /// <summary>A cold-seasoned survivor keeps part of the body heat the cold would take.</summary>
    private void ColdSeasonedTick(float dt)
    {
        if (Config.ColdSeasonedKeep <= 0) return;
        var chars = sapi.ModLoader.GetModSystem<CharacterSystem>();
        if (chars == null) return;

        foreach (var player in sapi.World.AllOnlinePlayers)
        {
            var entity = player.Entity;
            if (entity == null || !entity.Alive || !chars.HasTrait(player, ColdSeasonedTrait))
            {
                lastBodyTemp.Remove(player.PlayerUID);
                continue;
            }

            var body = entity.GetBehavior<EntityBehaviorBodyTemperature>();
            if (body == null) continue;
            float now = body.CurBodyTemperature;
            if (lastBodyTemp.TryGetValue(player.PlayerUID, out float before) && now < before && before - now < 4f)
            {
                now += (before - now) * Config.ColdSeasonedKeep;
                body.CurBodyTemperature = now;
            }

            lastBodyTemp[player.PlayerUID] = now;
        }
    }
}
