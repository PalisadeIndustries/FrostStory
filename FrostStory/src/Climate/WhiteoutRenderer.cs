using System;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FrostStory.Climate;

/// <summary>Client side whiteout: white fog that closes in with storm strength (thinned by worn
/// <c>froststoryStormVision</c> gear and under a roof) and heavy driven snow.</summary>
public class WhiteoutRenderer : IDisposable
{
    private const string AmbientKey = "froststory-whiteout";

    private readonly FrostClimateSystem sys;
    private readonly ICoreClientAPI capi;
    private readonly AmbientModifier ambient;
    private float fog;          // smoothed 0..1
    private float snow;         // smoothed 0..1

    // vanilla weather snapshot hook, reached by reflection (it lives in VSEssentials)
    private object? weatherSystem;
    private EventInfo? blendedEvent;
    private Delegate? blendedHandler;
    private FieldInfo? precIntensity, precType, nowPrecType;
    private object? snowValue;

    public WhiteoutRenderer(FrostClimateSystem sys, ICoreClientAPI capi)
    {
        this.sys = sys;
        this.capi = capi;
        ambient = new AmbientModifier
        {
            FogColor = new WeightedFloatArray(new[] { 0.86f, 0.89f, 0.93f }, 0),
            FogDensity = new WeightedFloat(0, 0),
            FogMin = new WeightedFloat(0, 0),
            AmbientColor = new WeightedFloatArray(new[] { 0.9f, 0.93f, 1f }, 0),
            CloudDensity = new WeightedFloat(1, 0),
        }.EnsurePopulated();
        capi.Ambient.CurrentModifiers[AmbientKey] = ambient;
        HookWeather();
    }

    private void HookWeather()
    {
        try
        {
            foreach (var system in capi.ModLoader.Systems)
            {
                if (system.GetType().FullName != "Vintagestory.GameContent.WeatherSystemClient") continue;
                weatherSystem = system;
                break;
            }
            if (weatherSystem == null) return;
            blendedEvent = weatherSystem.GetType().GetEvent("OnGetBlendedWeatherData");
            if (blendedEvent?.EventHandlerType == null) return;
            var snapType = blendedEvent.EventHandlerType.GetMethod("Invoke")!.GetParameters()[0].ParameterType;
            precIntensity = snapType.GetField("PrecIntensity");
            precType = snapType.GetField("BlendedPrecType");
            nowPrecType = snapType.GetField("nowPrecType");
            if (precType != null) snowValue = Enum.Parse(precType.FieldType, "Snow");
            var method = typeof(WhiteoutRenderer).GetMethod(nameof(OnBlendedWeather), BindingFlags.NonPublic | BindingFlags.Instance)!;
            blendedHandler = Delegate.CreateDelegate(blendedEvent.EventHandlerType, this, method);
            blendedEvent.AddEventHandler(weatherSystem, blendedHandler);
        }
        catch (Exception e)
        {
            sys.Mod.Logger.Warning("Whiteout snow could not hook the weather: {0}", e.Message);
            blendedHandler = null;
        }
    }

    private void OnBlendedWeather(object snapshot)
    {
        if (snow <= 0.01f || precIntensity == null) return;
        float cur = (float)precIntensity.GetValue(snapshot)!;
        precIntensity.SetValue(snapshot, Math.Max(cur, snow));
        if (snowValue != null)
        {
            precType?.SetValue(snapshot, snowValue);
            nowPrecType?.SetValue(snapshot, snowValue);
        }
    }

    public void Tick(float dt)
    {
        var player = capi.World.Player;
        var entity = player?.Entity;
        if (entity == null) return;
        var cfg = sys.Config;
        float strength = sys.Strength;

        float vision = FrostClimateSystem.WornSum(player, FrostClimateSystem.StormVisionAttr);
        var pos = entity.Pos;
        int rainHeight = capi.World.BlockAccessor.GetRainMapHeightAt((int)pos.X, (int)pos.Z);
        bool underRoof = rainHeight > pos.Y + entity.LocalEyePos.Y;
        float targetFog = strength * (1 - vision) * (underRoof ? cfg.WhiteoutUnderRoof : 1f);
        float targetSnow = strength * cfg.StormSnow;

        float k = GameMath.Clamp(dt * 0.6f, 0, 1);
        fog += (targetFog - fog) * k;
        snow += (targetSnow - snow) * k;

        float w = GameMath.Clamp(fog * 1.2f, 0, 1);
        ambient.FogDensity.Value = cfg.WhiteoutFogDensity * fog;
        ambient.FogDensity.Weight = w;
        ambient.FogMin.Value = 0.85f * fog;
        ambient.FogMin.Weight = w;
        ambient.FogColor.Weight = w;
        ambient.AmbientColor.Weight = w * 0.4f;
        ambient.CloudDensity.Weight = GameMath.Clamp(strength, 0, 1);
    }

    public void Dispose()
    {
        capi.Ambient.CurrentModifiers.Remove(AmbientKey);
        try
        {
            if (blendedEvent != null && blendedHandler != null && weatherSystem != null)
                blendedEvent.RemoveEventHandler(weatherSystem, blendedHandler);
        }
        catch { }
    }
}
