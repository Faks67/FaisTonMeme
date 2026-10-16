using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using FaisTonMeme.Web.Game;
using FaisTonMeme.Web.Library;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Les clés d'API vont dans appsettings.Local.json (ignoré par Git) ou dans des variables
// d'environnement, par ex. FaisTonMeme__YouTube__ApiKey.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<FaisTonMemeOptions>(builder.Configuration.GetSection("FaisTonMeme"));
var options = builder.Configuration.GetSection("FaisTonMeme").Get<FaisTonMemeOptions>() ?? new();

var dataPaths = new DataPaths(Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, options.DataFolder)));
builder.Services.AddSingleton(dataPaths);
builder.Services.AddSingleton<LibraryStore>();
builder.Services.AddSingleton<SubmissionStore>();
builder.Services.AddHttpClient<ImgflipSource>(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("FaisTonMeme/1.0"));
builder.Services.AddHttpClient<MemegenSource>(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("FaisTonMeme/1.0"));
builder.Services.AddHttpClient<OpenverseSource>(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("FaisTonMeme/1.0 (jeu non commercial)"));
builder.Services.AddHttpClient<YouTubeSource>(c => c.DefaultRequestHeaders.UserAgent.ParseAdd("FaisTonMeme/1.0"));
builder.Services.AddSingleton<LibraryRefresher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryRefresher>());

builder.Services.AddSignalR(o => o.AddFilter<HubErrorFilter>());
builder.Services.AddSingleton<RoomManager>();
builder.Services.AddHostedService<GameLoop>();

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = SubmissionStore.MaxFileBytes + 64 * 1024);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // 5 propositions toutes les 10 minutes par adresse IP
    o.AddPolicy("submissions", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10) }));
});

var app = builder.Build();
var log = app.Logger;

// Mot de passe de modération : celui de la config, sinon généré une fois et gardé dans data/.
var adminPassword = options.AdminPassword;
if (string.IsNullOrWhiteSpace(adminPassword))
{
    var file = Path.Combine(dataPaths.Root, "mot-de-passe-moderation.txt");
    if (!File.Exists(file)) File.WriteAllText(file, Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant());
    adminPassword = File.ReadAllText(file).Trim();
    log.LogInformation("Mot de passe de modération (dans {File}) : {Password}", file, adminPassword);
}

bool IsAdmin(HttpContext ctx) =>
    ctx.Request.Headers.TryGetValue("X-Admin-Password", out var given)
    && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given.ToString()), Encoding.UTF8.GetBytes(adminPassword));

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

// ---------- Configuration pour le navigateur ----------

app.MapGet("/api/config", (IOptionsMonitor<FaisTonMemeOptions> o) => new
{
    // KLIPY exige que ses API soient appelées depuis le navigateur : la clé est donc publique par nature.
    klipy = string.IsNullOrWhiteSpace(o.CurrentValue.Klipy.ApiKey) ? null : new
    {
        key = o.CurrentValue.Klipy.ApiKey,
        locale = o.CurrentValue.Klipy.Locale,
        contentFilter = o.CurrentValue.Klipy.ContentFilter,
    },
    youtube = !string.IsNullOrWhiteSpace(o.CurrentValue.YouTube.ApiKey),
});

// ---------- Bibliothèque ----------

app.MapGet("/api/library", (LibraryStore store, string? kind, string? source, string? q) =>
{
    var oldest = store.OldestFirstSeen;
    return store.All()
        .Where(t => kind is null or "" || t.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase))
        .Where(t => source is null or "" || t.Source == source)
        .Where(t => q is null or "" || t.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
        .Select(t => new { template = TemplateDto.From(t, RoomManager.IsNew(t, oldest)), t.FirstSeen, t.PublishedAt, t.Rank })
        .OrderByDescending(x => x.template.IsNew)
        .ThenByDescending(x => x.PublishedAt ?? x.FirstSeen)
        .ThenBy(x => x.Rank ?? int.MaxValue);
});

app.MapGet("/api/library/status", (LibraryStore store, LibraryRefresher refresher, SubmissionStore subs, RoomManager rooms) =>
{
    var all = store.All();
    return new
    {
        total = all.Count,
        bySource = all.GroupBy(t => t.Source).ToDictionary(g => g.Key, g => g.Count()),
        byKind = all.GroupBy(t => t.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
        sources = store.Statuses(),
        lastRun = refresher.LastRun,
        nextRun = refresher.NextRun,
        pending = subs.PendingCount(),
        rooms = rooms.RoomCount,
    };
});

// ---------- Propositions des joueurs ----------

app.MapPost("/api/submissions", async (HttpRequest req, SubmissionStore subs) =>
{
    if (!req.HasFormContentType) return Results.BadRequest(new { error = "Formulaire attendu." });
    var form = await req.ReadFormAsync();
    var title = form["title"].ToString();
    var by = form["name"].ToString();
    _ = int.TryParse(form["boxCount"], out var boxes);
    boxes = boxes == 0 ? 2 : boxes;

    var (sub, error) = form.Files.GetFile("file") is { } file
        ? await subs.AddFileAsync(file, title, by, boxes)
        : !string.IsNullOrWhiteSpace(form["url"])
            ? subs.AddLink(form["url"]!, title, by, boxes)
            : (null, "Ajoute un fichier ou un lien.");
    return sub is null ? Results.BadRequest(new { error }) : Results.Ok(new { id = sub.Id });
}).RequireRateLimiting("submissions").DisableAntiforgery();

// Fichiers envoyés : visibles par tous seulement une fois validés.
app.MapGet("/uploads/{file}", (string file, SubmissionStore subs, HttpContext ctx) =>
{
    var id = Path.GetFileNameWithoutExtension(file);
    var sub = subs.Get(id);
    if (sub is null || sub.FileName != file || sub.Status != SubmissionStatus.Approved) return Results.NotFound();
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    return Results.File(subs.FilePath(sub)!, sub.ContentType, enableRangeProcessing: true);
});

// ---------- Modération (en-tête X-Admin-Password) ----------

var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
    IsAdmin(ctx.HttpContext) ? await next(ctx) : Results.Json(new { error = "Mot de passe incorrect." }, statusCode: 401));

admin.MapGet("/check", () => Results.Ok());

admin.MapGet("/submissions", (SubmissionStore subs, SubmissionStatus? status) => subs.List(status));

admin.MapGet("/submissions/{id}/file", (string id, SubmissionStore subs) =>
    subs.Get(id) is { FileName: not null } s ? Results.File(subs.FilePath(s)!, s.ContentType) : Results.NotFound());

admin.MapPost("/submissions/{id}/approve", (string id, SubmissionStore subs) =>
    subs.Review(id, true) is { } s ? Results.Ok(s) : Results.NotFound());

admin.MapPost("/submissions/{id}/reject", (string id, SubmissionStore subs) =>
    subs.Review(id, false) is { } s ? Results.Ok(s) : Results.NotFound());

admin.MapDelete("/library/{id}", (string id, LibraryStore store) =>
    store.Block(id) ? Results.Ok() : Results.NotFound());

admin.MapPost("/refresh", async (LibraryRefresher refresher, LibraryStore store) =>
{
    await refresher.RefreshAsync(CancellationToken.None);
    return Results.Ok(store.Statuses());
});

app.MapHub<GameHub>("/hub/jeu");

app.Run();
