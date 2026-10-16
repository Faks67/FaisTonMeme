using System.Text.Json.Serialization;

namespace FaisTonMeme.Web.Library;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MediaKind { Image, Gif, Video }

/// <summary>Un modèle de mème à légender : image, GIF ou vidéo courte.</summary>
public sealed class MemeTemplate
{
    /// <summary>Identifiant préfixé par la source, ex. « imgflip:181913649 » ou « youtube:dQw4w9WgXcQ ».</summary>
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public MediaKind Kind { get; set; }
    /// <summary>imgflip, memegen, openverse, youtube, klipy ou communaute.</summary>
    public string Source { get; set; } = "";
    /// <summary>URL de l'image, du GIF ou du MP4 (absent pour YouTube, lu par le lecteur intégré).</summary>
    public string? MediaUrl { get; set; }
    public string? YouTubeId { get; set; }
    public string? ThumbnailUrl { get; set; }
    /// <summary>Page d'origine, affichée en crédit.</summary>
    public string? PageUrl { get; set; }
    public string? Author { get; set; }
    /// <summary>Licence de l'image (Openverse), à afficher avec l'auteur.</summary>
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Nombre de zones de texte proposées (images seulement).</summary>
    public int BoxCount { get; set; } = 2;
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    /// <summary>Position dans le classement de la source au dernier rafraîchissement (1 = plus populaire).</summary>
    public int? Rank { get; set; }
}

public sealed record SourceStatus(string Source, DateTimeOffset? LastRun, bool Ok, int Count, string Message);
