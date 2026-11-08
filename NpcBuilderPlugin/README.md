# NpcBuilderPlugin

An example plugin for the Imlight Plugin API that lets admins place NPCs where they stand, give them lines to say, and set the icon over their head and their animation.

This plugin builds on [SetHomePlugin](../SetHomePlugin) and shows the NPC API:

- **Spawning NPCs.** `Npcs.Spawn` puts an NPC built from any game object template into a zone, with an optional custom name.
- **Talking to players.** `SetInteraction` makes an NPC interactable, and `NpcInteraction.Say` shows the player a message box headed with the NPC's name.
- **Wizbangs and states.** `SetWizBang` sets the icon over its head and `SetState` plays one of its template's animation states.
- **Keeping NPCs in a zone.** NPCs disappear when their zone unloads, so the plugin saves them and spawns them again in `OnZoneLoad`.

## How to use

Every command needs the Administrator account level.

| Command | What it does |
| --- | --- |
| `.npc spawn <preset\|templateId> [name]` | Places an NPC where you stand, facing your camera. The name is optional and is used in its message boxes. The nametag over its head still shows the template's name. |
| `.npc list` | Lists every plugin NPC in your zone, nearest first, with its id. |
| `.npc say <id> <text>` | Adds a line. `{player}` is replaced by the name of the player talking to it. |
| `.npc lines <id>` | Shows an NPC's lines. |
| `.npc clearlines <id>` | Removes all of an NPC's lines, so players can't interact with it. |
| `.npc wizbang <id> <icon>` | Sets the icon over its head, such as `StartQuest`, `Shopping` or `NumberThree`. |
| `.npc state <id> <state>` | Puts it in an animation state, such as `Idle`, `Emoting` or `Dead`. `Off` hides it. |
| `.npc move <id>` | Moves an NPC to where you stand, even from another zone. |
| `.npc remove <id>` | Removes an NPC for good. |
| `.npc reload` | Reads `npcs.json` again and respawns everything, after you edit it by hand. |
| `.npc presets` | Lists the preset names. |

For example, to put a quest giver in the Commons:

```
.npc spawn ambrose Headmaster Ambrose
.npc say 1 Welcome to the server, {player}!
.npc say 1 Talk to me again for some advice.
.npc wizbang 1 StartQuest
```

Each player hears the lines in order, one each time they interact, and then starts from the first again.

### Presets

| Preset | Template ID | NPC |
| --- | --- | --- |
| `ambrose` | 38168 | Merle Ambrose |
| `gamma` | 38898 | Gamma |
| `lydia` | 38172 | Lydia Greyrose |
| `moolinda` | 38204 | Moolinda Wu |
| `cyrus` | 38206 | Cyrus Drake |
| `lincoln` | 38212 | Mr. Lincoln |
| `harold` | 38214 | Harold Argleston |
| `stillson` | 38216 | Private Stillson |
| `mindy` | 38170 | Mindy Pixiecrown |
| `malorn` | 81102 | Malorn Ashthorn |
| `dworgyn` | 82209 | Dworgyn |

Any other template ID works too. `npc.manifest` lists them with their names.

## What it shows

### Spawning
- **Templates:** `Npcs.Spawn` takes a game object template ID. The template only sets how the NPC looks: a creature's template gives an NPC that looks like that creature, but it never fights. A template with nothing to show, such as a sound or a trigger, throws `ArgumentException`. The plugin catches that and saves nothing.
- **Names:** the optional name is the title of the NPC's message boxes and its `INpc.Name`, which falls back to the template's name. It doesn't change the nametag over the NPC's head, which always shows the template's name. A Merle Ambrose renamed "Headmaster Ambrose" still has "Merle Ambrose" over his head.
- **Setting things up straight away:** `SetWizBang`, `SetState` and `SetInteraction` can all be called right after `Spawn`, before the NPC appears in the zone. It picks them up when it does.

### Interactions
- **Making an NPC interactable:** `SetInteraction` with a handler makes players near the NPC see the option to interact. Passing `null` takes it away. The plugin only sets a handler once an NPC has a line, so players are never offered an empty conversation.
- **Saying something:** `NpcInteraction.Say` shows the player a message box headed with the NPC's name. `NpcInteraction.Player` is the player who interacted, which lets the plugin keep each player's place in the conversation.
- **Threads:** the handler runs on the NPC's own actor, not the command dispatcher. Commands, zone loads and every NPC's handler can run at the same time, so the plugin locks everything it shares.

### Wizbangs and states
- **Wizbangs:** `SetWizBang` sets the icon from `NpcWizBang`, including the numbers zero to fifteen. Players nearby see the change within a second. The icon only shows while the NPC is interactable, so it is hidden until the NPC has a line.
- **States:** `SetState` plays a state from the template's state set. Most NPC templates use `NPCMobileStates`, which has `Idle`, `Emoting`, `Interacting`, `Dead` and a few more. Players who arrive later see it in the same state. `Off` hides the NPC without removing it.

### Keeping NPCs around
- **Zones unload:** an NPC lasts until it is removed or its zone unloads, and nothing about it is saved. The plugin keeps every NPC in `npcs.json` in its data folder and spawns them again in an `OnZoneLoad` handler. `INpc.IsRemoved` turns true when the zone unloads, which is how the plugin knows to spawn it again.
- **Zones already loaded:** `OnEnable` also goes through `Server.Zones`, for zones that loaded before the handler was added.
- **Moving:** an NPC never moves, so `.npc move` removes it and spawns a new one at the new spot.
- **Instances:** NPCs are only placed in shared zones. Instances are created per group and have no name to spawn back into.

### Finding NPCs
- **`IZone.Npcs`** has every plugin's NPCs in a zone. `.npc list` uses it, matches the ones that belong to this plugin by `GlobalId`, and marks the rest as another plugin's.
- **`Npcs.Spawned`** has only this plugin's NPCs, across every zone.
- **`Npcs.RemoveAll`** removes only this plugin's NPCs. `.npc reload` uses it to clear everything before spawning again from the file.

### Finding template IDs
`npc.manifest` lists NPC template IDs with their names. The same name often appears several times, for different versions of an NPC across quests. Templates are in `Root.wad` under `ObjectData/`, and an NPC's states are listed in the state set its `BasicObjectStateBehavior` names, under `StateData/`.

## Building

```
dotnet build
```

The compiled plugin will be at `bin/Debug/net10.0/NpcBuilderPlugin.dll`. This file can be dropped into the `plugins` folder in the same directory as `Imlight.Director.exe`. If this folder doesn't exist, you can create it.
