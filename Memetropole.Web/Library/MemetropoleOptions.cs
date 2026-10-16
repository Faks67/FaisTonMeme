namespace Memetropole.Web.Library;

/// <summary>Section « Memetropole » de appsettings.json (ou appsettings.Local.json).</summary>
public sealed class MemetropoleOptions
{
    public string DataFolder { get; set; } = "data";
    public double RefreshIntervalHours { get; set; } = 6;
    public string AdminPassword { get; set; } = "";
    public ImgflipOptions Imgflip { get; set; } = new();
    public YouTubeOptions YouTube { get; set; } = new();
    public KlipyOptions Klipy { get; set; } = new();
}

public sealed class ImgflipOptions
{
    public bool Enabled { get; set; } = true;
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
