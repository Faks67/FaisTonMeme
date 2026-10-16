using System.Net;
using System.Text.Json;

namespace FaisTonMeme.Web.Library;

/// <summary>
/// API Openverse (https://api.openverse.org, WordPress), sans clé : photos sous licence Creative Commons
/// (animaux drôles, têtes de réaction…). On ne demande que des licences qui autorisent la modification
/// (ajouter une légende en est une) et le contenu adulte est exclu. Chaque photo garde son auteur,
/// sa licence et un lien vers l'original, affichés dans le jeu.
/// Limites sans compte : 20 requêtes par minute et 200 par jour, 20 résultats par page, 12 pages par recherche.
/// </summary>
public sealed class OpenverseSource(HttpClient http, ILogger<OpenverseSource> log)
{
    public const string Name = "openverse";
    static readonly TimeSpan Pause = TimeSpan.FromSeconds(3.2); // reste sous 20 requêtes par minute

    public sealed record Result(List<MemeTemplate> Items, int Requests, string? StoppedBecause);

    public async Task<Result> FetchAsync(OpenverseOptions o, CancellationToken ct)
    {
        var byId = new Dictionary<string, MemeTemplate>();
        int requests = 0;
        foreach (var q in o.Queries.Where(q => !string.IsNullOrWhiteSpace(q)))
        {
            for (int page = 1; page <= Math.Clamp(o.PagesPerQuery, 1, 12); page++)
            {
                if (requests > 0) await Task.Delay(Pause, ct);
                var url = $"https://api.openverse.org/v1/images/?q={Uri.EscapeDataString(q)}"
                    + $"&license_type=modification&mature=false&page_size=20&page={page}";
                using var resp = await http.GetAsync(url, ct);
                requests++;
                if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                    return new(byId.Values.ToList(), requests, "limite de requêtes Openverse atteinte, la suite à la prochaine récolte");
                if (resp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
                    break; // plus de pages pour cette recherche
                resp.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var results = doc.RootElement.GetProperty("results");
                foreach (var r in results.EnumerateArray())
                    if (ToTemplate(r) is { } t) byId.TryAdd(t.Id, t);

                var pageCount = doc.RootElement.TryGetProperty("page_count", out var pc) ? pc.GetInt32() : 0;
                if (page >= pageCount || results.GetArrayLength() == 0) break;
            }
            log.LogInformation("Openverse « {Query} » : {Count} photos au total", q, byId.Count);
        }
        return new(byId.Values.ToList(), requests, null);
    }

    static MemeTemplate? ToTemplate(JsonElement r)
    {
        string? Str(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int Int(string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

        var id = Str("id");
        var url = Str("url");
        if (id is null || url is null || !url.StartsWith("https://")) return null;
        var type = (Str("filetype") ?? Path.GetExtension(new Uri(url).AbsolutePath).TrimStart('.')).ToLowerInvariant();
        if (type is not ("jpg" or "jpeg" or "png" or "gif" or "webp")) return null;

        var license = Str("license")?.ToUpperInvariant();
        var licenseText = license is null ? null
            : license is "CC0" or "PDM" ? (license == "CC0" ? "CC0" : "Domaine public")
            : $"CC {license} {Str("license_version")}".Trim();
        var title = (Str("title") ?? "").Trim();
        return new MemeTemplate
        {
            Id = $"{Name}:{id}",
            Title = title.Length == 0 ? "Photo sans titre" : title.Length > 80 ? title[..80] : title,
            Kind = type == "gif" ? MediaKind.Gif : MediaKind.Image,
            Source = Name,
            MediaUrl = url,
            ThumbnailUrl = Str("thumbnail") ?? url,
            PageUrl = Str("foreign_landing_url"),
            Author = Str("creator"),
            License = licenseText,
            LicenseUrl = Str("license_url"),
            Width = Int("width"),
            Height = Int("height"),
            BoxCount = 2,
        };
    }
}
