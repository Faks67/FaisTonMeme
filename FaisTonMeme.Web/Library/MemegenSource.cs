using System.Text.Json;

namespace FaisTonMeme.Web.Library;

/// <summary>
/// API libre et gratuite de memegen.link (https://api.memegen.link, projet open source), sans clé :
/// environ 200 modèles classiques, fournis vierges.
/// </summary>
public sealed class MemegenSource(HttpClient http)
{
    public const string Name = "memegen";

    public async Task<List<MemeTemplate>> FetchAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.memegen.link/templates/", ct));
        var list = new List<MemeTemplate>();
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            var id = m.GetProperty("id").GetString();
            var blank = m.GetProperty("blank").GetString();
            if (string.IsNullOrEmpty(id) || blank is null || !blank.StartsWith("https://api.memegen.link/")) continue;
            var lines = m.TryGetProperty("lines", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : 2;
            list.Add(new MemeTemplate
            {
                Id = $"{Name}:{id}",
                Title = m.GetProperty("name").GetString() ?? id,
                Kind = blank.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? MediaKind.Gif : MediaKind.Image,
                Source = Name,
                MediaUrl = blank,
                ThumbnailUrl = blank,
                PageUrl = m.TryGetProperty("source", out var s) && s.GetString() is { Length: > 0 } src ? src : $"https://api.memegen.link/templates/{id}",
                BoxCount = Math.Clamp(lines, 1, 6),
            });
        }
        return list;
    }
}
