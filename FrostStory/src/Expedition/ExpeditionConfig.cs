using System;

namespace FrostStory.Expedition;

/// <summary>Server settings for the expedition start, stored in ModConfig/froststory-expedition.json.</summary>
public class ExpeditionConfig
{
    /// <summary>Give each player the starting kit once, on their first join to a Long Winter world.</summary>
    public bool Kit = true;

    /// <summary>Set the wreck down near the world spawn when a Long Winter world is created, and move the spawn beside her.</summary>
    public bool Wreck = true;

    /// <summary>How far from the world spawn, in blocks, the middle of the wreck may lie (8 to 256).</summary>
    public int WreckDistance = 48;

    /// <summary>Pack ice under the ice sheet and the hull where the ground falls away, at most this many blocks deep (0 for none).</summary>
    public int WreckFillDepth = 12;

    /// <summary>Send a new survivor the short timed hints of the first day.</summary>
    public bool Guidance = true;

    /// <summary>How much of the body heat a survivor with the cold-seasoned trait would lose to the cold is kept (0 to 0.9).</summary>
    public float ColdSeasonedKeep = 0.35f;

    public void Clamp()
    {
        WreckDistance = Math.Clamp(WreckDistance, 8, 256);
        WreckFillDepth = Math.Clamp(WreckFillDepth, 0, 48);
        ColdSeasonedKeep = Math.Clamp(ColdSeasonedKeep, 0f, 0.9f);
    }
}
