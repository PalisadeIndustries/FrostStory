---
name: frost-climate
description: Builds FrostStory's Long Winter climate - the world option and playstyle, permanent deep cold, snow, escalating whiteout storms and exposure (wind chill, hypothermia) for players outside shelter.
---
You own `src/Climate/` and the climate assets listed in CLAUDE.md. Read CLAUDE.md first.

Build:
- The `froststoryLongWinter` world config option and a **Long Winter** playstyle that turns it on
  with suitable vanilla settings (patch the game's worldconfig/playstyle assets; find them under
  `/opt/vsenv/vs/assets/`). Its other knobs: cold offset, storm frequency.
- Deep permanent cold: when the flag is on, lower temperature through the climate event
  (`api.Event.OnGetClimate`) on both sides, so snow falls instead of rain and the vanilla body
  temperature system makes the cold dangerous. Cold deepens slowly with days survived.
- Whiteout storms: a server-side storm cycle (calm, building, storm, easing) whose frequency,
  length and strength rise over the days; strength synced to clients; strong wind; heavy fog and
  snow on the client; chat warnings as one builds.
- Exposure: while a player is outdoors (not in a vanilla room, not near a heat source) during a
  storm, extra body heat loss scaled by storm strength and reduced by worn gear's summed
  `froststoryWindproof`; whiteout fog reduced by `froststoryStormVision`. A visible cue when
  hypothermic (e.g. slowed walk speed) and a clear message.
- `FrostClimateSystem.StormStrength(IWorldAccessor)` as the public read point.
- Admin commands under `/frostclimate` (status, storm start/stop, set day offset) for testing.
- Config `ModConfig/froststory-climate.json`. Docs in `docs/climate.md`.

Everything must be a no-op when the flag is off. Build with 0 errors and pass `tools/smoketest.sh`
(also boot once with the flag on: pass worldconfig via a test, or temporarily default it on).
