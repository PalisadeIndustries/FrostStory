using System;
using Vintagestory.API.MathTools;

namespace FrostStory.Climate;

public enum StormPhase { Calm, Building, Storm, Easing }

/// <summary>The server's whiteout cycle: calm, building, storm, easing, and round again. Storms come
/// more often, last longer and hit harder as the days go on. Kept with the save as JSON.</summary>
public class StormCycle
{
    public StormPhase Phase = StormPhase.Calm;
    /// <summary>In-game total hours when the current phase began and when it ends.</summary>
    public double PhaseStart;
    public double PhaseEnd;
    /// <summary>Strength the current (or next) storm peaks at, 0..1.</summary>
    public float Peak;
    /// <summary>Direction the storm wind blows towards, in radians.</summary>
    public float WindAngle;
    /// <summary>Total days on the calendar when the Long Winter began in this world.</summary>
    public double StartDay = double.NaN;
    /// <summary>Days added by /frostclimate days, for testing.</summary>
    public double DayOffset;
    /// <summary>Set once the cycle has been laid out at least once.</summary>
    public bool Started;

    public double DaysSurvived(double totalDays) => Math.Max(0, totalDays - (double.IsNaN(StartDay) ? totalDays : StartDay) + DayOffset);

    /// <summary>Storm strength 0..1 at the given time.</summary>
    public float StrengthAt(double totalHours)
    {
        double len = Math.Max(1e-3, PhaseEnd - PhaseStart);
        float t = (float)GameMath.Clamp((totalHours - PhaseStart) / len, 0, 1);
        switch (Phase)
        {
            case StormPhase.Building:
                return Peak * 0.5f * t;
            case StormPhase.Storm:
            {
                float ramp = Math.Min(1f, t * 4f);
                // gusts: a few slow swells over the storm, never above the peak
                float gust = 0.92f + 0.08f * (float)Math.Sin(totalHours * 3.1 + WindAngle * 5);
                return GameMath.Clamp(Peak * (0.5f + 0.5f * ramp) * gust, 0, 1);
            }
            case StormPhase.Easing:
                return Peak * (1 - t) * 0.9f;
            default:
                return 0;
        }
    }

    /// <summary>Moves to the next phase if the current one is over. Returns true if the phase changed.</summary>
    public bool Advance(double totalHours, double days, float frequency, ClimateConfig cfg, Random rand)
    {
        if (!Started)
        {
            Started = true;
            Phase = StormPhase.Calm;
            PhaseStart = totalHours;
            PhaseEnd = totalHours + Math.Max(cfg.FirstStormAfterDays * 24 - days * 24, 0) + CalmHours(days, frequency, cfg, rand) * 0.25;
            return true;
        }
        if (totalHours < PhaseEnd) return false;
        if (Phase == StormPhase.Calm && frequency <= 0)
        {
            PhaseEnd = totalHours + 24;
            return false;
        }

        switch (Phase)
        {
            case StormPhase.Calm:
                Peak = GameMath.Clamp(cfg.PeakBase + cfg.PeakPerDay * (float)days * Math.Max(frequency, 0.25f)
                                      + (float)(rand.NextDouble() * 2 - 1) * cfg.PeakJitter, 0.15f, 1f);
                WindAngle = (float)(rand.NextDouble() * Math.PI * 2);
                Begin(StormPhase.Building, totalHours, cfg.BuildingHours);
                break;
            case StormPhase.Building:
                Begin(StormPhase.Storm, totalHours, StormHours(days, cfg, rand));
                break;
            case StormPhase.Storm:
                Begin(StormPhase.Easing, totalHours, cfg.EasingHours);
                break;
            default:
                Begin(StormPhase.Calm, totalHours, CalmHours(days, frequency, cfg, rand));
                break;
        }
        return true;
    }

    public void Begin(StormPhase phase, double now, double hours)
    {
        Phase = phase;
        PhaseStart = now;
        PhaseEnd = now + Math.Max(0.05, hours);
    }

    public static double CalmHours(double days, float frequency, ClimateConfig cfg, Random rand)
    {
        double baseHours = cfg.CalmHoursMin + rand.NextDouble() * Math.Max(0, cfg.CalmHoursMax - cfg.CalmHoursMin);
        double hours = baseHours / (1 + days * cfg.CalmShrinkPerDay * Math.Max(frequency, 0.25f)) / Math.Max(frequency, 0.1f);
        return Math.Max(cfg.CalmHoursFloor / Math.Max(frequency, 0.1f), hours);
    }

    public static double StormHours(double days, ClimateConfig cfg, Random rand)
    {
        double hours = cfg.StormHoursBase + cfg.StormHoursPerDay * days;
        return Math.Min(cfg.StormHoursMax, hours) * (0.8 + rand.NextDouble() * 0.4);
    }
}
