# FrostStory

A Vintage Story mod about holding back the cold with one enormous machine: a geothermal generator
you raise stage by stage. Built for Vintage Story **1.22** (.NET 10).

The genre is the frozen-city survival builder; every name, description, block and texture in this
repository is original to the mod.

## The generator

One block is one metre.

| | |
|---|---|
| Height above the deck | 40 m |
| Shaft depth | 80 m (less if the site is close to the bottom of the world; minimum 24) |
| Hull | octagon, 9 m across |
| Across the support frames | 11 m |
| Foundation pad | 17 m across |

The generator is not built out of one-metre blocks. It is assembled from large modelled segments, 25 kinds of them
(hull lifts, the 45 degree shoulder, the stack, braced frame lifts, pump towers, core, shaft lifts),
each a block shape several metres across, with octagonal walls, round pipes and true diagonals.
Invisible filler cells behind the models give you something to stand on, climb and bump into. Only
the foundation pad and the rock lining of the shaft are ordinary blocks.

It goes up in seven stages, each started from the control console:

1. **Foundation Shaft** – clears the site, lays the pad, sinks and lines the shaft in 8 m lifts, with
   a service ladder and a grated platform every 16 m.
2. **Regulator Valves** – the main riser, its wellhead, and three valve stations with relief ports.
3. **Injection Pumps** – four pump towers with flywheels on the pad, the feed-water ring main that
   links them, and their injection lines down the shaft.
4. **Steam Core** – the core vessel, standpipes, distribution header and exhaust line.
5. **Support Frame (east-west)** – paired columns with raking buttresses, cross-bracing and head beams.
6. **Support Frame (north-south)** – the second frame, lapped across the first.
7. **Thermal Hull** – the octagonal shell with doorways and louvres, heat-exchange risers up its
   flanks, the shoulder with its relief valves, the crown, and the stack under a weather cowl. Two
   outside service galleries (12 m and 24 m up) are reached by a ladder on the south frame, and
   there is a third gallery inside.

Then the console fires it: the core heart lights up and shows through the louvres, warms anyone near
it, and the stack steams.

## Playing

- Craft a **generator control console** (4 iron plates, 2 rusty gears) or take it from the
  FrostStory creative tab.
- Place it facing where the tower should stand. The centre is 8 blocks behind the console.
  Right-click it empty-handed to see the footprint and what the current stage needs.
- Right-click with the listed materials to deliver them. When a stage is paid for, it builds itself
  over a minute or two.
- In creative mode, sneak + right-click empty-handed to skip the materials.
- When all seven stages are done, right-click to fire the generator (and again to bank it).

Stage 1 removes whatever stands on the site, chests included. Breaking the console abandons the
project; what is built stays. The generator itself cannot be selected or broken by hand: use
`/frostgen demolish` to take one down.

### Admin commands (`controlserver` privilege)

| Command | Does |
|---|---|
| `/frostgen place [pos] [stages] [lit]` | Builds a generator at once, centred on a position |
| `/frostgen check [pos] [stages] [lit]` | Counts cells that differ from the blueprint |
| `/frostgen demolish [pos]` | Removes a generator, leaving the open shaft |
| `/frostgen console [pos] [side]` | Puts a console down |
| `/frostgen start [pos]` | Waives materials and starts the console's current stage |
| `/frostgen advance [pos]` | Finishes the console's current stage immediately |
| `/frostgen fire [pos] [on]` | Fires or banks a finished generator |
| `/frostgen status [pos]` | Reports a console's state |

Console commands take the console's position, or the one you are looking at.

### Configuration

- `ModConfig/froststory.json` (server): `RequireMaterials`, `CostMultiplier`, `BuildSpeedMultiplier`.
- `assets/froststory/config/generatorstages.json`: materials and build time per stage. It is a
  normal asset, so other mods can patch it.

## Building

Needs the .NET 10 SDK and a Vintage Story 1.22 install (client or server).

```bash
export VINTAGE_STORY=/path/to/vintagestory
```

```bash
dotnet build FrostStory/FrostStory.csproj -c Release
```

That writes `dist/froststory/` and `dist/froststory.zip`; copy either into your `Mods` folder.

## Layout

```
FrostStory/
  modinfo.json
  src/FrostStoryModSystem.cs            mod entry, config
  src/Generator/Blueprint/              where every segment and filler cell goes, pure C# (no game references)
  src/Generator/GeneratorBuilder.cs     writes blueprint cells into the world
  src/Generator/BlockEntityGeneratorConsole.cs   deliveries, timed construction, firing
  src/Generator/GeneratorCommands.cs    /frostgen
  assets/froststory/                    blocktypes, shapes, textures, lang, recipes, stage config
tools/
  gen_segments.py                       the geometry of the modelled segments; writes their shapes
  gen_assets.py                         regenerates the placeholder textures and small block shapes
  BlueprintExport/                      dumps the blueprint to JSON
  build_preview.py, preview/            builds a standalone 3D preview page
```

The look of the generator lives in `tools/gen_segments.py` (run it to rewrite the segment shapes);
where the segments and their collision cells go lives in `GeneratorBlueprint.cs`. The two share
dimensions, so change them together. The game and the preview both follow.

A segment is drawn as the model of one anchor block, so it is lit by the light at that block.

### Preview without the game

```bash
dotnet run --project tools/BlueprintExport -c Release -- tools/preview/blueprint.json
```

```bash
python3 tools/build_preview.py
```

Open `dist/generator-preview.html`. It draws the real blueprint with the mod's own shapes and
textures, stage by stage, with a section view of the shaft. (`pip install pillow` for the Python tools.)

## Status

Working and checked on a 1.22.7 dedicated server: assets load without errors, all seven stages
build through the console, firing and banking swap the lit models, and the result matches the
blueprint cell for cell. Every shape file loads cleanly through the game's own shape class.

Not yet checked in a game client: how the large segment models render and light in-engine, walking
and climbing on the invisible collision cells, the footprint highlight, the console's hover text
and the stack's steam particles. The preview page mirrors the engine's rotation and texture
mapping rules, but it is not the engine.

Next up:

- heat that reaches beyond the core (a warm zone around a running generator)
- fuel or upkeep for a running generator, and output levels
- proper intermediate parts instead of raw vanilla materials
- hand-painted textures to replace the procedural ones
