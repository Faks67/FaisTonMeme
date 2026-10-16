using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Memetropole.Web.Library;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubmissionStatus { Pending, Approved, Rejected }

/// <summary>Un modèle proposé par un joueur, en attente de modération.</summary>
public sealed class Submission
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public MediaKind Kind { get; set; }
    /// <summary>Fichier envoyé, rangé dans data/uploads.</summary>
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public string? YouTubeId { get; set; }
    public string? ExternalUrl { get; set; }
    public string SubmittedBy { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
    public SubmissionStatus Status { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public int BoxCount { get; set; } = 2;
}

/// <summary>
/// Propositions de modèles (data/submissions.json). Rien n'entre dans la bibliothèque
/// sans validation sur la page de modération.
/// </summary>
public sealed partial class SubmissionStore
{
    public const long MaxFileBytes = 15 * 1024 * 1024;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    readonly object _lock = new();
    readonly string _file;
    readonly DataPaths _paths;
    readonly LibraryStore _library;
    readonly List<Submission> _items;

    public SubmissionStore(DataPaths paths, LibraryStore library)
    {
        _paths = paths;
        _library = library;
        _file = Path.Combine(paths.Root, "submissions.json");
        _items = File.Exists(_file)
            ? JsonSerializer.Deserialize<List<Submission>>(File.ReadAllText(_file), Json) ?? []
            : [];
    }

    public IReadOnlyList<Submission> List(SubmissionStatus? status)
    {
        lock (_lock)
            return _items.Where(s => status is null || s.Status == status)
                         .OrderByDescending(s => s.SubmittedAt).ToList();
    }

    public Submission? Get(string id)
    {
        lock (_lock) return _items.FirstOrDefault(s => s.Id == id);
    }

    public int PendingCount()
    {
        lock (_lock) return _items.Count(s => s.Status == SubmissionStatus.Pending);
    }

    /// <summary>Enregistre un fichier envoyé, après avoir vérifié son vrai type (pas seulement l'extension).</summary>
    public async Task<(Submission? sub, string? error)> AddFileAsync(IFormFile file, string title, string by, int boxCount)
    {
        if (file.Length == 0) return (null, "Fichier vide.");
        if (file.Length > MaxFileBytes) return (null, "Fichier trop lourd (15 Mo maximum).");

        var head = new byte[16];
        await using (var s = file.OpenReadStream())
            _ = await s.ReadAsync(head);
        var type = Sniff(head);
        if (type is null) return (null, "Format non reconnu. Formats acceptés : JPG, PNG, GIF, WebP, MP4, WebM.");

        var sub = NewSubmission(title, by, type.Value.kind, boxCount);
        sub.FileName = sub.Id + type.Value.ext;
        sub.ContentType = type.Value.mime;
        await using (var dest = File.Create(Path.Combine(_paths.Uploads, sub.FileName)))
            await file.CopyToAsync(dest);
        Add(sub);
        return (sub, null);
    }

    /// <summary>Enregistre un lien : vidéo YouTube, ou image / vidéo en https.</summary>
    public (Submission? sub, string? error) AddLink(string url, string title, string by, int boxCount)
    {
        url = url.Trim();
        var yt = YouTubeIdRegex().Match(url);
        if (yt.Success)
        {
            var sub = NewSubmission(title, by, MediaKind.Video, 1);
            sub.YouTubeId = yt.Groups[1].Value;
            Add(sub);
            return (sub, null);
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return (null, "Le lien doit commencer par https://");
        var ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        MediaKind? kind = ext switch
        {
            ".jpg" or ".jpeg" or ".png" or ".webp" => MediaKind.Image,
            ".gif" => MediaKind.Gif,
            ".mp4" or ".webm" => MediaKind.Video,
            _ => null,
        };
        if (kind is null) return (null, "Lien non reconnu : donne un lien YouTube, ou un lien direct vers une image, un GIF ou une vidéo MP4/WebM.");
        var s2 = NewSubmission(title, by, kind.Value, boxCount);
        s2.ExternalUrl = uri.ToString();
        Add(s2);
        return (s2, null);
    }

    public Submission? Review(string id, bool approve)
    {
        lock (_lock)
        {
            var sub = _items.FirstOrDefault(s => s.Id == id);
            if (sub is null) return null;
            sub.Status = approve ? SubmissionStatus.Approved : SubmissionStatus.Rejected;
            sub.ReviewedAt = DateTimeOffset.UtcNow;
            if (approve) _library.Upsert([ToTemplate(sub)]);
            else
            {
                _library.RemoveWhere(t => t.Id == "communaute:" + sub.Id);
                if (sub.FileName is not null) File.Delete(Path.Combine(_paths.Uploads, sub.FileName));
                sub.FileName = null;
            }
            Save();
            return sub;
        }
    }

    public string? FilePath(Submission s) => s.FileName is null ? null : Path.Combine(_paths.Uploads, s.FileName);

    static MemeTemplate ToTemplate(Submission s) => new()
    {
        Id = "communaute:" + s.Id,
        Title = s.Title,
        Kind = s.Kind,
        Source = "communaute",
        MediaUrl = s.FileName is not null ? "/uploads/" + s.FileName : s.ExternalUrl,
        YouTubeId = s.YouTubeId,
        ThumbnailUrl = s.YouTubeId is not null ? $"https://i.ytimg.com/vi/{s.YouTubeId}/hqdefault.jpg"
                     : s.FileName is not null ? "/uploads/" + s.FileName : s.ExternalUrl,
        PageUrl = s.YouTubeId is not null ? $"https://www.youtube.com/watch?v={s.YouTubeId}" : null,
        Author = s.SubmittedBy,
        BoxCount = s.BoxCount,
        PublishedAt = s.SubmittedAt,
    };

    Submission NewSubmission(string title, string by, MediaKind kind, int boxCount) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..12],
        Title = Clean(title, 80, "Sans titre"),
        SubmittedBy = Clean(by, 24, "Anonyme"),
        SubmittedAt = DateTimeOffset.UtcNow,
        Kind = kind,
        Status = SubmissionStatus.Pending,
        BoxCount = kind == MediaKind.Image ? Math.Clamp(boxCount, 1, 4) : 1,
    };

    void Add(Submission s)
    {
        lock (_lock) { _items.Add(s); Save(); }
    }

    void Save()
    {
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items, Json));
        File.Move(tmp, _file, overwrite: true);
    }

    static string Clean(string? s, int max, string fallback)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return fallback;
        return s.Length > max ? s[..max] : s;
    }

    static (MediaKind kind, string ext, string mime)? Sniff(byte[] b)
    {
        if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return (MediaKind.Image, ".jpg", "image/jpeg");
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return (MediaKind.Image, ".png", "image/png");
        if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return (MediaKind.Gif, ".gif", "image/gif");
        if (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')
            return (MediaKind.Image, ".webp", "image/webp");
        if (b[4] == 'f' && b[5] == 't' && b[6] == 'y' && b[7] == 'p') return (MediaKind.Video, ".mp4", "video/mp4");
        if (b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3) return (MediaKind.Video, ".webm", "video/webm");
        return null;
    }

    [GeneratedRegex(@"(?:youtube\.com/(?:watch\?v=|shorts/|embed/)|youtu\.be/)([A-Za-z0-9_-]{11})")]
    private static partial Regex YouTubeIdRegex();
}
