using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Imlight.PluginApi;
using Imlight.PluginApi.Commands;

namespace StarterKitPlugin;

/// <summary>
/// Lets each character claim a one-time starter kit for their school with .starterkit.
/// </summary>
/// <remarks>
/// Kits are looked up by internal name when the plugin loads, so a name the game files don't have stops the plugin
/// there rather than in front of a player. Claims are kept in claimed.json in the plugin's data folder, so they
/// survive restarts.
/// </remarks>
public sealed class StarterKitPlugin : ImlightPlugin {

    // A school's kit, with the template details the command needs.
    private sealed record Kit(IReadOnlyList<ItemInfo> Items, IReadOnlyList<SpellInfo> Spells, SpellInfo WandSpell);

    // The internal names of a school's spells. Internal names don't follow a pattern, and the case has to match.
    private sealed record SchoolSpells(string Wand, string[] Taught);

    private const int Gold = 500;

    // Every wizard's kit gets this deck. It holds 14 spells and 5 treasure cards.
    private const string DeckName = "Deck-T1-001";

    // Wand is the spell the school's starter wand gives while equipped. Taught is the first two spells of the school.
    private static readonly Dictionary<School, SchoolSpells> Spells = new() {
        [School.Fire] = new("Wand-Fire-T2-007", ["Fire Cat", "Fireblade"]),
        [School.Ice] = new("Wand-Ice-T2-010", ["Frost Beetle", "Freeze"]),
        [School.Storm] = new("Wand-Storm-T2-019", ["Thunder Snake", "Lightning Strike"]),
        [School.Myth] = new("Wand-Myth-T2-016", ["Bloodbat", "Minion Myth 000"]),
        [School.Life] = new("Wand-Life-T2-013", ["Imp", "Minor Blessing"]),
        [School.Death] = new("Wand-Death-T2-004", ["Dark Sprite", "Death Trap"]),
        [School.Balance] = new("Wand-Balance-T3-001", ["Scarab", "Tri Shield"]),
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly Dictionary<School, Kit> kits = [];

    // Character ids. Only the command touches this, and commands all run on one dispatcher, so it needs no lock.
    private HashSet<ulong> claimed = [];

    private string ClaimedPath => Path.Combine(DataFolder, "claimed.json");

    public override string Name => "StarterKitPlugin";

    public override string Version => "1.0.0";

    public override void OnEnable() {
        // Load first: if a kit is bad, this throws before the command exists and the plugin stays off. It's also the
        // cheap place to do it, as the first lookup of an item reads it from the game files.
        foreach ((School school, SchoolSpells spells) in Spells) {
            kits[school] = LoadKit(school, spells);
        }

        if (File.Exists(ClaimedPath)) {
            claimed = JsonSerializer.Deserialize<HashSet<ulong>>(File.ReadAllText(ClaimedPath)) ?? [];
        }

        Commands.Register("starterkit", Claim, aliases: ["kit"]);
    }

    private Kit LoadKit(School school, SchoolSpells spells) {
        ItemInfo[] items = [
            FindItem($"Crown-Starter-Hat-{school}-01"),
            FindItem($"Crown-Starter-Robe-{school}-01"),
            FindItem($"Crown-Starter-Shoes-{school}-01"),
            FindItem($"Crown-Starter-Wand-{school}-01"),
            FindItem(DeckName),
        ];
        return new Kit(items, [.. spells.Taught.Select(FindSpell)], FindSpell(spells.Wand));
    }

    private ItemInfo FindItem(string internalName)
        => Server.FindItems(internalName).FirstOrDefault()
            ?? throw new InvalidDataException($"The game files have no item named {internalName}.");

    private SpellInfo FindSpell(string name)
        => Server.GetSpell(name) ?? throw new InvalidDataException($"The game files have no spell named {name}.");

    private void Claim(CommandInvocation c) {
        IPlayer player = c.Player;
        if (claimed.Contains(player.CharacterId)) {
            c.Reply("You have already claimed your starter kit.");
            return;
        }
        // The kits cover the seven schools a wizard can pick.
        if (!kits.TryGetValue(player.School, out Kit? kit)) {
            c.Reply("There is no starter kit for your school.");
            return;
        }

        // Saved before anything is given, so no failure below can let a character claim the kit twice.
        claimed.Add(player.CharacterId);
        File.WriteAllText(ClaimedPath, JsonSerializer.Serialize(claimed, JsonOptions));

        foreach (ItemInfo item in kit.Items) {
            player.GiveItem(item.TemplateId);
        }
        player.AddGold(Gold);
        // Learn is false for a spell the wizard already knows, so a quest-given first spell isn't announced as new.
        List<SpellInfo> learned = [.. kit.Spells.Where(x => player.Learn(x.TemplateId))];

        string items = string.Join(", ", kit.Items.Select(x => CleanName(x.Name)));
        c.Reply($"You received your starter kit: {items} and {Gold} gold.");
        if (learned.Count > 0) {
            c.Reply($"You learned {string.Join(" and ", learned.Select(x => x.DisplayName))}.");
        }
        c.Reply($"Your wand gives you {kit.WandSpell.DisplayName} cards: {kit.WandSpell.PipCost} pips, "
            + $"{kit.WandSpell.Accuracy}% accuracy.");
    }

    // Some English names carry the game's line break marker as plain text, such as "Starter Ice\nConical".
    private static string CleanName(string name) => name.Replace("\\n", " ");

}
