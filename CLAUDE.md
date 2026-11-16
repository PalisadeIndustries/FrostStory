# FrostStory: notes for agents

Vintage Story 1.22 code mod (.NET 10). Read README.md for what already exists: the geothermal
generator, steam heating, coal, the wrecked freighter, tents and the tent stove, building panels.

## Build and test

```bash
eval "$(tools/setup_env.sh)"                    # once per container: .NET 10 + VS server in /opt/vsenv
dotnet build FrostStory/FrostStory.csproj -c Release   # writes dist/froststory and dist/froststory.zip
tools/smoketest.sh                              # boots a headless server with the mod, lists errors
```

A change is not done until it builds with 0 errors and `tools/smoketest.sh` exits 0. Vanilla
assets for reference (codes, shapes, patches, JSON layouts) are in `/opt/vsenv/vs/assets/`, and the
API docs are `/opt/vsenv/vs/VintagestoryAPI.xml`; decompile nothing, check codes against those files
rather than memory. Vanilla JSON is JSON5 (comments, unquoted keys), so read it as text.

## The Long Winter (the start-of-game loop)

A world created with the **Long Winter** playstyle (world config bool `froststoryLongWinter`) is a
frozen hellscape: deep permanent cold, snow instead of rain, and whiteout storms that come more
often and bite harder as the days go on. The player starts as an expedition survivor with iron
tools and heavy furs beside the wreck, frozen in the ice:

1. survive the first nights in a tent with the tent stove, burning wood and the wreck's coal;
2. salvage the wreck for iron plates, ingots and coal;
3. raise the generator, fire it and pipe steam to a heated camp;
4. keep it fed and keep expanding the heat as the storms worsen.

Everything for the Long Winter does nothing in a world without that flag, so ordinary worlds play
as before.

## Areas and ownership

| Area | Code | Assets | Owner agent |
|---|---|---|---|
| Climate, storms, exposure | `src/Climate/` | `patches/climate*.json`, `worldproperties`, `config/climate*` | frost-climate |
| Expedition start | `src/Expedition/` | `patches/expedition*.json`, `config/expedition*` | frost-expedition |
| Fur gear and ice tools | `src/Gear/` | `itemtypes/gear/`, `recipes/grid/gear.json`, `textures/item/gear/`, `shapes/item/gear/` | frost-outfitter |

Rules that keep parallel work mergeable:

- Each area adds its **own** `ModSystem` (VS finds every ModSystem in the assembly) and its own
  server config file `ModConfig/froststory-<area>.json`. Don't edit `FrostStoryModSystem.cs`,
  the csproj, `modinfo.json` or README.md; write player-facing docs for your area in
  `docs/<area>.md` and the lead folds them into README.
- `lang/en.json` is shared: add your keys as one block at the end, after a
  `"//<area>": "-----"` separator key.
- Cross-area contracts (keep these exact):
  - world config bool `froststoryLongWinter` (`api.World.Config.GetBool("froststoryLongWinter")`);
    climate defines the option and the playstyle, the others read it.
  - gear item codes: `froststory:furhood`, `froststory:furparka`, `froststory:furtrousers`,
    `froststory:furmittens`, `froststory:furboots`, `froststory:snowgoggles`,
    `froststory:icepick-iron`, `froststory:icesaw-iron`.
  - gear attributes read by climate: `froststoryWindproof` (0..1 per worn piece, summed and
    capped at 1, cuts storm wind chill) and `froststoryStormVision` (0..1, cuts whiteout fog).
  - storm state: climate stores the current storm strength (0..1) in the world's
    `WatchedAttributes`-style shared data so others may read it via
    `FrostStory.Climate.FrostClimateSystem.StormStrength(IWorldAccessor)`.
- House style for text: plain, concrete, metric; original names only (the genre is the
  frozen-city builder, but nothing is copied from any existing game).
