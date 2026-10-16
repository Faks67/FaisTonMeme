using Microsoft.AspNetCore.SignalR;

namespace FaisTonMeme.Web.Game;

/// <summary>Point d'entrée temps réel (SignalR) des navigateurs : /hub/jeu.</summary>
public sealed class GameHub(RoomManager rooms) : Hub
{
    public sealed record Joined(string Code, string PlayerId, string Token);

    public async Task<Joined> CreateRoom(string name)
    {
        await LeaveCurrent();
        var (room, p) = rooms.Create(name, Context.ConnectionId);
        await rooms.BroadcastAsync(room);
        return new(room.Code, p.Id, p.Token);
    }

    public async Task<Joined> JoinRoom(string code, string name)
    {
        await LeaveCurrent();
        var (room, p) = rooms.Join(code, name, Context.ConnectionId);
        await rooms.BroadcastAsync(room);
        return new(room.Code, p.Id, p.Token);
    }

    public async Task<Joined> Rejoin(string code, string playerId, string token)
    {
        var (room, p) = rooms.Rejoin(code, playerId, token, Context.ConnectionId);
        await rooms.BroadcastAsync(room);
        return new(room.Code, p.Id, p.Token);
    }

    public async Task Leave()
    {
        await LeaveCurrent();
    }

    public Task UpdateSettings(GameSettings settings) => Act((r, p) => rooms.UpdateSettings(r, p, settings));
    public Task SetKlipyPool(List<KlipyItem> items) => Act((r, p) => rooms.SetKlipyPool(r, p, items ?? []));
    public Task StartGame() => Act(rooms.Start);
    public Task BackToLobby() => Act(rooms.BackToLobby);
    public Task Reroll() => Act(rooms.Reroll);
    public Task Vote(int index, int score) => Act((r, p) => rooms.Vote(r, p, index, score));

    public async Task SaveCaptions(List<Caption> captions, bool final)
    {
        var (room, p) = Me();
        if (rooms.SaveCaptions(room, p, captions ?? [], final))
            await rooms.BroadcastAsync(room);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var room = rooms.Disconnected(Context.ConnectionId);
        if (room is not null) await rooms.BroadcastAsync(room);
        await base.OnDisconnectedAsync(exception);
    }

    async Task Act(Action<Room, Player> action)
    {
        var (room, p) = Me();
        action(room, p);
        await rooms.BroadcastAsync(room);
    }

    (Room, Player) Me() => rooms.Who(Context.ConnectionId) ?? throw new HubException("Tu n'es dans aucune salle.");

    async Task LeaveCurrent()
    {
        var room = rooms.Leave(Context.ConnectionId);
        if (room is not null) await rooms.BroadcastAsync(room);
    }
}

/// <summary>
/// Les HubException sont des refus normaux (« salle pleine »…) affichés au joueur : inutile de les
/// journaliser comme des erreurs. Les vraies erreurs sont journalisées ici, car le journal par
/// défaut de SignalR est coupé dans appsettings.json.
/// </summary>
public sealed class HubErrorFilter(ILogger<HubErrorFilter> log) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext ctx, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try { return await next(ctx); }
        catch (Exception ex) when (ex is not HubException)
        {
            log.LogError(ex, "Erreur dans la méthode {Method}", ctx.HubMethodName);
            throw;
        }
    }
}

/// <summary>Fait tourner les chronomètres des salles.</summary>
public sealed class GameLoop(RoomManager rooms, ILogger<GameLoop> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await rooms.TickAsync(); }
            catch (Exception ex) { log.LogError(ex, "Erreur dans la boucle de jeu"); }
        }
    }
}
