using System.Collections.Concurrent;
using System.Security.Cryptography;
using Memetropole.Web.Library;
using Microsoft.AspNetCore.SignalR;

namespace Memetropole.Web.Game;

/// <summary>
/// Toutes les salles en mémoire et les règles du jeu. Le serveur fait autorité :
/// il tient les chronomètres, choisit les modèles et compte les votes.
/// Chaque salle est verrouillée pendant qu'on la modifie ; l'état est ensuite envoyé
/// à chaque joueur (chacun reçoit sa propre vue, avec son modèle et son mème).
/// </summary>
public sealed class RoomManager(LibraryStore library, IHubContext<GameHub> hub, ILogger<RoomManager> log)
{
    public const int MaxPlayers = 12;
    const string CodeLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    static readonly TimeSpan RoundResultsDuration = TimeSpan.FromSeconds(9);
    static readonly TimeSpan HostGrace = TimeSpan.FromSeconds(8);
    static readonly TimeSpan LobbyGrace = TimeSpan.FromSeconds(45);
    static readonly TimeSpan EmptyRoomLifetime = TimeSpan.FromMinutes(15);

    readonly ConcurrentDictionary<string, Room> _rooms = new();
    readonly ConcurrentDictionary<string, (string code, string playerId)> _byConnection = new();

    public int RoomCount => _rooms.Count;

    // ---------- Entrée / sortie ----------

    public (Room room, Player player) Create(string name, string connectionId)
    {
        string code;
        do code = new string(Enumerable.Range(0, 4).Select(_ => CodeLetters[RandomNumberGenerator.GetInt32(CodeLetters.Length)]).ToArray());
        while (_rooms.ContainsKey(code));

        var room = new Room { Code = code };
        var player = AddPlayer(room, name, connectionId);
        room.HostId = player.Id;
        _rooms[code] = room;
        log.LogInformation("Salle {Code} créée", code);
        return (room, player);
    }

    public (Room room, Player player) Join(string code, string name, string connectionId)
    {
        var room = GetRoom(code) ?? throw new HubException("Aucune salle avec ce code.");
        lock (room)
        {
            if (room.Phase != Phase.Lobby) throw new HubException("La partie a déjà commencé dans cette salle.");
            if (room.Players.Count >= MaxPlayers) throw new HubException($"La salle est pleine ({MaxPlayers} joueurs).");
            return (room, AddPlayer(room, name, connectionId));
        }
    }

    public (Room room, Player player) Rejoin(string code, string playerId, string token, string connectionId)
    {
        var room = GetRoom(code) ?? throw new HubException("Cette salle n'existe plus.");
        lock (room)
        {
            var p = room.Find(playerId);
            if (p is null || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(p.Token), System.Text.Encoding.UTF8.GetBytes(token ?? "")))
                throw new HubException("Impossible de reprendre ta place dans cette salle.");
            if (p.ConnectionId is not null) _byConnection.TryRemove(p.ConnectionId, out _);
            p.ConnectionId = connectionId;
            _byConnection[connectionId] = (room.Code, p.Id);
            room.LastActivity = DateTimeOffset.UtcNow;
            return (room, p);
        }
    }

    Player AddPlayer(Room room, string name, string connectionId)
    {
        name = CleanName(name);
        var baseName = name;
        for (int i = 2; room.Players.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"{baseName} {i}";
        var used = room.Players.Select(p => p.Color).ToHashSet();
        var p = new Player
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            Name = name,
            Color = Enumerable.Range(0, MaxPlayers).FirstOrDefault(c => !used.Contains(c)),
            ConnectionId = connectionId,
        };
        room.Players.Add(p);
        room.LastActivity = DateTimeOffset.UtcNow;
        _byConnection[connectionId] = (room.Code, p.Id);
        return p;
    }

    /// <summary>Le joueur quitte volontairement la salle.</summary>
    public Room? Leave(string connectionId)
    {
        if (!_byConnection.TryRemove(connectionId, out var who)) return null;
        var room = GetRoom(who.code);
        if (room is null) return null;
        lock (room)
        {
            var p = room.Find(who.playerId);
            if (p is null) return room;
            room.Players.Remove(p);
            room.Entries.Remove(p.Id);
            AfterPlayerGone(room, p);
            return room;
        }
    }

    /// <summary>Connexion perdue (onglet fermé, réseau…) : on garde la place un moment.</summary>
    public Room? Disconnected(string connectionId)
    {
        if (!_byConnection.TryRemove(connectionId, out var who)) return null;
        var room = GetRoom(who.code);
        if (room is null) return null;
        lock (room)
        {
            var p = room.Find(who.playerId);
            if (p is null || p.ConnectionId != connectionId) return room;
            p.ConnectionId = null;
            p.DisconnectedAt = DateTimeOffset.UtcNow;
            CheckEarlyAdvance(room);
            return room;
        }
    }

    void AfterPlayerGone(Room room, Player p)
    {
        if (room.Players.Count == 0) { _rooms.TryRemove(room.Code, out _); return; }
        if (room.HostId == p.Id) PassHost(room);
        CheckEarlyAdvance(room);
    }

    static void PassHost(Room room)
    {
        var next = room.Players.FirstOrDefault(x => x.Connected) ?? room.Players.FirstOrDefault();
        if (next is not null) room.HostId = next.Id;
    }

    public (Room room, Player player)? Who(string connectionId)
    {
        if (!_byConnection.TryGetValue(connectionId, out var who)) return null;
        var room = GetRoom(who.code);
        var p = room?.Find(who.playerId);
        return room is null || p is null ? null : (room, p);
    }

    Room? GetRoom(string code) => _rooms.GetValueOrDefault((code ?? "").Trim().ToUpperInvariant());

    // ---------- Actions de l'hôte ----------

    public void UpdateSettings(Room room, Player p, GameSettings s)
    {
        lock (room)
        {
            RequireHost(room, p);
            if (room.Phase != Phase.Lobby) throw new HubException("Les réglages ne changent qu'avant la partie.");
            room.Settings = s.Normalized();
        }
    }

    /// <summary>
    /// Le navigateur de l'hôte a interrogé KLIPY (exigence de KLIPY : appels depuis le client).
    /// On ne garde que des liens vers static.klipy.com, en mémoire et pour cette partie seulement.
    /// </summary>
    public void SetKlipyPool(Room room, Player p, List<KlipyItem> items)
    {
        lock (room)
        {
            RequireHost(room, p);
            if (room.Phase != Phase.Lobby) return;
            room.KlipyPool = items.Take(60).Select(ToTemplate).OfType<MemeTemplate>().ToList();
        }
    }

    static MemeTemplate? ToTemplate(KlipyItem k)
    {
        static bool Ok(string? u) => u is not null && u.StartsWith("https://static.klipy.com/", StringComparison.Ordinal);
        if (!Ok(k.Mp4) || (k.Poster is not null && !Ok(k.Poster))) return null;
        if (k.Page is not null && !k.Page.StartsWith("https://klipy.com/", StringComparison.Ordinal)) k.Page = null;
        var title = (k.Title ?? "").Trim();
        return new MemeTemplate
        {
            Id = "klipy:" + (k.Id ?? Guid.NewGuid().ToString("N")),
            Title = title.Length > 80 ? title[..80] : title,
            Kind = k.Type == "clip" ? MediaKind.Video : MediaKind.Gif,
            Source = "klipy",
            MediaUrl = k.Mp4,
            ThumbnailUrl = k.Poster,
            PageUrl = k.Page,
            Width = Math.Clamp(k.Width, 1, 4000),
            Height = Math.Clamp(k.Height, 1, 4000),
            BoxCount = 1,
            FirstSeen = DateTimeOffset.UtcNow,
            PublishedAt = DateTimeOffset.UtcNow,
        };
    }

    public void Start(Room room, Player p)
    {
        lock (room)
        {
            RequireHost(room, p);
            if (room.Phase is not (Phase.Lobby or Phase.Final)) throw new HubException("La partie est déjà en cours.");
            if (room.Players.Count(x => x.Connected) < 2) throw new HubException("Il faut au moins 2 joueurs pour lancer la partie.");
            foreach (var pl in room.Players) { pl.Score = 0; pl.RerollsLeft = room.Settings.Rerolls; }
            room.Players.RemoveAll(x => !x.Connected);
            room.Round = 0;
            room.UsedTemplates.Clear();
            room.Highlights.Clear();
            StartCaptioning(room);
        }
    }

    public void BackToLobby(Room room, Player p)
    {
        lock (room)
        {
            RequireHost(room, p);
            room.Phase = Phase.Lobby;
            room.Deadline = null;
            room.Entries.Clear();
            room.VoteOrder.Clear();
            room.KlipyPool.Clear();
            foreach (var pl in room.Players) pl.Score = 0;
        }
    }

    static void RequireHost(Room room, Player p)
    {
        if (room.HostId != p.Id) throw new HubException("Seul l'hôte peut faire ça.");
    }

    // ---------- Légendes ----------

    public void Reroll(Room room, Player p)
    {
        lock (room)
        {
            if (room.Phase != Phase.Captioning || !room.Entries.TryGetValue(p.Id, out var e)) return;
            if (p.RerollsLeft <= 0) throw new HubException("Plus de changement de modèle disponible.");
            var t = PickTemplate(room);
            if (t is null) throw new HubException("Pas d'autre modèle disponible.");
            p.RerollsLeft--;
            e.Template = t;
            e.Captions = DefaultCaptions(t);
            e.Submitted = false;
        }
    }

    /// <summary>Enregistre le brouillon ou la version finale (final = « J'ai fini »).</summary>
    public bool SaveCaptions(Room room, Player p, List<Caption> captions, bool final)
    {
        lock (room)
        {
            if (room.Phase != Phase.Captioning || !room.Entries.TryGetValue(p.Id, out var e)) return false;
            var clean = captions.Take(e.Captions.Count).Select(c => new Caption
            {
                Text = (c.Text ?? "").Length > 140 ? c.Text![..140] : c.Text ?? "",
                X = Math.Clamp(double.IsFinite(c.X) ? c.X : 50, 0, 100),
                Y = Math.Clamp(double.IsFinite(c.Y) ? c.Y : 50, 0, 100),
            }).ToList();
            while (clean.Count < e.Captions.Count) clean.Add(e.Captions[clean.Count]);
            e.Captions = clean;
            var wasSubmitted = e.Submitted;
            e.Submitted = final;
            room.LastActivity = DateTimeOffset.UtcNow;
            CheckEarlyAdvance(room);
            return wasSubmitted != final || final; // les brouillons ne déclenchent pas de diffusion
        }
    }

    // ---------- Votes ----------

    public void Vote(Room room, Player p, int index, int score)
    {
        lock (room)
        {
            if (room.Phase != Phase.Voting || index != room.VoteIndex || index >= room.VoteOrder.Count) return;
            var e = room.VoteOrder[index];
            if (e.PlayerId == p.Id) throw new HubException("Tu ne peux pas voter pour ton propre mème.");
            e.Votes[p.Id] = Math.Clamp(score, 1, 3);
            CheckEarlyAdvance(room);
        }
    }

    // ---------- Déroulement ----------

    void StartCaptioning(Room room)
    {
        room.Round++;
        room.Phase = Phase.Captioning;
        room.Entries = new();
        foreach (var p in room.Players)
        {
            var t = PickTemplate(room) ?? library.All().First();
            room.Entries[p.Id] = new Entry { PlayerId = p.Id, Template = t, Captions = DefaultCaptions(t) };
        }
        room.Deadline = DateTimeOffset.UtcNow.AddSeconds(room.Settings.CaptionSeconds);
    }

    void StartVoting(Room room)
    {
        room.VoteOrder = room.Entries.Values.Where(e => e.HasText).OrderBy(_ => Random.Shared.Next()).ToList();
        room.VoteIndex = 0;
        if (room.VoteOrder.Count == 0) { StartRoundResults(room); return; }
        room.Phase = Phase.Voting;
        room.Deadline = DateTimeOffset.UtcNow.AddSeconds(VoteDuration(room, room.VoteOrder[0]));
    }

    static int VoteDuration(Room room, Entry e) =>
        room.Settings.VoteSeconds + (e.Template.Kind == MediaKind.Video ? 6 : 0);

    void StartRoundResults(Room room)
    {
        foreach (var e in room.VoteOrder)
        {
            var p = room.Find(e.PlayerId);
            if (p is not null) p.Score += e.Points;
        }
        var best = room.VoteOrder.OrderByDescending(e => e.Points).FirstOrDefault();
        if (best is not null && best.Points > 0) room.Highlights.Add(best);
        room.Phase = Phase.RoundResults;
        room.Deadline = DateTimeOffset.UtcNow + RoundResultsDuration;
    }

    void Next(Room room)
    {
        switch (room.Phase)
        {
            case Phase.Captioning:
                StartVoting(room);
                break;
            case Phase.Voting:
                room.VoteIndex++;
                if (room.VoteIndex < room.VoteOrder.Count)
                    room.Deadline = DateTimeOffset.UtcNow.AddSeconds(VoteDuration(room, room.VoteOrder[room.VoteIndex]));
                else StartRoundResults(room);
                break;
            case Phase.RoundResults:
                if (room.Round < room.Settings.Rounds) StartCaptioning(room);
                else { room.Phase = Phase.Final; room.Deadline = null; }
                break;
        }
    }

    /// <summary>Passe à la suite sans attendre la fin du chrono quand tout le monde a terminé.</summary>
    void CheckEarlyAdvance(Room room)
    {
        var active = room.Players.Where(p => p.Connected).ToList();
        if (active.Count == 0) return;
        bool done = room.Phase switch
        {
            Phase.Captioning => active.All(p => !room.Entries.TryGetValue(p.Id, out var e) || e.Submitted),
            Phase.Voting => room.VoteIndex < room.VoteOrder.Count
                && active.Where(p => p.Id != room.VoteOrder[room.VoteIndex].PlayerId)
                         .All(p => room.VoteOrder[room.VoteIndex].Votes.ContainsKey(p.Id)),
            _ => false,
        };
        // On laisse une petite seconde pour que le dernier vote s'affiche.
        if (done) room.Deadline = DateTimeOffset.UtcNow.AddMilliseconds(room.Phase == Phase.Voting ? 900 : 300);
    }

    /// <summary>Appelé plusieurs fois par seconde : fait avancer les salles dont le chrono est écoulé.</summary>
    public async Task TickAsync()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var room in _rooms.Values)
        {
            bool changed = false;
            lock (room)
            {
                if (room.Deadline is { } d && now >= d && room.Phase is not (Phase.Lobby or Phase.Final))
                {
                    Next(room);
                    changed = true;
                }
                if (room.Players.Find(p => p.Id == room.HostId) is { Connected: false } host && now - host.DisconnectedAt > HostGrace
                    && room.Players.Any(p => p.Connected))
                {
                    PassHost(room);
                    changed = true;
                }
                if (room.Phase is Phase.Lobby or Phase.Final)
                {
                    var gone = room.Players.Where(p => !p.Connected && now - p.DisconnectedAt > LobbyGrace).ToList();
                    foreach (var p in gone) { room.Players.Remove(p); changed = true; }
                    if (gone.Count > 0 && room.Players.Count > 0 && room.Find(room.HostId) is null) PassHost(room);
                }
                if (changed) room.LastActivity = now;
                if (room.Players.Count == 0 || (!room.Players.Any(p => p.Connected) && now - room.LastActivity > EmptyRoomLifetime))
                {
                    _rooms.TryRemove(room.Code, out _);
                    log.LogInformation("Salle {Code} fermée", room.Code);
                    continue;
                }
            }
            if (changed) await BroadcastAsync(room);
        }
    }

    // ---------- Choix des modèles ----------

    static List<Caption> DefaultCaptions(MemeTemplate t)
    {
        if (t.Kind != MediaKind.Image) return [new Caption { X = 50, Y = 0 }];
        int n = Math.Clamp(t.BoxCount, 1, 6);
        if (n == 1) return [new Caption { X = 50, Y = 12 }];
        // Réparties de haut en bas, comme le classique « texte du haut / texte du bas ».
        return Enumerable.Range(0, n).Select(i => new Caption { X = 50, Y = 10 + 80.0 * i / (n - 1) }).ToList();
    }

    /// <summary>
    /// Tire d'abord un type de média parmi ceux autorisés et disponibles (pour que les vidéos
    /// et GIF sortent vraiment), puis un modèle, en favorisant les plus récents et les mieux classés.
    /// </summary>
    MemeTemplate? PickTemplate(Room room)
    {
        var s = room.Settings;
        var all = library.All().Concat(room.KlipyPool)
            .Where(t => (t.Kind == MediaKind.Image && s.Images) || (t.Kind == MediaKind.Gif && s.Gifs) || (t.Kind == MediaKind.Video && s.Videos))
            .ToList();
        var fresh = all.Where(t => !room.UsedTemplates.Contains(t.Id)).ToList();
        if (fresh.Count == 0)
        {
            // Tout a servi : on recommence, en évitant quand même les modèles déjà donnés dans cette manche.
            room.UsedTemplates.Clear();
            var inRound = room.Entries.Values.Select(e => e.Template.Id).ToHashSet();
            fresh = all.Where(t => !inRound.Contains(t.Id)).ToList();
            if (fresh.Count == 0) fresh = all;
        }
        if (fresh.Count == 0) return null;

        var kinds = fresh.Select(t => t.Kind).Distinct().ToList();
        var kind = kinds[Random.Shared.Next(kinds.Count)];
        var candidates = fresh.Where(t => t.Kind == kind).ToList();

        var oldest = library.OldestFirstSeen;
        var weights = candidates.Select(t => Weight(t, oldest)).ToArray();
        var roll = Random.Shared.NextDouble() * weights.Sum();
        for (int i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0) { room.UsedTemplates.Add(candidates[i].Id); return candidates[i]; }
        }
        room.UsedTemplates.Add(candidates[^1].Id);
        return candidates[^1];
    }

    static double Weight(MemeTemplate t, DateTimeOffset oldest)
    {
        double w = 1;
        if (IsNew(t, oldest)) w += 2;
        if (t.Rank is <= 25) w += 1;
        return w;
    }

    /// <summary>
    /// « Nouveau » : publié il y a moins de 7 jours, ou entré dans la bibliothèque il y a moins
    /// de 7 jours (sauf lors du tout premier remplissage, où tout serait « nouveau »).
    /// </summary>
    public static bool IsNew(MemeTemplate t, DateTimeOffset oldest)
    {
        var now = DateTimeOffset.UtcNow;
        if (t.PublishedAt is { } pub && now - pub < TimeSpan.FromDays(7)) return true;
        return now - t.FirstSeen < TimeSpan.FromDays(7) && t.FirstSeen - oldest > TimeSpan.FromHours(1);
    }

    // ---------- Diffusion de l'état ----------

    public async Task BroadcastAsync(Room room)
    {
        List<(string conn, StateDto state)> outgoing;
        lock (room)
            outgoing = room.Players.Where(p => p.ConnectionId is not null)
                                   .Select(p => (p.ConnectionId!, BuildState(room, p))).ToList();
        foreach (var (conn, state) in outgoing)
            await hub.Clients.Client(conn).SendAsync("state", state);
    }

    public StateDto BuildState(Room room, Player me)
    {
        var oldest = library.OldestFirstSeen;
        MemeDto Meme(Entry e, bool reveal) => new(
            TemplateDto.From(e.Template, IsNew(e.Template, oldest)), e.Captions,
            reveal ? e.PlayerId : null, reveal ? e.Points : null);

        room.Entries.TryGetValue(me.Id, out var mine);
        VoteDto? vote = null;
        if (room.Phase == Phase.Voting && room.VoteIndex < room.VoteOrder.Count)
        {
            var e = room.VoteOrder[room.VoteIndex];
            vote = new(room.VoteIndex, room.VoteOrder.Count, Meme(e, false), e.PlayerId == me.Id,
                e.Votes.GetValueOrDefault(me.Id), e.Votes.Count);
        }

        return new StateDto(
            room.Code, me.Id, room.HostId, room.Phase, room.Round, room.Settings.Rounds,
            room.Deadline?.ToUnixTimeMilliseconds(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            room.Settings, room.KlipyPool.Count,
            room.Players.Select(p => new PlayerDto(p.Id, p.Name, p.Color, p.Score, p.Connected, IsDone(room, p))).ToList(),
            mine is null || room.Phase != Phase.Captioning ? null
                : new TaskDto(TemplateDto.From(mine.Template, IsNew(mine.Template, oldest)), mine.Captions, mine.Submitted, me.RerollsLeft),
            vote,
            room.Phase is Phase.RoundResults ? room.VoteOrder.OrderByDescending(e => e.Points).Select(e => Meme(e, true)).ToList() : null,
            room.Phase is Phase.Final ? room.Highlights.Select(e => Meme(e, true)).ToList() : null);
    }

    static bool IsDone(Room room, Player p) => room.Phase switch
    {
        Phase.Captioning => room.Entries.TryGetValue(p.Id, out var e) && e.Submitted,
        Phase.Voting => room.VoteIndex < room.VoteOrder.Count
            && (room.VoteOrder[room.VoteIndex].PlayerId == p.Id || room.VoteOrder[room.VoteIndex].Votes.ContainsKey(p.Id)),
        _ => false,
    };

    static string CleanName(string? name)
    {
        name = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0) name = "Joueur";
        return name.Length > 20 ? name[..20] : name;
    }
}

// ---------- Objets envoyés aux navigateurs ----------

public sealed class KlipyItem
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? Mp4 { get; set; }
    public string? Poster { get; set; }
    public string? Page { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed record TemplateDto(string Id, string Title, MediaKind Kind, string Source, string? MediaUrl, string? YouTubeId,
    string? ThumbnailUrl, string? PageUrl, string? Author, int Width, int Height, int BoxCount, bool IsNew)
{
    public static TemplateDto From(MemeTemplate t, bool isNew) => new(t.Id, t.Title, t.Kind, t.Source, t.MediaUrl, t.YouTubeId,
        t.ThumbnailUrl, t.PageUrl, t.Author, t.Width, t.Height, t.BoxCount, isNew);
}

public sealed record PlayerDto(string Id, string Name, int Color, int Score, bool Connected, bool Done);
public sealed record MemeDto(TemplateDto Template, List<Caption> Captions, string? AuthorId, int? Points);
public sealed record TaskDto(TemplateDto Template, List<Caption> Captions, bool Submitted, int RerollsLeft);
public sealed record VoteDto(int Index, int Count, MemeDto Meme, bool IsMine, int MyVote, int VotesIn);

public sealed record StateDto(
    string Code, string MeId, string HostId, Phase Phase, int Round, int TotalRounds,
    long? Deadline, long ServerNow, GameSettings Settings, int KlipyCount,
    List<PlayerDto> Players, TaskDto? Task, VoteDto? Vote, List<MemeDto>? RoundResults, List<MemeDto>? Highlights);
