# The Long Winter: climate, storms and exposure

Everything on this page happens only in a world with the world option **Long Winter**
(`froststoryLongWinter`) turned on. Ordinary worlds play as before.

## Starting a Long Winter world

Pick the **The Long Winter** playstyle when you create a world. It turns the option on and sets:

| Setting | Value |
|---|---|
| Starting climate | Icy |
| Global temperature | Very cold |
| Seasons | Always winter |
| Precipitation | Humid (more snow) |
| Temporal storms | Very rare |
| Sapling growth | Slower |

Three world options sit in their own **The Long Winter** group and can be changed in any playstyle:

| Option | Code | Values |
|---|---|---|
| Long Winter | `froststoryLongWinter` | on / off (only when the world is created) |
| Long Winter cold | `froststoryColdOffset` | -5, -10 (default), -15, -20, -30 °C |
| Whiteout storms | `froststoryStormFrequency` | Off, Rare, Normal (default), Often, Relentless |

On a dedicated server, set `"PlayStyle": "froststory-longwinter"` under `WorldConfig` in
`serverconfig.json` before the world is first created.

## The cold

The world's cold offset is taken off the temperature everywhere, and the cold deepens by a
quarter of a degree for each day since the world began, up to 15 °C more. A storm takes up to
another 8 °C off while it blows. It is cold enough that snow falls instead of rain, and the game's
own body temperature makes it dangerous: furs, a fire and a closed room are what keep you alive.

## Whiteout storms

Storms go round a cycle:

1. **Calm.** Two days or so at first, getting shorter as the days pass (never under 8 hours).
   No storm comes in the first day and a half.
2. **Building** (2 hours). Everyone is warned in chat, with the wind's direction and how long is
   left. The wind rises and the light goes white.
3. **Storm.** A few hours at first, up to 16 hours late in the game. Strong wind, driving snow,
   white fog that closes in to a few metres. Storms hit harder the longer the world has run; a
   killing storm gets its own warning.
4. **Easing** (2 hours). The wind drops and the fog lifts.

Times are in-game hours.

## Exposure

Out in a storm you lose body heat on top of the ordinary cold, up to about 1.2 °C a minute in the
open in a full storm. You are sheltered, and lose nothing extra, when you are:

- inside a closed room (the game's own room check: walls, roof and a door that shuts), or
- near enough heat: a lit tent stove, a live steam pipe or two, a fire.

Under a roof but not in a closed room (a lean-to, a doorway) you lose half as much. Windproof
gear cuts the loss: each worn piece's windproof value adds up, to a total of 1 (no loss at all).
Storm goggles thin the whiteout fog the same way.

Below 35 °C body temperature you are **hypothermic**: you are told so, a red warning shows, and
you walk a quarter slower, up to half as slow as you get colder. Get to heat. You are told again
when you have warmed up.

## Admin commands

All need the `controlserver` privilege.

| Command | Does |
|---|---|
| `/frostclimate status` | Days survived, the cold now, storm phase, strength and time left, your body temperature, exposure and gear |
| `/frostclimate storm start [strength] [hours]` | Start a storm now (strength 0..1, default 1) |
| `/frostclimate storm stop` | End any storm and start a calm |
| `/frostclimate days <offset>` | Add days to the days survived, to try out late-game cold and storms |

## Server config

`ModConfig/froststory-climate.json` holds the tuning: how fast the cold deepens, calm and storm
lengths, how strength grows, wind, fog and snow strength, heat loss, how much heat counts as
shelter, and the hypothermia threshold and slow-down. Each field is commented in
`src/Climate/ClimateConfig.cs`.

## For the other areas

- `FrostStory.Climate.FrostClimateSystem.StormStrength(IWorldAccessor world)` gives the storm
  strength 0..1 on either side (0 when the Long Winter is off).
- `FrostClimateSystem.IsLongWinter(IWorldAccessor world)` reads the world option.
- Every player entity carries a `froststoryClimate` tree in its `WatchedAttributes`
  (`strength`, `days`, `phase`, `windAngle`, `cold`, `wind`), which is how clients learn the state.
- Gear attributes read: `froststoryWindproof` and `froststoryStormVision` (0..1 per worn piece,
  summed, capped at 1).
