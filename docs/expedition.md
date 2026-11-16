### The expedition start

Only in a **Long Winter** world (world config `froststoryLongWinter`); in any other world none of
this happens and the class below is not offered.

**The wreck by the spawn.** When a Long Winter world is created, the freighter is set down frozen
into the ice near the world spawn, once per world:

- The ground within 48 blocks of the spawn is loaded first, and she goes on the flattest dry site
  there (a little water is allowed: she is frozen in). Her ice sheet comes up level with the ground.
- Where the ground falls away under the hull or the ice sheet, it is packed with ice down to the
  ground, at most 12 blocks below the keel, so she does not hang in the air.
- The world spawn moves onto the ice two blocks off the hole stove in her port side, which opens
  into the No. 2 'tween deck. Survivors already in the world when she goes down are moved there.
- She is remembered in the save. If the server stops before the ground is loaded she is set down at
  the next start.

**Expedition survivor.** A character class offered in Long Winter worlds:

- **Cold-seasoned**: loses body heat to the cold about a third more slowly.
- **Hardy** (+10% mining speed, +5 health) and **Ravenous** (+30% hunger: cold work burns food).
- Starts dressed in the fur set: hood, parka, trousers, mittens, boots and snow goggles.

**The starting kit.** Every player gets it once, the first time they join a Long Winter world
(after choosing their character), whatever their class. Clothing goes on where its slot is free,
and furs the class already put on are not given twice.

| | |
|---|---|
| Iron tools | felling axe, pickaxe, knife, shovel, hammer |
| Ice tools | ice pick, ice saw |
| Furs | hood, parka, trousers, mittens, boots, snow goggles |
| Fire | firestarter, 32 firewood |
| Rations | 12 cured meat, 8 spelt bread |
| Shelter | 16 tent rods, 40 tent cloth |
| Stove | tent stove, 4 flue lengths, a flue elbow, a flue upturn, a flue cap |

The list is `assets/froststory/config/expeditionkit.json`, an ordinary asset other mods can patch.
A code in it that does not exist is left out with a warning in the server log.

**The first day.** A new survivor gets a few short hints in chat over the first few minutes:
shelter, the stove, salvaging the wreck, and the generator. The handbook has a **Long Winter**
guide page for the whole loop.

**Admin commands** (`controlserver` privilege):

| Command | Does |
|---|---|
| `/frostexp kit [player]` | Gives the starting kit to a player (or you), whether or not they have had it |
| `/frostexp resetkit [player]` | Forgets that a player has had the kit, so it is given again on their next join |
| `/frostexp wreck [pos]` | Sets the start wreck down: at a position on the ice amidships, or with no position on the best site near the world spawn; moves the world spawn beside her hole |
| `/frostexp status` | Reports the Long Winter flag, the wreck, the spawn and the kit |

**Configuration** (`ModConfig/froststory-expedition.json`, server):

| Setting | Default | |
|---|---|---|
| `Kit` | true | give the starting kit |
| `Wreck` | true | set the wreck down when a Long Winter world is created |
| `WreckDistance` | 48 | how far from the world spawn the middle of the wreck may lie (8 to 256) |
| `WreckFillDepth` | 12 | how deep ice is packed under her where the ground falls away (0 for none) |
| `Guidance` | true | the first day's hints |
| `ColdSeasonedKeep` | 0.35 | the share of body heat lost to the cold that a cold-seasoned survivor keeps (0 to 0.9) |

Code: `src/Expedition/`; assets: `patches/expedition-classes.json` (class and trait, applied only
when `froststoryLongWinter` is true), `config/expeditionkit.json`,
`config/handbook/expedition-longwinter.json`, and the `//expedition` block of `lang/en.json`.
