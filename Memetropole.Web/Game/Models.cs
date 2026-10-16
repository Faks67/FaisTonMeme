using System.Text.Json.Serialization;
using Memetropole.Web.Library;

namespace Memetropole.Web.Game;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Phase { Lobby, Captioning, Voting, RoundResults, Final }

public sealed class GameSettings
{
    public int Rounds { get; set; } = 3;
    public int CaptionSeconds { get; set; } = 75;
    public int VoteSeconds { get; set; } = 12;
    public int Rerolls { get; set; } = 2;
    public bool Images { get; set; } = true;
    public bool Gifs { get; set; } = true;
    public bool Videos { get; set; } = true;

    public GameSettings Normalized()
    {
        bool any = Images || Gifs || Videos; // au moins un type de média
        return new()
        {
            Rounds = Math.Clamp(Rounds, 1, 10),
            CaptionSeconds = Math.Clamp(CaptionSeconds, 20, 180),
            VoteSeconds = Math.Clamp(VoteSeconds, 5, 30),
            Rerolls = Math.Clamp(Rerolls, 0, 5),
            Images = Images || !any,
            Gifs = Gifs || !any,
            Videos = Videos || !any,
        };
    }
}

public sealed class Player
{
    public string Id { get; init; } = "";
    /// <summary>Secret gardé par l'onglet pour se reconnecter après un rechargement.</summary>
    public string Token { get; init; } = "";
    public string Name { get; set; } = "";
    public int Color { get; init; }
    public string? ConnectionId { get; set; }
    public DateTimeOffset DisconnectedAt { get; set; }
    public int Score { get; set; }
    public int RerollsLeft { get; set; }
    public bool Connected => ConnectionId is not null;
}

/// <summary>Une zone de texte. X et Y sont le centre de la zone, en pourcentage de l'image.</summary>
public sealed class Caption
{
    public string Text { get; set; } = "";
    public double X { get; set; } = 50;
    public double Y { get; set; } = 50;
}

/// <summary>Le mème d'un joueur pour une manche.</summary>
public sealed class Entry
{
    public string PlayerId { get; init; } = "";
    public MemeTemplate Template { get; set; } = new();
    public List<Caption> Captions { get; set; } = [];
    public bool Submitted { get; set; }
    /// <summary>Votant → note de 1 à 3.</summary>
    public Dictionary<string, int> Votes { get; } = new();
    public int Points => Votes.Values.Sum();
    public bool HasText => Captions.Any(c => !string.IsNullOrWhiteSpace(c.Text));
}

public sealed class Room
{
    public string Code { get; init; } = "";
    public string HostId { get; set; } = "";
    public List<Player> Players { get; } = [];
    public GameSettings Settings { get; set; } = new();
    public Phase Phase { get; set; } = Phase.Lobby;
    public int Round { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public Dictionary<string, Entry> Entries { get; set; } = new();
    public List<Entry> VoteOrder { get; set; } = [];
    public int VoteIndex { get; set; }
    /// <summary>Modèles KLIPY récupérés par le navigateur de l'hôte, valables pour cette partie seulement.</summary>
    public List<MemeTemplate> KlipyPool { get; set; } = [];
    public HashSet<string> UsedTemplates { get; } = [];
    /// <summary>Meilleur mème de chaque manche, pour le récapitulatif final.</summary>
    public List<Entry> Highlights { get; } = [];
    public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;

    public Player? Find(string id) => Players.FirstOrDefault(p => p.Id == id);
}
