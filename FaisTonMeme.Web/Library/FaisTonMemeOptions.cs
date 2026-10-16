namespace FaisTonMeme.Web.Library;

/// <summary>Section « FaisTonMeme » de appsettings.json (ou appsettings.Local.json).</summary>
public sealed class FaisTonMemeOptions
{
    public string DataFolder { get; set; } = "data";
    public double RefreshIntervalHours { get; set; } = 6;
    public string AdminPassword { get; set; } = "";
    public ImgflipOptions Imgflip { get; set; } = new();
    public MemegenOptions Memegen { get; set; } = new();
    public OpenverseOptions Openverse { get; set; } = new();
    public YouTubeOptions YouTube { get; set; } = new();
    public KlipyOptions Klipy { get; set; } = new();
}

public sealed class ImgflipOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class MemegenOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class OpenverseOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Recherches (en anglais : les mots-clés des photos sont surtout en anglais).</summary>
    public string[] Queries { get; set; } = ["funny cat"];
    /// <summary>Sans compte, Openverse donne au plus 12 pages de 20 résultats par recherche.</summary>
    public int PagesPerQuery { get; set; } = 12;
    /// <summary>Sans compte, Openverse limite à 200 requêtes par jour : une récolte complète par jour au plus.</summary>
    public double RefreshHours { get; set; } = 24;
}

public sealed class YouTubeOptions
{
    public string ApiKey { get; set; } = "";
    public string[] Queries { get; set; } = ["meme #shorts"];
    public string RegionCode { get; set; } = "FR";
    public string Language { get; set; } = "fr";
    public int MaxAgeDays { get; set; } = 7;
    public int MaxResultsPerQuery { get; set; } = 25;
}

public sealed class KlipyOptions
{
    public string ApiKey { get; set; } = "";
    public string Locale { get; set; } = "fr";
    public string ContentFilter { get; set; } = "high";
}
