using Microsoft.Extensions.Options;

namespace Memetropole.Web.Library;

/// <summary>
/// Tâche planifiée : au démarrage puis toutes les RefreshIntervalHours heures, récupère
/// les modèles des sources officielles configurées et met la bibliothèque à jour.
/// </summary>
public sealed class LibraryRefresher(
    LibraryStore store,
    ImgflipSource imgflip,
    YouTubeSource youtube,
    IOptionsMonitor<MemetropoleOptions> options,
    ILogger<LibraryRefresher> log) : BackgroundService
{
    /// <summary>Les données de l'API YouTube ne doivent pas être conservées plus de 30 jours.</summary>
    static readonly TimeSpan YouTubeRetention = TimeSpan.FromDays(30);

    readonly SemaphoreSlim _gate = new(1, 1);
    public DateTimeOffset? LastRun { get; private set; }
    public DateTimeOffset? NextRun { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);
            var every = TimeSpan.FromHours(Math.Max(0.1, options.CurrentValue.RefreshIntervalHours));
            NextRun = DateTimeOffset.UtcNow + every;
            await Task.Delay(every, stoppingToken);
        }
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) return; // un rafraîchissement est déjà en cours
        try
        {
            var o = options.CurrentValue;

            if (o.Imgflip.Enabled)
                await RunSource(ImgflipSource.Name, () => imgflip.FetchAsync(ct));
            else
                store.SetStatus(new(ImgflipSource.Name, null, false, 0, "Désactivée dans la configuration"));

            if (!string.IsNullOrWhiteSpace(o.YouTube.ApiKey))
                await RunSource(YouTubeSource.Name, () => youtube.FetchAsync(o.YouTube, ct));
            else
                store.SetStatus(new(YouTubeSource.Name, null, false, 0, "Aucune clé API : renseigne Memetropole:YouTube:ApiKey"));

            var purged = store.RemoveWhere(t => t.Source == YouTubeSource.Name && DateTimeOffset.UtcNow - t.LastSeen > YouTubeRetention);
            if (purged > 0) log.LogInformation("{Count} vidéos YouTube de plus de 30 jours retirées", purged);
            LastRun = DateTimeOffset.UtcNow;
        }
        finally { _gate.Release(); }
    }

    async Task RunSource(string name, Func<Task<List<MemeTemplate>>> fetch)
    {
        try
        {
            var items = await fetch();
            store.ClearRanks(name);
            var added = store.Upsert(items);
            store.SetStatus(new(name, DateTimeOffset.UtcNow, true, items.Count, $"{items.Count} récupérés, dont {added} nouveaux"));
            log.LogInformation("Source {Source} : {Count} modèles, {Added} nouveaux", name, items.Count, added);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            store.SetStatus(new(name, DateTimeOffset.UtcNow, false, 0, ex.Message));
            log.LogWarning("Source {Source} en échec : {Message}", name, ex.Message);
        }
    }
}
