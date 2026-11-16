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
| Shaft | 7 m across at the mouth and for a 3 m throat under the pad, then 11 m across |
| Hull | octagon, 9 m across |
| Across the support frames | 11 m |
| Foundation pad | 17 m across |

The generator is not built out of one-metre blocks. It is assembled from large modelled segments, 25 kinds of them
(hull lifts, the 45 degree shoulder, the stack, braced frame lifts, pump towers, core, shaft lifts),
each a block shape several metres across, with octagonal walls, round pipes and true diagonals.
Invisible filler cells behind the models give you something to stand on, climb and bump into. Only
the foundation pad and the rock lining of the shaft are ordinary blocks.

It goes up in eight stages from the control console, and each stage goes up piece by piece: 45
pieces in all at full depth. A stage's materials are delivered to one running total, and every
piece is raised as soon as that total covers its share. Deliver a third of a stage and a third of
it stands there, waiting for the rest.

1. **Foundation Shaft** – clears the site, lays the pad and raises a headframe over the dig, then
   sinks and lines the shaft one 8 m lift at a time. The tower stands on the rim of the mouth, so
   the first 3 m is a throat of that width; below it the shaft opens out by two blocks all round.
   Every 16 m a grated catwalk runs round the wall, leaving the middle open. The service ladder
   runs down the throat wall from the deck hatch and hangs free to a landing on the first
   catwalk; from there a second ladder runs down the shaft wall, through a hatch in every catwalk. The headframe stays until the core takes its place. (11 pieces)
2. **Regulator Valves** – the main riser (nearly 2 m across), its wellhead, and three valve stations
   whose arms reach the wall over the catwalks, with inverted valves and relief ports,
   in 16 m sections from the bottom. (5 pieces)
3. **Injection Pumps** – four pump towers with flywheels on the pad, the feed-water ring main that
   links them, and their injection lines down the shaft: the lines first, then the ring main, then
   one tower at a time. (10 pieces)
4. **Steam Core** – the plinth, lower vessel and steam ports, then the upper vessel, the
   distribution header and the exhaust line. (4 pieces)
5. **Support Frame (east-west)** – paired columns with raking buttresses, cross-bracing and head
   beams, in three 12 m lifts. (3 pieces)
6. **Support Frame (north-south)** – the second frame, lapped across the first. (3 pieces)
7. **Thermal Hull** – the octagonal shell with doorways and louvres, heat-exchange risers up its
   flanks, the shoulder with its relief valves, the crown, and the stack under a weather cowl. Two
   outside service galleries (12 m and 24 m up) are reached by a ladder on the south frame, and
   there is a third gallery inside. The hull goes up as three lifts, the shoulder, then the stack.
   (5 pieces)
8. **Insulating Jacket** – an outer shell of eight panels round the upper tower from 20 m up. It
   stands a block and a half clear of the galleries on beams and raking struts, and hoods in at
   45 degrees below the stack. It goes up in opposite pairs: east and west, north and south, then
   the two diagonal pairs. (4 pieces)

Then the console fires it: the core heart lights up and shows through the louvres, warms anyone near
it, and the stack steams.

## Playing

- Craft a **generator control console** (4 iron plates, 2 rusty gears) or take it from the
  FrostStory creative tab.
- Place it facing where the tower should stand. The centre is 8 blocks behind the console.
  Right-click it empty-handed to see the footprint and what the current stage needs.
- Right-click with the listed materials to deliver them, as much or as little as you have. The
  console shows the stage's totals, which piece is next and what that piece is still short of.
  Each piece builds itself as soon as it is paid for; deliver everything and the stage goes up
  piece after piece.
- In creative mode, sneak + right-click empty-handed to skip the materials.
- When all eight stages are done, right-click to fire the generator (and again to bank it).

Stage 1 removes whatever stands on the site, chests included. Breaking the console abandons the
project; what is built stays. The generator itself cannot be selected or broken by hand: use
`/frostgen demolish` to take one down.

### Steam pipes

Eight steam ports stand on the rim of the foundation pad, two to a side, each facing outward. They
arrive with the Steam Core stage and go live when the generator is fired.

- Run **steam pipes** from a port. A pipe lies along the face you place it against and joins end to
  end with the next one.
- Use a **pipe fitting** for corners, tees and crossings: it joins on all six sides.
- Any pipe, fitting or port that is joined to a live port warms players near it (within about three
  blocks outdoors, or anywhere in the same room). Each one counts for a fifth of a firepit by default,
  and they add up, so a run of pipe along a wall heats a room.
- Look at a pipe to see whether it is carrying steam.

**Large ports and steam mains.** Four large ports, one to a side, come out of the hull through the
frames with their mouths over the first gallery, 2 m above its deck. Each has a valve whose spindle
drops to a handwheel at chest height on the gallery (the wheel is not operable yet). They arrive with the Thermal
Hull stage and go live with the rest.

- A **steam main** is two blocks across. Set one against the mouth of a large port, or the end of
  another main, and it lines up with it; mains only join when they meet squarely like that.
- A **steam main fitting** is a 2 x 2 x 2 block that joins mains on every side.
- An ordinary steam pipe can tap a main or a main fitting anywhere, or butt against the mouth of a
  large port, so a main can feed many small runs.
- A main gives three times the warmth of a pipe.
- A **steam main stand** carries a main fitting on a lattice post. Set it on the ground in line
  with a large port or the end of a main (within 48 blocks along the compass, on either of the
  two columns the main covers) and the fitting comes up at that pipe's height and lined up with
  it, whatever the ground does, up to 40 blocks tall. Run the main out to it and carry on level,
  or branch. Anywhere else the fitting stands 14 blocks up, the height of the large ports above
  the pad. Breaking the fitting leaves the post; the post drops nothing.

Recipes: two iron plates make four pipes; four iron plates make four fittings; eight iron plates
in a ring make four mains; four mains make two main fittings; a main fitting over four iron
ingots makes a stand.

A pipe is traced back to its port on demand, so the route has to be in loaded chunks. A run that
leaves the loaded world keeps the state it last had until the server restarts.

### Admin commands (`controlserver` privilege)

| Command | Does |
|---|---|
| `/frostgen place [pos] [stages] [lit]` | Builds a generator at once, centred on a position |
| `/frostgen check [pos] [stages] [lit]` | Counts cells that differ from the blueprint |
| `/frostgen demolish [pos]` | Removes a generator, leaving the open shaft |
| `/frostgen console [pos] [side]` | Puts a console down |
| `/frostgen start [pos]` | Waives the materials of the console's current stage |
| `/frostgen deliver <n> <amount> [pos]` | Credits requirement n of the current stage with materials |
| `/frostgen piece [pos]` | Puts up the next piece immediately |
| `/frostgen advance [pos]` | Finishes the console's current stage immediately |
| `/frostgen fire [pos] [on]` | Fires or banks a finished generator |
| `/frostgen status [pos]` | Reports a console's state |
| `/frostgen steam [pos]` | Says whether a pipe, fitting or port is carrying steam |
| `/frostgen stand [pos]` | Raises a steam main stand with its post starting there |

Console commands take the console's position, or the one you are looking at.

### Configuration

- `ModConfig/froststory.json` (server): `RequireMaterials`, `CostMultiplier`, `BuildSpeedMultiplier`,
  `PipeHeatStrength`.
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
  src/Steam/                            pipes, mains, fittings and ports; tracing a run back to a live port
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

Working and checked on a 1.22.7 dedicated server: assets load without errors, all eight stages
build through the console, partial deliveries raise exactly the pieces they pay for and then wait,
a build resumes mid-stage after a restart, firing and banking swap the lit models and the steam ports, and the
result matches the blueprint cell for cell. Pipes and mains laid from the ports report steam when
the generator is fired, taps off a main work, a misaligned main stays cold, and everything goes cold
downstream of a cut. Main stands come up level and in line with a port or another fitting, on
higher and lower ground. Every shape file loads cleanly through the game's own
shape class.

Not yet checked in a game client: how the large segment models render and light in-engine, walking
and climbing on the invisible collision cells, placing pipes and mains by hand (including a main
lining itself up with a port, and setting down a stand), how the two-block mains, the stand and
its post look in the game and the inventory, how much a pipe run actually
warms a player, the footprint highlight, the console's hover text and the stack's steam particles. The preview page mirrors the engine's rotation and texture
mapping rules, but it is not the engine.

Next up:

- radiators and other things for the steam pipes to feed
- fuel or upkeep for a running generator, and output levels
- proper intermediate parts instead of raw vanilla materials
- hand-painted textures to replace the procedural ones
