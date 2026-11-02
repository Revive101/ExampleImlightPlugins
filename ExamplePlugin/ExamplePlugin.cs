using System;
using Imlight.PluginApi;
using Imlight.PluginApi.Events;

namespace ExamplePlugin;

/// <summary>
/// Greets players when they enter a zone, and drops zone chat containing a blocked word.
/// </summary>
public sealed class ExamplePlugin : ImlightPlugin {

    public override string Name => "ExamplePlugin";

    public override string Version => "1.0.0";

    public override void OnEnable() {
        Events.Subscribe<OnPlayerZoneEnter>(e => e.Player.SendMessage($"Welcome to {e.Zone.Name}, {e.Player.Name}!"));

        Events.Subscribe<OnPlayerChat>(e => {
            if (e.Message.Contains("badword", StringComparison.OrdinalIgnoreCase)) {
                e.IsCancelled = true;
                e.Player.SendMessage("Your message was not sent because it contains a blocked word.");
            }
        });

        Events.Subscribe<OnDuelistDamage>(e => {
            IPlayer? player = e.Duelist as IPlayer;
            if (player is null) return;
            player.SendMessage($"You took {e.Damage}");
        });
        Events.Subscribe<OnPlayerKnockout>(e => e.Player.SendMessage($"You have been knocked out"));

        Commands.Register("ping", c => c.Reply("Pong!"), aliases: ["boing"]);
        Commands.Register("broadcast", c => broadcast(c.RawArgs, c.Player.Zone), permission: Imlight.PluginApi.Commands.CommandPermission.HallMonitor);
        Commands.Register("poke", c => poke(c.Args[0], c.Args[1], c.Player, c.Player.Zone));
    }

    private void broadcast(string message, IZone? zone) {
        if (zone is null) return;
        foreach (IPlayer player in zone.Players) {
            player.SendMessage(message);
        }
    }

    private void poke(string firstName, string lastName, IPlayer originPlayer, IZone? zone) {
        if (zone is null) return;
        string playerName = firstName + " " + lastName;
        foreach (IPlayer player in zone.Players) {
            // note that there can be multiple players with the same Wizard name
            // future builds will have a more player-friendly intuitive way to select players via a chat command
            if (player.Name.ToLower().Equals(playerName)) {
                player.SendMessage($"{originPlayer.Name} has poked you!");
            }
        }
    }

}
