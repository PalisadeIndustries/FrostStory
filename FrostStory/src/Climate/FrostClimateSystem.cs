using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FrostStory.Climate;

/// <summary>The Long Winter: deep permanent cold, escalating whiteout storms and exposure.
/// Everything here does nothing unless the world config bool <c>froststoryLongWinter</c> is set.</summary>
public class FrostClimateSystem : ModSystem
{
    public const string FlagKey = "froststoryLongWinter";
    public const string ColdOffsetKey = "froststoryColdOffset";
    public const string StormFrequencyKey = "froststoryStormFrequency";
    public const string WindproofAttr = "froststoryWindproof";
    public const string StormVisionAttr = "froststoryStormVision";
    /// <summary>Tree in every player entity's WatchedAttributes that carries the shared climate state to clients.</summary>
    public const string SyncTree = "froststoryClimate";

    private const string ConfigFile = "froststory-climate.json";
    private const string SaveKey = "froststory:climate";

    public ClimateConfig Config { get; private set; } = new();
    public StormCycle Cycle { get; private set; } = new();

    /// <summary>True when this world is a Long Winter world. Read once the world config is known.</summary>
    public bool Enabled { get; private set; }
    /// <summary>The world's base cold offset in °C (negative), from world config.</summary>
    public float ColdOffset { get; private set; } = -10f;
    /// <summary>The world's storm frequency multiplier, from world config (0 turns storms off).</summary>
    public float StormFrequency { get; private set; } = 1f;

    // Current shared values on this side. Plain fields: OnGetClimate is called from many threads.
    private volatile float strength;
    private volatile float windAngle;
    private double days;
    private StormPhase phase;
    private volatile float cold;
    private volatile float windAdd;

    public float Strength => strength;
    public double Days => days;
    public StormPhase Phase => phase;
    public float WindAngle => windAngle;

    private ICoreAPI? api;
    private ICoreServerAPI? sapi;
    private StormExposure? exposure;
    public StormExposure? Exposure => exposure;
    private WhiteoutRenderer? whiteout;
    private readonly Random rand = new();
    private StormPhase? loggedPhase;
    private double loggedPhaseEnd;
    private int debugTicks;

    /// <summary>Current storm strength 0..1 on the side the world belongs to (0 when the Long Winter is off).</summary>
    public static float StormStrength(IWorldAccessor world)
    {
        var sys = world?.Api?.ModLoader.GetModSystem<FrostClimateSystem>();
        return sys != null && sys.Enabled ? sys.strength : 0f;
    }

    /// <summary>True if the world is a Long Winter world.</summary>
    public static bool IsLongWinter(IWorldAccessor world) => ReadBool(world?.Config, FlagKey);

    public override double ExecuteOrder() => 0.2;

    public override void Start(ICoreAPI api)
    {
        this.api = api;
        api.Event.OnGetClimate += OnGetClimate;
        api.Event.OnGetWindSpeed += OnGetWindSpeed;
    }

    /// <summary>Reads the world options. Called once the world config has arrived on this side.</summary>
    private void ReadWorldConfig()
    {
        var cfg = api!.World.Config;
        Enabled = ReadBool(cfg, FlagKey);
        ColdOffset = ReadFloat(cfg, ColdOffsetKey, -10f);
        StormFrequency = Math.Max(0, ReadFloat(cfg, StormFrequencyKey, 1f));
        cold = ColdOffset;
    }

    // ------------------------------------------------------------------ server

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        try
        {
            Config = api.LoadModConfig<ClimateConfig>(ConfigFile) ?? new ClimateConfig();
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not read ModConfig/{0}, using defaults: {1}", ConfigFile, e.Message);
            Config = new ClimateConfig();
        }
        api.StoreModConfig(Config, ConfigFile);

        api.Event.SaveGameLoaded += () =>
        {
            ReadWorldConfig();
            LoadCycle();
            if (!Enabled) return;
            if (double.IsNaN(Cycle.StartDay)) Cycle.StartDay = api.World.Calendar.TotalDays;
            Mod.Logger.Notification("Long Winter is on: cold offset {0} °C, storm frequency {1}.", ColdOffset, StormFrequency);
        };
        api.Event.GameWorldSave += () =>
        {
            if (Enabled) api.WorldManager.SaveGame.StoreData(SaveKey, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Cycle)));
        };
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
        {
            if (!Enabled) return;
            exposure = new StormExposure(this, api);
            api.Event.RegisterGameTickListener(OnServerTick, 1000);
            OnServerTick(0);
        });
        api.Event.PlayerJoin += player =>
        {
            if (Enabled) PushTo(player.Entity, true);
        };

        ClimateCommands.Register(api, this);
    }

    private void LoadCycle()
    {
        Cycle = new StormCycle();
        try
        {
            var data = sapi!.WorldManager.SaveGame.GetData(SaveKey);
            if (data != null) Cycle = JsonConvert.DeserializeObject<StormCycle>(Encoding.UTF8.GetString(data)) ?? new StormCycle();
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not read the saved storm cycle, starting afresh: {0}", e.Message);
        }
    }

    private void OnServerTick(float dt)
    {
        try
        {
            var cal = sapi!.World.Calendar;
            double hours = cal.TotalHours;
            days = Cycle.DaysSurvived(cal.TotalDays);

            Cycle.Advance(hours, days, StormFrequency, Config, rand);
            phase = Cycle.Phase;
            windAngle = Cycle.WindAngle;
            strength = Cycle.StrengthAt(hours);
            cold = ColdOffset - (float)Math.Min(Config.ColdDeepenMax, days * Config.ColdDeepenPerDay) - strength * Config.StormCold;
            windAdd = strength * Config.StormWind;

            if (loggedPhase != Cycle.Phase || loggedPhaseEnd != Cycle.PhaseEnd)
            {
                Mod.Logger.Notification("Storm cycle: {0} -> {1} for {2:0.0} h (peak {3:0.00}, day {4:0.0}).",
                    loggedPhase?.ToString() ?? "start", Cycle.Phase, Cycle.PhaseEnd - hours, Cycle.Peak, days);
                if (loggedPhase != null && loggedPhase != Cycle.Phase) Announce(Cycle.Phase);
                loggedPhase = Cycle.Phase;
                loggedPhaseEnd = Cycle.PhaseEnd;
            }
            if (++debugTicks % 60 == 0)
                Mod.Logger.Debug("Storm tick: {0}, strength {1:0.00}, cold {2:0.0} °C, day {3:0.0}.", phase, strength, cold, days);

            foreach (var player in sapi.World.AllOnlinePlayers)
            {
                if (player.Entity != null) PushTo(player.Entity, false);
            }
            exposure?.Tick(dt);
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Storm tick failed: {0}", e);
        }
    }

    /// <summary>Copies the shared state into the player's watched attributes, marking it dirty only when it moved.</summary>
    private void PushTo(Entity? entity, bool force)
    {
        if (entity == null) return;
        var tree = entity.WatchedAttributes.GetTreeAttribute(SyncTree);
        if (tree == null)
        {
            tree = new TreeAttribute();
            entity.WatchedAttributes[SyncTree] = tree;
            force = true;
        }
        float oldStrength = tree.GetFloat("strength", -1);
        double oldDays = tree.GetDouble("days", -1);
        int oldPhase = tree.GetInt("phase", -1);
        float oldAngle = tree.GetFloat("windAngle", -1);
        float oldCold = tree.GetFloat("cold", 999);
        if (!force && Math.Abs(oldStrength - strength) < 0.005f && Math.Abs(oldDays - days) < 0.05
            && oldPhase == (int)phase && Math.Abs(oldAngle - windAngle) < 0.001f && Math.Abs(oldCold - cold) < 0.05f) return;
        tree.SetFloat("strength", strength);
        tree.SetDouble("days", days);
        tree.SetInt("phase", (int)phase);
        tree.SetFloat("windAngle", windAngle);
        tree.SetFloat("cold", cold);
        tree.SetFloat("wind", windAdd);
        // The client never reads ModConfig, so the server's look settings travel with the state.
        tree.SetFloat("fogDensity", Config.WhiteoutFogDensity);
        tree.SetFloat("fogUnderRoof", Config.WhiteoutUnderRoof);
        tree.SetFloat("snow", Config.StormSnow);
        entity.WatchedAttributes.MarkPathDirty(SyncTree);
    }

    private void Announce(StormPhase newPhase)
    {
        string? key = newPhase switch
        {
            StormPhase.Building => "climate-storm-building",
            StormPhase.Storm => Cycle.Peak > 0.7f ? "climate-storm-severe" : "climate-storm-hit",
            StormPhase.Easing => "climate-storm-easing",
            StormPhase.Calm => "climate-storm-over",
            _ => null
        };
        if (key == null) return;
        string dir = Lang.Get("froststory:climate-wind-" + CompassFrom(windAngle));
        string msg = Lang.Get("froststory:" + key, dir, Math.Round(Cycle.PhaseEnd - Cycle.PhaseStart));
        sapi!.BroadcastMessageToAllGroups(msg, EnumChatType.Notification);
    }

    /// <summary>Where the wind comes from, as one of n, ne, e, se, s, sw, w, nw.</summary>
    public static string CompassFrom(float towardsAngle)
    {
        // wind blows towards (cos a, sin a) in (x, z); +x east, +z south. It comes from the opposite side.
        double fromX = -Math.Cos(towardsAngle), fromZ = -Math.Sin(towardsAngle);
        double deg = (Math.Atan2(fromX, -fromZ) * 180 / Math.PI + 360) % 360; // 0 = north, clockwise
        string[] names = { "n", "ne", "e", "se", "s", "sw", "w", "nw" };
        return names[(int)Math.Round(deg / 45) % 8];
    }

    // ------------------------------------------------------------------ client

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.LevelFinalize += () =>
        {
            ReadWorldConfig();
            if (!Enabled) return;
            whiteout = new WhiteoutRenderer(this, api);
            api.Event.RegisterGameTickListener(OnClientTick, 100);
        };
    }

    private void OnClientTick(float dt)
    {
        var entity = (api as ICoreClientAPI)?.World.Player?.Entity;
        var tree = entity?.WatchedAttributes.GetTreeAttribute(SyncTree);
        if (tree != null)
        {
            strength = tree.GetFloat("strength");
            days = tree.GetDouble("days");
            phase = (StormPhase)tree.GetInt("phase");
            windAngle = tree.GetFloat("windAngle");
            cold = tree.GetFloat("cold", ColdOffset);
            windAdd = tree.GetFloat("wind");
            Config.WhiteoutFogDensity = tree.GetFloat("fogDensity", Config.WhiteoutFogDensity);
            Config.WhiteoutUnderRoof = tree.GetFloat("fogUnderRoof", Config.WhiteoutUnderRoof);
            Config.StormSnow = tree.GetFloat("snow", Config.StormSnow);
        }
        whiteout?.Tick(dt);
    }

    // ------------------------------------------------------------------ both sides

    /// <summary>Total temperature change now, in °C (negative): the world's offset, the deepening with days and the storm.</summary>
    public float ColdNow => cold;

    private void OnGetClimate(ref ClimateCondition climate, BlockPos pos, EnumGetClimateMode mode, double totalDays)
    {
        if (!Enabled || climate == null || mode == EnumGetClimateMode.WorldGenValues) return;
        climate.Temperature += cold;
    }

    private void OnGetWindSpeed(Vec3d pos, ref Vec3d windSpeed)
    {
        if (!Enabled) return;
        double add = windAdd;
        if (add <= 0.001) return;
        float a = windAngle;
        windSpeed.X += Math.Cos(a) * add;
        windSpeed.Z += Math.Sin(a) * add;
    }

    /// <summary>Summed attribute over the worn character slots, capped at 1.</summary>
    public static float WornSum(IPlayer? player, string attribute)
    {
        var inv = player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
        if (inv == null) return 0;
        float sum = 0;
        foreach (var slot in inv)
        {
            var stack = slot?.Itemstack;
            if (stack == null) continue;
            sum += stack.Collectible?.Attributes?[attribute].AsFloat(0) ?? 0;
        }
        return GameMath.Clamp(sum, 0, 1);
    }

    public static bool ReadBool(ITreeAttribute? tree, string key)
    {
        var attr = tree?[key];
        return attr switch
        {
            null => false,
            BoolAttribute b => b.value,
            StringAttribute s => s.value?.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            IntAttribute i => i.value != 0,
            _ => false
        };
    }

    public static float ReadFloat(ITreeAttribute? tree, string key, float fallback)
    {
        var attr = tree?[key];
        return attr switch
        {
            FloatAttribute f => f.value,
            DoubleAttribute d => (float)d.value,
            IntAttribute i => i.value,
            LongAttribute l => l.value,
            StringAttribute s when float.TryParse(s.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v,
            _ => fallback
        };
    }

    public override void Dispose()
    {
        if (api != null)
        {
            api.Event.OnGetClimate -= OnGetClimate;
            api.Event.OnGetWindSpeed -= OnGetWindSpeed;
        }
        exposure?.Dispose();
        whiteout?.Dispose();
    }
}
