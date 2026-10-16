using System.Text.Json;

namespace FaisTonMeme.Web.Library;

/// <summary>
/// API publique d'Imgflip (https://imgflip.com/api), sans clé : les ~100 modèles les plus
/// légendés en ce moment. Le classement évolue avec les tendances.
/// </summary>
public sealed class ImgflipSource(HttpClient http)
{
    public const string Name = "imgflip";

    public async Task<List<MemeTemplate>> FetchAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.imgflip.com/get_memes", ct));
        var root = doc.RootElement;
        if (!root.GetProperty("success").GetBoolean())
            throw new InvalidOperationException("Imgflip a répondu success=false");

        var list = new List<MemeTemplate>();
        int rank = 0;
        foreach (var m in root.GetProperty("data").GetProperty("memes").EnumerateArray())
        {
            rank++;
            var url = m.GetProperty("url").GetString() ?? "";
            if (!url.StartsWith("https://i.imgflip.com/")) continue;
            var id = m.GetProperty("id").GetString();
            list.Add(new MemeTemplate
            {
                Id = $"{Name}:{id}",
                Title = m.GetProperty("name").GetString() ?? "",
                Kind = url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? MediaKind.Gif : MediaKind.Image,
                Source = Name,
                MediaUrl = url,
                ThumbnailUrl = url,
                PageUrl = $"https://imgflip.com/memegenerator/{id}",
                Width = m.GetProperty("width").GetInt32(),
                Height = m.GetProperty("height").GetInt32(),
                BoxCount = Math.Clamp(m.GetProperty("box_count").GetInt32(), 1, 6),
                Rank = rank,
            });
        }
        return list;
    }
}
