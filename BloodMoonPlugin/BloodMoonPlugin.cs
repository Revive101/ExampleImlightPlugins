using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Imlight.PluginApi;
using Imlight.PluginApi.Commands;
using Imlight.PluginApi.Events;
using Imlight.PluginApi.Scheduling;

namespace BloodMoonPlugin;

/// <summary>
/// Runs a recurring server-wide event where creatures get tougher and smarter, and pay bonus gold when defeated.
/// </summary>
/// <remarks>
/// The moon only touches creatures in shared zones, and leaves bosses, minions and fights already running alone.
/// Settings are read from config.json in the plugin's data folder, which is created on first run.
/// </remarks>
public sealed class BloodMoonPlugin : ImlightPlugin {

    private sealed record Config(
        double IntervalMinutes = 180,
        double DurationMinutes = 20,
        double HealthMultiplier = 1.5,
        double ProximityMultiplier = 2,
        float IntelligenceBonus = 0.25f,
        float AggressivenessBonus = 0.25f,
        int GoldPerCreatureLevel = 5);

    // What a creature was like before the moon changed it, so it can be put back afterwards.
    private sealed record OriginalStats(ICreature Creature, int MaxHealth, float Intelligence, float Aggressiveness,
        float Proximity);

    // The bonus gold for defeating one creature. Loot only names a creature by template, so that's what this matches on.
    private sealed record Bounty(ulong CreatureTemplateId, int Gold);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private Config config = new();

    // Guards active, nextChange, changeTask and empowered. Scheduled tasks, commands and every creature's actor all
    // reach this state.
    private readonly object moonLock = new();
    private bool active;
    private DateTime nextChange;
    private IScheduledTask? changeTask;
    private readonly Dictionary<ulong, OriginalStats> empowered = [];

    // The bounties in each running duel, keyed by duel id. A sigil reuses its id for the next fight.
    private readonly ConcurrentDictionary<ulong, ConcurrentBag<Bounty>> bounties = [];

    // The bounties each winner has yet to get from their loot, keyed by character id. Every winner has their own list.
    private readonly ConcurrentDictionary<ulong, List<Bounty>> unclaimed = [];

    private string ConfigPath => Path.Combine(DataFolder, "config.json");

    public override string Name => "BloodMoonPlugin";

    public override string Version => "1.0.0";

    public override void OnEnable() {
        // Load first: if the config is bad, this throws before anything is subscribed and the plugin stays off.
        LoadConfig();

        Commands.Register("bloodmoon", c => c.Reply(Status()), aliases: ["bm"]);
        Commands.Register("startbloodmoon", c => {
            if (!Rise()) {
                c.Reply("The Blood Moon is already up.");
            }
        }, CommandPermission.Administrator);
        Commands.Register("stopbloodmoon", c => {
            if (!Set()) {
                c.Reply("There is no Blood Moon right now.");
            }
        }, CommandPermission.Administrator);

        Events.Subscribe<OnCreatureSpawn>(e => {
            lock (moonLock) {
                if (active) {
                    Empower(e.Creature);
                }
            }
        });
        Events.Subscribe<OnCreatureDespawn>(e => {
            lock (moonLock) {
                empowered.Remove(e.Creature.GlobalId);
            }
        });

        Events.Subscribe<OnDuelStart>(e => {
            // Clears the bounty of an earlier fight on this sigil that ended without a winner.
            bounties.TryRemove(e.Duel.Id, out _);
            foreach (ICreature creature in e.Duel.Creatures) {
                AddBounty(e.Duel, creature);
            }
        });
        Events.Subscribe<OnDuelistJoin>(e => {
            if (e.Duelist is ICreature creature) {
                AddBounty(e.Duel, creature);
            }
        });
        Events.Subscribe<OnDuelEnd>(e => {
            bounties.TryRemove(e.Duel.Id, out ConcurrentBag<Bounty>? won);
            foreach (IPlayer winner in e.Winners) {
                // Replaces anything left from an earlier duel, such as loot that a disconnect stopped from rolling.
                unclaimed[winner.CharacterId] = [.. won ?? []];
            }
        });
        // Fires once per creature, on the winner's session, after OnDuelEnd has filled in their list.
        Events.Subscribe<OnCreatureLoot>(e => {
            if (!unclaimed.TryGetValue(e.Player.CharacterId, out List<Bounty>? owed)) {
                return;
            }
            int index = owed.FindIndex(x => x.CreatureTemplateId == e.CreatureTemplateId);
            if (index >= 0) {
                e.Gold += owed[index].Gold;
                owed.RemoveAt(index);
            }
        });

        lock (moonLock) {
            Schedule(TimeSpan.FromMinutes(config.IntervalMinutes));
        }
    }

    private void LoadConfig() {
        if (File.Exists(ConfigPath)) {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath)) ?? config;
        } else {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
        }

        if (config.IntervalMinutes <= 0 || config.DurationMinutes <= 0) {
            throw new InvalidDataException("IntervalMinutes and DurationMinutes must be above 0.");
        }
        if (config.HealthMultiplier <= 0 || config.ProximityMultiplier < 0 || config.GoldPerCreatureLevel < 0) {
            throw new InvalidDataException("HealthMultiplier must be above 0, and ProximityMultiplier and "
                + "GoldPerCreatureLevel can't be negative.");
        }
    }

    private void ChangeMoon() {
        bool isUp;
        lock (moonLock) {
            isUp = active;
        }
        if (isUp) {
            Set();
        } else {
            Rise();
        }
    }

    /// <summary>
    /// Starts the Blood Moon. Returns false if it is already up.
    /// </summary>
    private bool Rise() {
        lock (moonLock) {
            if (active) {
                return false;
            }
            active = true;

            foreach (ICreature creature in Server.Zones.SelectMany(zone => zone.Creatures)) {
                Empower(creature);
            }
            Schedule(TimeSpan.FromMinutes(config.DurationMinutes));
        }

        Server.Broadcast("The Blood Moon rises! Creatures are stronger, and bonus gold awaits those who defeat them.");
        return true;
    }

    /// <summary>
    /// Ends the Blood Moon. Returns false if it isn't up.
    /// </summary>
    private bool Set() {
        lock (moonLock) {
            if (!active) {
                return false;
            }
            active = false;

            // A creature in a duel stays in it until it is gone, and shrinking it now would take health from a fight
            // in progress. It keeps its stats, and OnCreatureDespawn clears it.
            foreach (OriginalStats original in empowered.Values.Where(x => !x.Creature.InCombat).ToList()) {
                Restore(original);
                empowered.Remove(original.Creature.GlobalId);
            }
            Schedule(TimeSpan.FromMinutes(config.IntervalMinutes));
        }

        Server.Broadcast("The Blood Moon has set.");
        return true;
    }

    // Callers hold moonLock.
    private void Schedule(TimeSpan delay) {
        nextChange = DateTime.UtcNow + delay;
        // A command can change the moon early, so the change that was waiting has to go.
        changeTask?.Cancel();
        changeTask = Scheduler.RunAfter(delay, ChangeMoon);
    }

    // Callers hold moonLock.
    private void Empower(ICreature creature) {
        if (creature.Zone.IsInstance || creature.IsBoss || creature.IsMinion || creature.InCombat
            || empowered.ContainsKey(creature.GlobalId)) {
            return;
        }

        empowered[creature.GlobalId] = new OriginalStats(creature, creature.MaxHealth, creature.Intelligence,
            creature.Aggressiveness, creature.Proximity);

        // SetMaxHealth doesn't heal, so the creature is topped up to match. These writes apply in order.
        int maxHealth = Math.Max(1, (int) (creature.MaxHealth * config.HealthMultiplier));
        creature.SetMaxHealth(maxHealth);
        creature.SetHealth(maxHealth);
        creature.SetIntelligence(Math.Clamp(creature.Intelligence + config.IntelligenceBonus, 0, 1));
        creature.SetAggressiveness(Math.Clamp(creature.Aggressiveness + config.AggressivenessBonus, 0, 1));
        // Creatures with a proximity of 0 only fight when scripted to, and multiplying keeps them that way.
        creature.SetProximity((float) (creature.Proximity * config.ProximityMultiplier));
    }

    private static void Restore(OriginalStats original) {
        ICreature creature = original.Creature;
        creature.SetMaxHealth(original.MaxHealth);
        creature.SetIntelligence(original.Intelligence);
        creature.SetAggressiveness(original.Aggressiveness);
        creature.SetProximity(original.Proximity);
    }

    /// <summary>
    /// Adds a creature's bounty to its duel, if the moon empowered it.
    /// </summary>
    private void AddBounty(IDuel duel, ICreature creature) {
        lock (moonLock) {
            if (!empowered.ContainsKey(creature.GlobalId)) {
                return;
            }
        }
        bounties.GetOrAdd(duel.Id, _ => []).Add(new Bounty(creature.TemplateId,
            creature.Level * config.GoldPerCreatureLevel));
    }

    private string Status() {
        lock (moonLock) {
            int minutes = (int) Math.Ceiling((nextChange - DateTime.UtcNow).TotalMinutes);
            return active
                ? $"The Blood Moon is up! It sets in about {minutes} minute(s)."
                : $"The next Blood Moon rises in about {minutes} minute(s).";
        }
    }

}
