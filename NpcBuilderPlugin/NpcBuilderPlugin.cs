using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Imlight.PluginApi;
using Imlight.PluginApi.Commands;
using Imlight.PluginApi.Events;
using Imlight.PluginApi.Npcs;

namespace NpcBuilderPlugin;

/// <summary>
/// Lets admins place NPCs where they stand with .npc, then give them lines to say, an icon over their head and an
/// animation state.
/// </summary>
/// <remarks>
/// A spawned NPC only lasts until its zone unloads, so every placement is kept in npcs.json in the plugin's data
/// folder and spawned again each time its zone loads. NPCs are only placed in shared zones, as instances are created
/// per group and have no name to come back to.
/// </remarks>
public sealed class NpcBuilderPlugin : ImlightPlugin {

    // A placed NPC, as saved. Vector3 stores its coordinates as fields, which System.Text.Json skips, so the location
    // is saved as plain numbers.
    private sealed class Placement {
        public string Zone { get; set; } = "";
        public ulong TemplateId { get; set; }
        public string? Name { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Orientation { get; set; }
        public List<string> Lines { get; set; } = [];
        public NpcWizBang WizBang { get; set; }
        public string? State { get; set; }

        [JsonIgnore]
        public Vector3 Location {
            get => new(X, Y, Z);
            set => (X, Y, Z) = (value.X, value.Y, value.Z);
        }
    }

    // Short names for some Wizard City NPC templates, so admins don't have to look up IDs. Any template ID from
    // npc.manifest works too, as long as the template has something to show.
    private static readonly Dictionary<string, ulong> Presets = new(StringComparer.OrdinalIgnoreCase) {
        ["ambrose"] = 38168,
        ["gamma"] = 38898,
        ["lydia"] = 38172,
        ["moolinda"] = 38204,
        ["cyrus"] = 38206,
        ["lincoln"] = 38212,
        ["harold"] = 38214,
        ["stillson"] = 38216,
        ["mindy"] = 38170,
        ["malorn"] = 81102,
        ["dworgyn"] = 82209,
    };

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        // Saves wizbangs by name, so npcs.json is easy to edit by hand.
        Converters = { new JsonStringEnumConverter() },
    };

    // Commands run on the command dispatcher, OnZoneLoad on the zone, and interactions on each NPC's own actor, so
    // everything below is locked.
    private readonly object gate = new();

    // Keyed by placement id, the number admins use in commands.
    private Dictionary<int, Placement> placements = [];

    // The NPC currently spawned for each placement. One that is IsRemoved belongs to a zone that has unloaded.
    private readonly Dictionary<int, INpc> live = [];

    // The next line each player hears from each NPC. Kept in memory only, so everyone starts over after a restart.
    private readonly Dictionary<(int Id, ulong CharacterId), int> progress = [];

    private string PlacementsPath => Path.Combine(DataFolder, "npcs.json");

    public override string Name => "NpcBuilderPlugin";

    public override string Version => "1.0.0";

    public override void OnEnable() {
        Load();

        // A zone loads again after it unloads, and its NPCs go with it, so this is where they come back.
        Events.Subscribe<OnZoneLoad>(e => SpawnInto(e.Zone));

        // Zones that loaded before the handler above was added. Spawn skips placements that are already up, so a
        // zone that also fires OnZoneLoad in between doesn't get them twice.
        foreach (IZone zone in Server.Zones) {
            SpawnInto(zone);
        }

        Commands.Register("npc", HandleCommand, CommandPermission.Administrator);
        Logger.Information($"Loaded {placements.Count} placed NPC(s).");
    }

    private void SpawnInto(IZone zone) {
        if (zone.IsInstance) {
            return;
        }

        lock (gate) {
            foreach ((int id, Placement placement) in placements) {
                if (!string.Equals(placement.Zone, zone.Name, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                try {
                    Spawn(id, placement, zone);
                } catch (ArgumentException ex) {
                    // npcs.json can be edited by hand, so a bad template is logged instead of stopping the others.
                    Logger.Warning($"NPC #{id} in {zone.Name} wasn't spawned: {ex.Message}");
                } catch (InvalidOperationException) {
                    // The zone unloaded again already. Its next OnZoneLoad tries again.
                }
            }
        }
    }

    /// <summary>
    /// Spawns the placement's NPC in the zone and applies its lines, wizbang and state. Does nothing if it is already
    /// spawned. Callers hold the gate.
    /// </summary>
    /// <exception cref="ArgumentException">The template is not one that can be shown.</exception>
    /// <exception cref="InvalidOperationException">The zone has unloaded.</exception>
    private INpc Spawn(int id, Placement placement, IZone zone) {
        if (LiveNpc(id) is { } existing) {
            return existing;
        }

        INpc npc = Npcs.Spawn(zone, placement.TemplateId, placement.Location, placement.Orientation, placement.Name);
        live[id] = npc;

        // These can all be set straight after Spawn: the NPC keeps them until it appears in the zone.
        npc.SetWizBang(placement.WizBang);
        if (placement.State is not null) {
            npc.SetState(placement.State);
        }
        UpdateInteraction(id, placement, npc);
        return npc;
    }

    // An NPC with nothing to say isn't interactable, so players aren't offered an empty conversation. That also hides
    // its wizbang, which only shows while the NPC is interactable.
    private void UpdateInteraction(int id, Placement placement, INpc npc)
        => npc.SetInteraction(placement.Lines.Count > 0 ? i => Talk(id, i) : null);

    // Runs on the NPC's own actor each time a player interacts with it. Each player hears the lines in order, one per
    // interaction, starting again after the last.
    private void Talk(int id, NpcInteraction interaction) {
        string line;
        lock (gate) {
            if (!placements.TryGetValue(id, out Placement? placement) || placement.Lines.Count == 0) {
                return;
            }
            (int, ulong) key = (id, interaction.Player.CharacterId);
            int index = progress.GetValueOrDefault(key) % placement.Lines.Count;
            progress[key] = (index + 1) % placement.Lines.Count;
            line = placement.Lines[index];
        }

        // Say shows a message box headed with the NPC's name, so a custom name shows up here too.
        interaction.Say(line.Replace("{player}", interaction.Player.Name));
    }

    // Callers hold the gate.
    private INpc? LiveNpc(int id) => live.TryGetValue(id, out INpc? npc) && !npc.IsRemoved ? npc : null;

    private void HandleCommand(CommandInvocation c) {
        string action = c.Args.Length > 0 ? c.Args[0].ToLowerInvariant() : "help";
        switch (action) {
            case "spawn": SpawnCommand(c); break;
            case "list": ListCommand(c); break;
            case "say": SayCommand(c); break;
            case "lines": LinesCommand(c); break;
            case "clearlines": ClearLinesCommand(c); break;
            case "wizbang": WizBangCommand(c); break;
            case "state": StateCommand(c); break;
            case "move": MoveCommand(c); break;
            case "remove": RemoveCommand(c); break;
            case "reload": ReloadCommand(c); break;
            case "presets":
                c.Reply("Presets: " + string.Join(", ", Presets.Select(x => $"{x.Key} ({x.Value})")));
                break;
            default:
                c.Reply(".npc spawn <preset|templateId> [name] - place an NPC where you stand");
                c.Reply(".npc list - NPCs in your zone, nearest first");
                c.Reply(".npc say <id> <text> - add a line ({player} becomes the player's name)");
                c.Reply(".npc lines <id> / .npc clearlines <id>");
                c.Reply(".npc wizbang <id> <icon> / .npc state <id> <state>");
                c.Reply(".npc move <id> / .npc remove <id> / .npc reload / .npc presets");
                break;
        }
    }

    private void SpawnCommand(CommandInvocation c) {
        IPlayer player = c.Player;
        if (player.Zone is not { IsInstance: false } zone) {
            c.Reply("NPCs can only be placed in shared zones, not instances.");
            return;
        }
        if (c.Args.Length < 2) {
            c.Reply("Usage: .npc spawn <preset|templateId> [name]. See .npc presets.");
            return;
        }
        if (!Presets.TryGetValue(c.Args[1], out ulong templateId) && !ulong.TryParse(c.Args[1], out templateId)) {
            c.Reply($"{c.Args[1]} isn't a preset or a template ID. See .npc presets.");
            return;
        }

        var placement = new Placement {
            Zone = zone.Name,
            TemplateId = templateId,
            Name = c.Args.Length > 2 ? string.Join(' ', c.Args.Skip(2)) : null,
            Location = player.Location,
            Orientation = FacingBack(player.Orientation),
        };

        INpc npc;
        int id;
        lock (gate) {
            id = placements.Count == 0 ? 1 : placements.Keys.Max() + 1;
            try {
                npc = Spawn(id, placement, zone);
            } catch (ArgumentException) {
                c.Reply($"Template {templateId} isn't something that can be shown. Pick an NPC from npc.manifest.");
                return;
            } catch (InvalidOperationException) {
                c.Reply("Your zone is unloading. Try again in a moment.");
                return;
            }
            // Saved only once the spawn worked, so a bad template never ends up in npcs.json.
            placements[id] = placement;
            Save();
        }

        c.Reply($"Placed #{id} {npc.Name}. Give it something to say with .npc say {id} <text>.");
    }

    private void ListCommand(CommandInvocation c) {
        IPlayer player = c.Player;
        if (player.Zone is not { } zone) {
            return;
        }

        // IZone.Npcs has every plugin's NPCs, not just this one's.
        IReadOnlyList<INpc> npcs = zone.Npcs;
        if (npcs.Count == 0) {
            c.Reply("There are no plugin NPCs in this zone.");
            return;
        }

        Dictionary<ulong, int> ids;
        lock (gate) {
            ids = live.Where(x => !x.Value.IsRemoved).ToDictionary(x => x.Value.GlobalId, x => x.Key);
        }

        foreach (INpc npc in npcs.OrderBy(x => Vector3.Distance(x.Location, player.Location))) {
            string label = ids.TryGetValue(npc.GlobalId, out int id) ? $"#{id}" : "(another plugin)";
            string talks = npc.IsInteractable ? $"talks, {npc.WizBang}" : "silent";
            int distance = (int) Vector3.Distance(npc.Location, player.Location);
            c.Reply($"{label} {npc.Name} - template {npc.TemplateId}, {talks}, {distance} away");
        }
        c.Reply($"This plugin has {Npcs.Spawned.Count} NPC(s) spawned across all zones.");
    }

    private void SayCommand(CommandInvocation c) {
        if (c.Args.Length < 3) {
            c.Reply("Usage: .npc say <id> <text>");
            return;
        }
        string text = string.Join(' ', c.Args.Skip(2));

        lock (gate) {
            if (!TryGetPlacement(c, out int id, out Placement placement)) {
                return;
            }
            placement.Lines.Add(text);
            Save();
            // The first line makes the NPC interactable. Players already beside it see that the next time they
            // walk up to it.
            if (LiveNpc(id) is { } npc) {
                UpdateInteraction(id, placement, npc);
            }
            c.Reply($"#{id} now has {placement.Lines.Count} line(s).");
        }
    }

    private void LinesCommand(CommandInvocation c) {
        lock (gate) {
            if (!TryGetPlacement(c, out int id, out Placement placement)) {
                return;
            }
            if (placement.Lines.Count == 0) {
                c.Reply($"#{id} has nothing to say.");
                return;
            }
            for (int i = 0; i < placement.Lines.Count; i++) {
                c.Reply($"{i + 1}. {placement.Lines[i]}");
            }
        }
    }

    private void ClearLinesCommand(CommandInvocation c) {
        lock (gate) {
            if (!TryGetPlacement(c, out int id, out Placement placement)) {
                return;
            }
            placement.Lines.Clear();
            ForgetProgress(id);
            Save();
            LiveNpc(id)?.SetInteraction(null);
            c.Reply($"#{id} has nothing to say now, so players can't interact with it.");
        }
    }

    private void WizBangCommand(CommandInvocation c) {
        // TryParse also takes numbers, so IsDefined keeps out ones that aren't a wizbang.
        if (c.Args.Length < 3 || !Enum.TryParse(c.Args[2], true, out NpcWizBang wizBang)
            || !Enum.IsDefined(wizBang)) {
            c.Reply("Usage: .npc wizbang <id> <icon>. Icons: " + string.Join(", ", Enum.GetNames<NpcWizBang>()));
            return;
        }

        lock (gate) {
            if (!TryGetPlacement(c, out int id, out Placement placement)) {
                return;
            }
            placement.WizBang = wizBang;
            Save();
            // Nearby players see the new icon within a second, without walking away and back.
            LiveNpc(id)?.SetWizBang(wizBang);
            c.Reply(placement.Lines.Count > 0
                ? $"#{id} now shows {wizBang}."
                : $"#{id} will show {wizBang} once it has a line to say.");
        }
    }

    private void StateCommand(CommandInvocation c) {
        if (c.Args.Length < 3) {
            c.Reply("Usage: .npc state <id> <state>. Most NPCs have Idle, Emoting, Interacting and Dead. Off hides it.");
            return;
        }
        string state = c.Args[2];

        lock (gate) {
            if (!TryGetPlacement(c, out int id, out Placement placement)) {
                return;
            }
            placement.State = state;
            Save();
            // Players who arrive later see the NPC in this state too. A state the template doesn't have is ignored
            // by the client.
            LiveNpc(id)?.SetState(state);
            c.Reply($"#{id} is now in state {state}.");
        }
    }

    private void MoveCommand(CommandInvocation c) {
        IPlayer player = c.Player;
        if (player.Zone is not { IsInstance: false } zone) {
            c.Reply("NPCs can only be placed in shared zones, not instances.");
            return;
        }

        lock (gate) {
            if (!TryGetPlacement(c, out int id, out Placement placement)) {
                return;
            }

            // An NPC never moves, so it is removed and spawned again at the new spot. Remove is safe even if the
            // NPC's zone unloaded and took it already.
            if (live.Remove(id, out INpc? old)) {
                old.Remove();
            }
            placement.Zone = zone.Name;
            placement.Location = player.Location;
            placement.Orientation = FacingBack(player.Orientation);
            Save();

            try {
                Spawn(id, placement, zone);
                c.Reply($"Moved #{id} to you.");
            } catch (InvalidOperationException) {
                c.Reply($"Moved #{id}. It appears next time {zone.Name} loads.");
            }
        }
    }

    private void RemoveCommand(CommandInvocation c) {
        lock (gate) {
            if (!TryGetPlacement(c, out int id, out _)) {
                return;
            }
            placements.Remove(id);
            ForgetProgress(id);
            Save();
            if (live.Remove(id, out INpc? npc)) {
                npc.Remove();
            }
            c.Reply($"Removed #{id}.");
        }
    }

    // Picks up changes made to npcs.json by hand.
    private void ReloadCommand(CommandInvocation c) {
        lock (gate) {
            try {
                Load();
            } catch (JsonException ex) {
                c.Reply($"npcs.json couldn't be read, so nothing changed: {ex.Message}");
                return;
            }
            // RemoveAll only touches this plugin's NPCs, so other plugins' NPCs in the same zones stay.
            Npcs.RemoveAll();
            live.Clear();
            progress.Clear();
            foreach (IZone zone in Server.Zones) {
                SpawnInto(zone);
            }
            c.Reply($"Reloaded {placements.Count} placement(s). {Npcs.Spawned.Count} NPC(s) are in loaded zones.");
        }
    }

    // Reads the id in Args[1]. Callers hold the gate.
    private bool TryGetPlacement(CommandInvocation c, out int id, out Placement placement) {
        placement = null!;
        if (c.Args.Length < 2 || !int.TryParse(c.Args[1].TrimStart('#'), out id)) {
            id = 0;
            c.Reply($"Usage: .npc {c.Args[0]} <id>. Find ids with .npc list.");
            return false;
        }
        if (!placements.TryGetValue(id, out Placement? found)) {
            c.Reply($"There is no NPC #{id}.");
            return false;
        }
        placement = found;
        return true;
    }

    // Callers hold the gate.
    private void ForgetProgress(int id) {
        foreach ((int, ulong) key in progress.Keys.Where(x => x.Id == id).ToList()) {
            progress.Remove(key);
        }
    }

    // The player's camera is behind them, so turning the NPC around has it face the camera when it appears.
    private static float FacingBack(float orientation) => MathF.IEEERemainder(orientation + MathF.PI, MathF.Tau);

    // Callers hold the gate, apart from OnEnable, which runs before anything else can touch placements.
    private void Load() {
        placements = File.Exists(PlacementsPath)
            ? JsonSerializer.Deserialize<Dictionary<int, Placement>>(File.ReadAllText(PlacementsPath), JsonOptions) ?? []
            : [];
    }

    // Callers hold the gate.
    private void Save() => File.WriteAllText(PlacementsPath, JsonSerializer.Serialize(placements, JsonOptions));

}
