using System.Net;
using System.Text.Json;

namespace FaisTonMeme.Web.Library;

/// <summary>
/// API YouTube Data v3 (clé gratuite Google Cloud) : vidéos courtes récentes et intégrables
/// correspondant aux recherches configurées, triées par vues, avec SafeSearch strict.
/// Chaque recherche coûte 100 unités sur un quota gratuit de 10 000 par jour.
/// Les vidéos sont lues avec le lecteur YouTube intégré, jamais téléchargées.
/// </summary>
public sealed class YouTubeSource(HttpClient http)
{
    public const string Name = "youtube";

    public async Task<List<MemeTemplate>> FetchAsync(YouTubeOptions o, CancellationToken ct)
    {
        var after = DateTime.UtcNow.AddDays(-Math.Max(1, o.MaxAgeDays)).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var byId = new Dictionary<string, MemeTemplate>();
        int rank = 0;
        foreach (var q in o.Queries.Where(q => !string.IsNullOrWhiteSpace(q)))
        {
            var url = "https://www.googleapis.com/youtube/v3/search?part=snippet&type=video"
                + "&videoDuration=short&videoEmbeddable=true&safeSearch=strict&order=viewCount"
                + $"&publishedAfter={after}&regionCode={Uri.EscapeDataString(o.RegionCode)}"
                + $"&relevanceLanguage={Uri.EscapeDataString(o.Language)}"
                + $"&maxResults={Math.Clamp(o.MaxResultsPerQuery, 1, 50)}"
                + $"&q={Uri.EscapeDataString(q)}&key={Uri.EscapeDataString(o.ApiKey)}";

            using var resp = await http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"YouTube a répondu {(int)resp.StatusCode} : {ExtractError(body)}");

            using var doc = JsonDocument.Parse(body);
            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                rank++;
                var videoId = item.GetProperty("id").GetProperty("videoId").GetString();
                if (string.IsNullOrEmpty(videoId) || byId.ContainsKey(videoId)) continue;
                var s = item.GetProperty("snippet");
                var thumbs = s.GetProperty("thumbnails");
                var thumb = thumbs.TryGetProperty("high", out var h) ? h : thumbs.GetProperty("default");
                byId[videoId] = new MemeTemplate
                {
                    Id = $"{Name}:{videoId}",
                    Title = WebUtility.HtmlDecode(s.GetProperty("title").GetString() ?? ""),
                    Kind = MediaKind.Video,
                    Source = Name,
                    YouTubeId = videoId,
                    ThumbnailUrl = thumb.GetProperty("url").GetString(),
                    PageUrl = $"https://www.youtube.com/shorts/{videoId}",
                    Author = WebUtility.HtmlDecode(s.GetProperty("channelTitle").GetString() ?? ""),
                    PublishedAt = s.TryGetProperty("publishedAt", out var p) && p.TryGetDateTimeOffset(out var d) ? d : null,
                    Width = 9,
                    Height = 16,
                    BoxCount = 1,
                    Rank = rank,
                };
            }
        }
        return byId.Values.ToList();
    }

    static string ExtractError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? body;
        }
        catch { return body.Length > 200 ? body[..200] : body; }
    }
}
