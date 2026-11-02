# StarterKitPlugin

An example plugin for the Imlight Plugin API that lets each character claim a one-time starter kit for their school.

This plugin builds on [ExamplePlugin](../ExamplePlugin) and shows how to work with game templates:

- **Looking up items and spells.** `Server.FindItems` and `Server.GetSpell` turn the internal names in the game files into template IDs and details such as English names and pip costs.
- **Giving items and spells.** The kit goes to the player with `GiveItem`, `AddGold` and `Learn`.

## How to use

| Command | What it does |
| --- | --- |
| `.starterkit` (or `.kit`) | Gives you the kit for your school. Each character can do this once. |

| Part of the kit | Internal name |
| --- | --- |
| Hat | `Crown-Starter-Hat-<School>-01` |
| Robe | `Crown-Starter-Robe-<School>-01` |
| Boots | `Crown-Starter-Shoes-<School>-01` |
| Wand | `Crown-Starter-Wand-<School>-01` |
| Deck | `Deck-T1-001` |
| Gold | 500 |

`<School>` is Fire, Ice, Storm, Myth, Life, Death or Balance. None of these items have a level or school requirement, so a new wizard can wear them straight away.

The kit also teaches the first two spells of the wizard's school:

| School | Spells |
| --- | --- |
| Fire | Fire Cat, Fireblade |
| Ice | Frost Beetle, Freeze |
| Storm | Thunder Snake, Lightning Strike |
| Myth | Blood Bat, Golem Minion |
| Life | Imp, Minor Blessing |
| Death | Dark Sprite, Death Trap |
| Balance | Scarab, Elemental Shield |

Items go into the backpack, then the bank if the backpack is full. If there's no room in either, the item is lost and the player is told, and the kit can't be claimed again.

## What it shows

### Looking up templates
- **Items by internal name:** `Server.FindItems` matches the name of the item's template file, ignoring case. It returns a list, as several items can share a name, and the list is empty if there are none.
- **Spells by internal name:** `Server.GetSpell` must match the name exactly, including its case. Spell names don't follow a pattern, and the internal name isn't always the English one (`Minion Myth 000` is Golem Minion), so the plugin keeps them in a table.
- **Teaching spells:** `Learn` takes the template ID from `SpellInfo` and returns false if the wizard already knows the spell. The plugin uses that to tell the player only about the spells that are new to them.
- **English names:** `ItemInfo.Name` and `SpellInfo.DisplayName` are what the player sees. Some names have the game's line break marker in them as plain text, such as `Starter Ice\nConical`, so the plugin replaces it before showing them in chat.
- **When to look things up:** the first lookup of an item reads it from the game files. `OnEnable()` looks up every kit once and keeps the results, so the command stays fast. Commands share one dispatcher with the whole server, so a slow one holds up everybody.
- **Failing early:** if a name isn't in the game files, `OnEnable()` throws and the plugin stays off. The error shows up in the server log at startup, not when a player claims a kit.

### Finding names
Internal names come from the game files, which Imlight keeps in the LiteDB file set by `LocalWadCachePath`. Item templates are in `Root.wad` under `ObjectData/`, and spells are under `Spells/`. A wand's spell is listed in its template as a `ProvideSpellEffectInfo` in `m_equipEffects`.

### Claiming once
- **Saving before giving:** the claim is written to `claimed.json` in the plugin's data folder before any item is given. If something goes wrong partway through, the player can't claim the kit a second time.
- **Thread safety:** only the command touches the claim list and commands all run on one dispatcher, so unlike [SetHomePlugin](../SetHomePlugin) it doesn't need a `lock`.

## Building

```
dotnet build
```

The compiled plugin will be at `bin/Debug/net10.0/StarterKitPlugin.dll`. This file can be dropped into the `plugins` folder in the same directory as `Imlight.Director.exe`. If this folder doesn't exist, you can create it.
