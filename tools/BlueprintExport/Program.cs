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
        steps = s.Steps.Select(step => new
        {
            key = step.Key,
            number = step.Number,
            of = step.Of,
            weight = step.Weight,
            cells = step.Placements.SelectMany(p => new[] { p.X, p.Y, p.Z, IndexOf(p.Code) }).ToArray()
        }).ToArray()
    })
    .ToArray();

var swaps = GeneratorBlueprint.LitSwaps.SelectMany(s => new[] { s.X, s.Y, s.Z, IndexOf(s.Cold), IndexOf(s.Lit) }).ToArray();
IndexOf(Blocks.Console + "-north");

var doc = new
{
    depth = GeneratorBlueprint.ClampDepth(depth),
    height = GeneratorBlueprint.Height,
    consoleDistance = GeneratorBlueprint.ConsoleDistance,
    swaps,
    codes,
    stages
};

File.WriteAllText(outPath, JsonSerializer.Serialize(doc));
Console.WriteLine($"Wrote {outPath}: {stages.Length} stages, {stages.Sum(s => s.steps.Length)} pieces, {stages.Sum(s => s.steps.Sum(p => p.cells.Length / 4))} placements, {codes.Count} block codes.");
