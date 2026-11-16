using System;
using System.Collections.Generic;

namespace FrostStory.Ship;

/// <summary>
/// The wrecked freighter as it is laid down: read from <c>config/wreck.json</c>, which
/// <c>tools/gen_ship.py</c> writes together with the ship's models. Cells are relative to the middle of
/// the keel, bow to the east (+X).
///
/// Almost every cell belongs to a group: a section of hull with everything built into it, or a fitting
/// such as the engine or a boiler. A group is drawn by one model and is broken up whole.
/// </summary>
public class WreckPlan
{
    public int MinX, MaxX, HalfBeam, Height, Waterline;

    /// <summary>Block codes, indexed by the cells.</summary>
    public string[] Codes = Array.Empty<string>();

    /// <summary>Five numbers a cell: x, y, z, index into <see cref="Codes"/>, group (-1 for none).</summary>
    public int[] Cells = Array.Empty<int>();

    public WreckGroup[] Groups = Array.Empty<WreckGroup>();

    /// <summary>Four numbers a column: x, least z, greatest z, greatest y. Everything in them is cleared first.</summary>
    public int[] Clear = Array.Empty<int>();

    /// <summary>Three numbers a cell: where ice is packed round the hull.</summary>
    public int[] Ice = Array.Empty<int>();

    private Dictionary<(int, int, int), int>? groupAt;
    private List<(int, int, int)>[]? cellsOf;

    public int CellCount => Cells.Length / 5;

    private void Index()
    {
        if (groupAt != null) return;
        groupAt = new Dictionary<(int, int, int), int>();
        cellsOf = new List<(int, int, int)>[Groups.Length];
        for (int g = 0; g < Groups.Length; g++) cellsOf[g] = new List<(int, int, int)>();
        for (int i = 0; i + 4 < Cells.Length; i += 5)
        {
            int group = Cells[i + 4];
            if (group < 0 || group >= Groups.Length) continue;
            var cell = (Cells[i], Cells[i + 1], Cells[i + 2]);
            groupAt[cell] = group;
            cellsOf![group].Add(cell);
        }
    }

    /// <summary>The group a cell belongs to, or -1.</summary>
    public int GroupAt(int x, int y, int z)
    {
        Index();
        return groupAt!.TryGetValue((x, y, z), out int group) ? group : -1;
    }

    public IReadOnlyList<(int X, int Y, int Z)> CellsOf(int group)
    {
        Index();
        return cellsOf![group];
    }
}

public class WreckGroup
{
    public string Name = "";

    /// <summary>What it yields when broken up: item or block codes and how many of each.</summary>
    public WreckDrop[] Drops = Array.Empty<WreckDrop>();
}

public class WreckDrop
{
    public string Code = "";
    public int Quantity;
}
