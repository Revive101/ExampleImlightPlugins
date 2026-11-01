# BloodMoonPlugin

An example plugin for the Imlight Plugin API that runs a recurring server-wide event. While the Blood Moon is up, creatures get more health, fight smarter, notice players from further away, and pay bonus gold when defeated.

This plugin builds on [ExamplePlugin](../ExamplePlugin) and shows how to work with creatures and duels:

- **Changing creatures while the server runs.** It changes every creature in the world, catches new spawns, and puts everything back afterwards.
- **Following a duel from start to finish.** It tracks which creatures are in a fight and rewards the winners.
- **Running something on a schedule.** A timer starts and ends the event without anyone using a command.

## How to use

| Command | Who can use it | What it does |
| --- | --- | --- |
| `.bloodmoon` or `.bm` | Everyone | Shows when the current moon sets or the next one rises. |
| `.startbloodmoon` | Administrators | Starts a Blood Moon now. |
| `.stopbloodmoon` | Administrators | Ends the Blood Moon now. |

The first moon rises one interval after the server starts. The timer restarts when a moon starts or ends, including from a command.

The moon only affects creatures in shared zones. Dungeons and other instances, bosses, summoned minions and fights already running when it rises are left alone.

## Configuration

The plugin writes `config.json` to its data folder on first run. Changes take effect on the next restart. If a value is invalid, the plugin logs an error and doesn't enable.

| Setting | Default | What it does |
| --- | --- | --- |
| `IntervalMinutes` | `180` | The time between moons. |
| `DurationMinutes` | `20` | How long a moon lasts. |
| `HealthMultiplier` | `1.5` | Multiplies each creature's max health. |
| `ProximityMultiplier` | `2` | Multiplies how close a player must come to start a fight. Creatures with a proximity of 0 stay at 0. |
| `IntelligenceBonus` | `0.25` | Added to the chance a creature picks its strongest spell, up to 1. |
| `AggressivenessBonus` | `0.25` | Added to the chance a creature attacks rather than prepares, up to 1. |
| `GoldPerCreatureLevel` | `5` | Bonus gold per level of each empowered creature in a duel. Each winner gets the full amount. |

## What it shows

### Changing creatures
- **Covering every creature:** `Rise()` goes through `Zone.Creatures` for creatures already spawned, and `OnCreatureSpawn` catches the ones that spawn later.
- **Delayed writes:** creature setters are applied by the creature's own actor after they return. `Empower()` reads each original value and works out the new one before changing anything.
- **Health and max health:** `SetMaxHealth` doesn't heal, so `Empower()` calls `SetHealth` straight after. The writes are applied in the order they were sent.
- **Putting things back:** the original stats are stored by `GlobalId`. Creatures that are mid-fight when the moon sets keep their stats, because lowering their max health would take health away in the middle of a duel. They're forgotten when `OnCreatureDespawn` fires.

### Following a duel
- **Bonus gold, not doubled gold:** the API can't change the rewards a duel gives, so this is paid on top. `OnDuelEnd` fires before the game gives its own rewards.
- **Tracking participants:** `OnDuelStart` gives the starting creatures, and `OnDuelistJoin` gives any that join later. Defeated creatures have left `IDuel.Creatures` by the time `OnDuelEnd` fires, so the bounty is added up as creatures arrive.
- **Sigils are reused:** `IDuel.Id` is the sigil's ID, so `OnDuelStart` clears any bounty left over from an earlier fight there that nobody won.

### Thread safety
Commands, the timer and every creature's actor can reach the moon's state at the same time, so it's all guarded by one `lock`. An exception on a timer thread would crash the server, so `OnTimer()` catches everything.

## Building

```
dotnet build
```

The compiled plugin will be at `bin/Debug/net10.0/BloodMoonPlugin.dll`. This file can be dropped into the `plugins` folder in the same directory as `Imlight.Director.exe`. If this folder doesn't exist, you can create it.
