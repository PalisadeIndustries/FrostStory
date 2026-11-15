using System;

namespace FrostStory.Generator;

/// <summary>Contents of assets/froststory/config/generatorstages.json. Other mods can patch it.</summary>
public class GeneratorStagesConfig
{
    public StageConfig[] Stages = Array.Empty<StageConfig>();

    public StageConfig? Find(string id)
    {
        foreach (var stage in Stages)
        {
            if (stage.Id == id) return stage;
        }

        return null;
    }
}

public class StageConfig
{
    public string Id = "";

    /// <summary>How long the stage takes to assemble once its materials are in.</summary>
    public float BuildSeconds = 60f;

    public StageRequirement[] Requirements = Array.Empty<StageRequirement>();
}

public class StageRequirement
{
    /// <summary>Item or block code; may use wildcards, e.g. "game:stonebricks-*".</summary>
    public string Code = "";

    public int Quantity = 1;

    /// <summary>Lang key shown for wildcard requirements. Exact codes use the item's own name.</summary>
    public string? Name;
}
