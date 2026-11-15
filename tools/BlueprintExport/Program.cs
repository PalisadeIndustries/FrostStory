using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FrostStory.Generator.Blueprint;

int depth = args.Length > 1 ? int.Parse(args[1]) : GeneratorBlueprint.MaxDepth;
string outPath = args.Length > 0 ? args[0] : "blueprint.json";

var codes = new List<string>();
var codeIndex = new Dictionary<string, int>();

int IndexOf(string? code)
{
    if (code == null) return -1;
    if (!codeIndex.TryGetValue(code, out int i))
    {
        i = codes.Count;
        codeIndex[code] = i;
        codes.Add(code);
    }

    return i;
}

var stages = GeneratorBlueprint.Build(depth)
    .Select(s => new
    {
        id = s.Id,
        cells = s.Placements.SelectMany(p => new[] { p.X, p.Y, p.Z, IndexOf(p.Code) }).ToArray()
    })
    .ToArray();

IndexOf(Blocks.CoreHeartLit);
IndexOf(Blocks.Console + "-north");

var doc = new
{
    depth = GeneratorBlueprint.ClampDepth(depth),
    height = GeneratorBlueprint.Height,
    consoleDistance = GeneratorBlueprint.ConsoleDistance,
    heart = GeneratorBlueprint.CoreHeartCells().SelectMany(c => new[] { c.X, c.Y, c.Z }).ToArray(),
    codes,
    stages
};

File.WriteAllText(outPath, JsonSerializer.Serialize(doc));
Console.WriteLine($"Wrote {outPath}: {stages.Length} stages, {stages.Sum(s => s.cells.Length / 4)} placements, {codes.Count} block codes.");
