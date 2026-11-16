namespace FrostStory.Climate;

/// <summary>Server tuning for the Long Winter, stored as ModConfig/froststory-climate.json.
/// Temperatures in °C, times in in-game hours unless the name says otherwise.</summary>
public class ClimateConfig
{
    // ---- Cold ----
    /// <summary>Extra cold added per day survived, on top of the world's cold offset.</summary>
    public float ColdDeepenPerDay = 0.25f;
    /// <summary>The most the cold can deepen with days survived.</summary>
    public float ColdDeepenMax = 15f;
    /// <summary>Extra cold at the height of a full-strength storm.</summary>
    public float StormCold = 8f;

    // ---- Storm cycle ----
    /// <summary>No storm before this many days have passed since the world began.</summary>
    public float FirstStormAfterDays = 1.5f;
    /// <summary>Calm between storms on day 0 (random in this range).</summary>
    public float CalmHoursMin = 36f;
    public float CalmHoursMax = 60f;
    /// <summary>The calm shrinks by this fraction per day survived (calm / (1 + days * x)).</summary>
    public float CalmShrinkPerDay = 0.05f;
    /// <summary>Shortest calm the storms ever leave.</summary>
    public float CalmHoursFloor = 8f;
    /// <summary>How long a storm takes to build, during which it is announced.</summary>
    public float BuildingHours = 2f;
    /// <summary>Length of the storm itself: base + per day, capped.</summary>
    public float StormHoursBase = 3f;
    public float StormHoursPerDay = 0.15f;
    public float StormHoursMax = 16f;
    /// <summary>How long a storm takes to die down.</summary>
    public float EasingHours = 2f;
    /// <summary>Peak strength (0..1): base + per day, capped at 1, plus or minus the jitter.</summary>
    public float PeakBase = 0.35f;
    public float PeakPerDay = 0.03f;
    public float PeakJitter = 0.1f;

    // ---- Weather ----
    /// <summary>Wind speed added at full storm strength (vanilla winds run 0 to about 1).</summary>
    public float StormWind = 1.2f;
    /// <summary>Snow intensity (0..1) shown at full storm strength.</summary>
    public float StormSnow = 1f;
    /// <summary>Fog density of a full whiteout (vanilla clear air is about 0.005).</summary>
    public float WhiteoutFogDensity = 0.08f;
    /// <summary>Share of the whiteout still seen from under a roof.</summary>
    public float WhiteoutUnderRoof = 0.3f;

    // ---- Exposure ----
    /// <summary>Body temperature lost per real minute outdoors in a full storm with no windproof gear.</summary>
    public float StormHeatLossPerMinute = 1.2f;
    /// <summary>Share of the loss under a roof but not in a closed room.</summary>
    public float UnderRoofExposure = 0.5f;
    /// <summary>A player near heat sources adding up to at least this strength is sheltered (a lit tent stove is 10, a steam pipe 2).</summary>
    public float ShelteringHeat = 1.5f;
    /// <summary>Lowest body temperature the storm alone can drive a player to.</summary>
    public float StormBodyTempFloor = 31f;
    /// <summary>Below this body temperature a player is hypothermic and slowed.</summary>
    public float HypothermiaBelow = 35f;
    /// <summary>Walk speed lost while hypothermic (0.3 = 30% slower), growing to double that 4 °C further down.</summary>
    public float HypothermiaSlow = 0.25f;
}
