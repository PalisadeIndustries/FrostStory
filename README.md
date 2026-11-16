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

**Pipe runs.** The main way to pipe steam is to string a run between two fittings or ports:

- Hold steam pipes and right-click a fitting or port, then right-click another. A curved tube is
  strung between them. It leaves each end square to one of its sides, so two fittings at different
  heights or angles get a smooth bend, and a port is always left by its outlet.
- A run costs one pipe per block of its length, taken from the stack in your hand. A small run can
  be up to 24 blocks long; put a fitting in between to go further.
- Hold steam mains instead to string a large run (up to 32 blocks) between main fittings and large
  ports. A small run can also start from a main fitting, as a tap.
- Clicking the same two ends again takes the run down and hands the pipes back; so does breaking
  a fitting. Clicking the first end again cancels.
- Each side of a fitting takes one run. A run has no collision and is not checked against what is
  in its way.
- The air a live run passes through is warm, much as a line of pipe blocks would be, so a run
  across a room heats it.
- Sneak while clicking to set a pipe block against the fitting instead.

**Heating pipes.** A heating pipe is a low pipe, one block wide, that lies on the ground:

- It joins whatever is beside it (another heating pipe, a pipe, a fitting or a port's outlet) and
  takes the shape to suit: straight, corner, tee or cross. No fittings needed.
- It is low enough to step over.
- Carrying steam it gives off twice the warmth of an ordinary pipe.
- Three iron plates in a row make six.

**Pipe blocks.** Rigid pipes still work and join the same network:

- Run **steam pipes** from a port. A pipe lies along the face you place it against and joins end to
  end with the next one.
- Use a **pipe fitting** for corners, tees and crossings: it joins on all six sides.
- Any pipe, fitting or port that is joined to a live port warms players near it (within about three
  blocks outdoors, or anywhere in the same room). Each one counts for a fifth of a firepit by default,
  and they add up, so a run of pipe along a wall heats a room.
- A **pipe wall passthrough** is a solid block of wall with a pipe mount on each of two opposite
  sides, so a pipe can go through a wall without leaving a gap in it. Set it in the wall while
  looking through the wall: one mount faces you and the other faces away (look steeply up or
  down to put it in a floor or roof). Each mount takes a pipe laid against it or one pipe run;
  its other four sides are plain wall, and it keeps a room closed like any solid block.
- A **radiator** is a cast-iron column radiator two blocks long, one high and half a block deep.
  Its two hookups are on its back, one behind each block, at a right angle to its length, so it
  stands along the wall its pipe comes through: put a wall passthrough behind either block. Each
  hookup takes a pipe or one pipe run, and steam passes from one to the other; a pipe can also
  feed it from underneath. Set against a wall it backs onto that wall; set on a floor it backs
  away from you. With steam in it, it gives five times the warmth of a pipe (the
  same as a firepit at the default setting).
- An **underfloor grate** is a floor block to walk on with a heating coil under its grating. It
  has a pipe mount underneath, for a pipe or one pipe run, and one in the middle of each side
  for a pipe. Grates laid side by side pass steam along, so one feed warms a whole floor. It gives
  three times the warmth of a pipe, and is solid for closing a room. Nothing joins on top.
- Look at a pipe to see whether it is carrying steam.

**Supply, draw, valves and gauges.** Steam comes in quantities. A live base port supplies 40
and a large port 250 (`PortSupply`, `MainPortSupply` in the config). Everything on a line draws
some: a pipe 0.25 a block, a main 0.5, a heating pipe 1, an underfloor grate 3, a radiator 6, a
steam hub 30; strung runs draw by their length. A line that asks for more than reaches it is
starved, and everything on the starved part gives proportionally less heat. Look at a pipe to
see if it is.

- A **stop valve** is a length of pipe that can be shut. Each click opens it a step and each
  sneak-click closes it a step, shut to fully open in eight. Part open it is a restrictor: each
  step lets 5 more through, so 3 of 8 passes 15 however much is drawn beyond it. A pointer over
  the handwheel lies along the pipe when open and across it when shut.
- A **steam gauge station** goes in a line of pipe (pipes join on three sides and underneath;
  the dial faces you). Its needle shows the steam that can reach that point, on a scale of 60.
  The green zone starts at what everything further along the line draws and runs a quarter
  above it, so it moves as you build; above that is red, more than is needed. With the needle
  below the green, the line beyond is starved. Close a valve upstream until the needle sits in
  the green and that branch takes no more than it needs.
- The sums assume lines branch out from their ports. A ring is followed one way round.
- Red costs nothing yet: there is no fuel or wear behind the steam. Large ports and mains have
  no valve or gauge of their own yet; a gauge on a pipe tapped off a main reads off the scale.

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
- A **steam distributor** splits a main into four ordinary pipes. The main goes in one end and
  four outlets come out of the other, one to each block of that end; set it against the end of a
  main or a fitting and the inlet lines up. Each outlet takes a pipe laid against it or one pipe
  run. Nothing joins on its other sides.
- A **steam drop distributor** is the same thing with the four outlets underneath: the main comes
  in at one side and the pipes leave downwards, so they can go down and away from a main carried
  overhead.
- A **steam hub** is a heater two blocks square and six high that hangs from a steam main, for
  warming open ground. Set it under a main fitting, or under a main coming down from one; it
  takes a main on top and nothing else. With steam in it, it warms the ground beneath for 12
  blocks round (up to 30 blocks below it), most strongly close by and falling to 40% at the
  edge; the warmth follows the lie of the ground and catches up with digging and building within
  a minute. `HubHeatStrength` and `HubRadius` in the config change it.
- A **steam hub tower** raises the whole thing. Set it on the ground and it builds four legs,
  four blocks square, that go straight up and then lean in over the last four blocks to carry a
  main fitting. The hub hangs inside the frame on a main dropped from the fitting, with its
  underside three blocks above the ground, so it can be walked under. Only the four legs are solid, so you can walk under it. It lines the fitting up with a
  large port or the end of a main exactly as a main stand does, and otherwise stands it 14
  blocks up; it needs at least ten blocks of height. The frame drops nothing when broken.
- A **steam main stand** carries a main fitting on a lattice post. Set it on the ground in line
  with a large port or the end of a main (within 48 blocks along the compass, on either of the
  two columns the main covers) and the fitting comes up at that pipe's height and lined up with
  it, whatever the ground does, up to 40 blocks tall. Run the main out to it and carry on level,
  or branch. Anywhere else the fitting stands 14 blocks up, the height of the large ports above
  the pad. Breaking the fitting leaves the post; the post drops nothing.

Recipes: two iron plates make four pipes; four iron plates make four fittings; eight iron plates
in a ring make four mains; four mains make two main fittings; a main fitting over four iron
ingots makes a stand; a pipe with an iron plate on each of its four sides makes two wall passthroughs; three iron plates over pipe, plate, pipe make a radiator; two gratings over two pipes over two iron plates make two underfloor grates; six iron plates over a main fitting make a steam hub; a steam hub over two columns of two iron ingots makes a hub tower; an iron plate over pipe, plate, pipe makes a stop valve, and a glass block over the same makes a gauge station; a main with four pipes makes a distributor (main and two pipes over two
pipes) or a drop distributor (main over two rows of two pipes).

A pipe is traced back to its port on demand, so the route has to be in loaded chunks. A run that
leaves the loaded world keeps the state it last had until the server restarts.

### Tents

Tent rods and tent cloth make simple shelters.

- A **tent rod** is a pole one block long that lies along the face you set it against. Rods end to
  end make uprights, ridge poles and ground rails.
- With **tent cloth** in hand, click a rod, then another rod or the edge of any block. A panel is
  stretched between them, as wide as the whole pole the rod is part of. It sags a little and stirs
  in the wind; the two edges it hangs from stay still.
- Hold the sprint key on the second click for a **triangle**: the cloth is drawn to a point, the
  nearest end of the pole or corner of the block you clicked, instead of along an edge. Two
  triangles off an end upright, one out to each ground rail, close a gable. A triangle and a full
  panel between the same two poles are separate sheets.
- A panel can span 12 blocks and be 16 wide, and takes one cloth per block of area. The same two
  clicks take it down and hand the cloth back; breaking a rod at either end does too.
- Cloth has no collision, but it keeps rain out and counts as a wall when the game decides whether
  a space is an enclosed room, so a closed tent holds warmth. A heating pipe through it does the rest.
- The first click has to be on a rod. A block edge is one block long, so for a wide wall down to
  the ground, lay a rail of rods along it.
- Two sticks make two rods; two linen make four cloth.

**Tent stove.** A small pot-bellied stove that needs no steam, for a tent or a hut before the
pipes reach it.

- Put fuel in by hand (anything a firepit burns), one piece a click, then light it the way you
  light a firepit. Fuel lasts three times as long as in a firepit and the stove holds about half
  an hour of it. Look at it to see how long is left.
- It only draws with a **stove flue** standing on it that comes out under open sky. Stack
  lengths straight up through the cloth, or put a **flue elbow** on and carry on sideways out
  through a wall, where a **flue upturn** turns it back up to take a cap; the flue closes the hole it makes. A **flue cap** on the top keeps the rain
  out. If the end is under cover the fire goes out.
- Lit, it gives the warmth of a firepit and a little light.
- An iron plate over two iron plates over three iron ingots makes a stove; four iron plates in
  two columns make eight flues; an iron plate over a flue makes a cap; three flues in an L make
  two elbows; an elbow on its own in the grid becomes an upturn, and back. `StoveHeatStrength` and `StoveFuelEfficiency` in the config
  change the warmth and how long fuel lasts.

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
| `/frostgen valve <pos> <0-8>` | Sets a stop valve |
| `/frostgen stove <pos> [seconds]` | Reports on a tent stove; with a number, adds that much fuel and lights it |
| `/frostgen stand [pos] [hub]` | Raises a steam main stand with its post starting there; `true` raises a steam hub tower |
| `/frostgen run <from> <to> [main]` | Strings a pipe run between two fittings or ports, or takes it down |
| `/frostgen cloth <rod> <rod> [triangle]` | Stretches a tent cloth panel between two rods, or takes it down |

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
  src/Steam/                            pipe runs, pipes, mains, fittings and ports; tracing back to a live port
  src/Tent/                             tent rods and stretched cloth panels
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
textures, stage by stage and piece by piece, with a section view of the shaft. A "Pipework
example" layer adds a sample of what a player can build off the ports (mains, stands, fittings,
pipes), laid out in `pipework_example()` in `tools/build_preview.py`. (`pip install pillow` for the Python tools.)

## Status

Working and checked on a 1.22.7 dedicated server: assets load without errors, all eight stages
build through the console, partial deliveries raise exactly the pieces they pay for and then wait,
a build resumes mid-stage after a restart, firing and banking swap the lit models and the steam ports, and the
result matches the blueprint cell for cell. Pipes and mains laid from the ports report steam when
the generator is fired, taps off a main work, a misaligned main stays cold, and everything goes cold
downstream of a cut. Pipe runs can be strung and taken down between fittings and ports, carry steam,
warm the air they cross, keep through banking, firing and a restart, and respect the length and
size limits. Heating pipes take the right shape as they are laid, branched and cut, and carry
steam. Tent panels go up between rods, come down, and follow a rod that is removed. Main stands come up level and in line with a port or another fitting, on
higher and lower ground. Both distributors carry steam from a main to a pipe on each of their four
outlets and to four pipe runs (a fifth is refused), and stay shut to pipes and mains on every other side. A wall passthrough carries steam between its two mounts by pipe or by run,
stays shut on its other four sides, and counts as a solid, heat-keeping block. A radiator takes steam at the two hookups on its back and from
underneath, by pipe or by run, passes it on, and stays shut on its front, ends and top. A tent stove lights with fuel and a flue to open sky (straight up with a cap, round an elbow and out sideways, or on round an upturn to a cap outside), refuses without a flue or with the flue top under
cover, burns its fuel down and goes out, and goes out when its flue is removed. A stop valve shuts a line, and part open passes only its share so that what is beyond is fed in proportion; a gauge reads the supply reaching it and the draw beyond it, and both follow a valve being turned and things being added. An overloaded port starves its whole line. A hub tower comes up with its legs, tapered head, fitting, drop main and hub in place, the hub three blocks off the ground and live. A steam hub takes a main on top, refuses pipes, lays warm air over uneven ground out to its radius that is warm
only while the hub has steam and weaker further out, and takes it up again when broken. Underfloor grates take steam from underneath or the side, pass it
along a row, take one run underneath, and stay shut on top. Every shape file loads cleanly through the game's own
shape class.

Not yet checked in a game client: turning a stop valve by hand, its pointer, and the gauge's needle and coloured marks (drawn into the chunk mesh; if they are wrong the reading is still in the hover text); fuelling and lighting the tent stove by hand, its smoke and glow; whether tent cloth draws and moves in the wind (it uses the same
chunk-mesh approach as pipe runs, with the engine's wind flags on the cloth), whether a closed tent
really registers as a room, how the heating pipe's arms line up, and whether pipe runs draw at all, and correctly (they are built into
the chunk mesh from short straight pieces using the game's tesselator, which I could only check
against the engine's code and in the preview), and the two-click way of stringing them. Also: how the large segment models render and light in-engine, walking
and climbing on the invisible collision cells, placing pipes and mains by hand (including a main
lining itself up with a port, a distributor turning its inlet to what it is set against, a wall passthrough turning to the way you look, a radiator backing onto the wall it is set against, and setting down a stand), how the two-block mains, the stand and
its post look in the game and the inventory, how much a pipe run actually
warms a player, the footprint highlight, the console's hover text and the stack's steam particles. The preview page mirrors the engine's rotation and texture
mapping rules, but it is not the engine.

Next up:

- radiators and other things for the steam pipes to feed
- fuel or upkeep for a running generator, and output levels
- proper intermediate parts instead of raw vanilla materials
- hand-painted textures to replace the procedural ones
