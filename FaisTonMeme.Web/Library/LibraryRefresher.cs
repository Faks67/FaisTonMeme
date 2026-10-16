using Microsoft.Extensions.Options;

namespace FaisTonMeme.Web.Library;

/// <summary>
/// Tâche planifiée : au démarrage puis toutes les RefreshIntervalHours heures, récupère
/// les modèles des sources officielles configurées et met la bibliothèque à jour.
/// </summary>
public sealed class LibraryRefresher(
    LibraryStore store,
    ImgflipSource imgflip,
    MemegenSource memegen,
    OpenverseSource openverse,
    YouTubeSource youtube,
    IOptionsMonitor<FaisTonMemeOptions> options,
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

            if (o.Memegen.Enabled)
                await RunSource(MemegenSource.Name, async () => WithoutImgflipDuplicates(await memegen.FetchAsync(ct)));
            else
                store.SetStatus(new(MemegenSource.Name, null, false, 0, "Désactivée dans la configuration"));

            if (!string.IsNullOrWhiteSpace(o.YouTube.ApiKey))
                await RunSource(YouTubeSource.Name, () => youtube.FetchAsync(o.YouTube, ct));
            else
                store.SetStatus(new(YouTubeSource.Name, null, false, 0, "Aucune clé API : renseigne FaisTonMeme:YouTube:ApiKey"));

            var purged = store.RemoveWhere(t => t.Source == YouTubeSource.Name && DateTimeOffset.UtcNow - t.LastSeen > YouTubeRetention);
            if (purged > 0) log.LogInformation("{Count} vidéos YouTube de plus de 30 jours retirées", purged);

            // En dernier : la récolte Openverse est lente (pause entre les requêtes) et limitée à une par jour.
            if (!o.Openverse.Enabled)
                store.SetStatus(new(OpenverseSource.Name, null, false, 0, "Désactivée dans la configuration"));
            else if (LastOpenverseHarvest() is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(o.Openverse.RefreshHours))
                store.SetStatus(new(OpenverseSource.Name, last, true, store.All().Count(t => t.Source == OpenverseSource.Name),
                    $"Dernière récolte le {last.ToLocalTime():dd/MM à HH:mm}, la prochaine dans moins de {o.Openverse.RefreshHours:0} h"));
            else
                await RunOpenverse(o.Openverse, ct);

            LastRun = DateTimeOffset.UtcNow;
        }
        finally { _gate.Release(); }
    }

    DateTimeOffset? LastOpenverseHarvest()
    {
        var seen = store.All().Where(t => t.Source == OpenverseSource.Name).Select(t => t.LastSeen).ToList();
        return seen.Count == 0 ? null : seen.Max();
    }

    async Task RunOpenverse(OpenverseOptions o, CancellationToken ct)
    {
        store.SetStatus(new(OpenverseSource.Name, null, true, 0, "Récolte en cours (quelques minutes)…"));
        try
        {
            var result = await openverse.FetchAsync(o, ct);
            var added = store.Upsert(result.Items);
            var total = store.All().Count(t => t.Source == OpenverseSource.Name);
            var msg = $"{result.Items.Count} récupérées, dont {added} nouvelles ({total} au total)";
            if (result.StoppedBecause is not null) msg += " ; " + result.StoppedBecause;
            store.SetStatus(new(OpenverseSource.Name, DateTimeOffset.UtcNow, true, result.Items.Count, msg));
            log.LogInformation("Source openverse : {Message} en {Requests} requêtes", msg, result.Requests);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            store.SetStatus(new(OpenverseSource.Name, DateTimeOffset.UtcNow, false, 0, ex.Message));
            log.LogWarning("Source openverse en échec : {Message}", ex.Message);
        }
    }

    /// <summary>memegen et Imgflip ont beaucoup de modèles en commun : on garde la version Imgflip.</summary>
    List<MemeTemplate> WithoutImgflipDuplicates(List<MemeTemplate> items)
    {
        static string Key(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var imgflipNames = store.All().Where(t => t.Source == ImgflipSource.Name).Select(t => Key(t.Title)).ToHashSet();
        return items.Where(t => !imgflipNames.Contains(Key(t.Title))).ToList();
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
