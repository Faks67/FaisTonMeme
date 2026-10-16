using System.Text.Json;

namespace Memetropole.Web.Library;

/// <summary>
/// Bibliothèque de modèles, gardée en mémoire et enregistrée dans data/library.json.
/// Au premier lancement, elle est remplie avec Seed/templates.json pour que le jeu marche
/// même si aucune source n'a encore répondu.
/// </summary>
public sealed class LibraryStore
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    readonly object _lock = new();
    readonly string _file;
    readonly string _blockedFile;
    readonly HashSet<string> _blocked;
    readonly ILogger<LibraryStore> _log;
    Dictionary<string, MemeTemplate> _items = new();
    readonly Dictionary<string, SourceStatus> _status = new();

    public LibraryStore(DataPaths paths, IWebHostEnvironment env, ILogger<LibraryStore> log)
    {
        _log = log;
        _file = Path.Combine(paths.Root, "library.json");
        _blockedFile = Path.Combine(paths.Root, "blocked.json");
        _blocked = File.Exists(_blockedFile)
            ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(_blockedFile), Json) ?? []
            : [];
        if (File.Exists(_file))
        {
            var list = JsonSerializer.Deserialize<List<MemeTemplate>>(File.ReadAllText(_file), Json) ?? [];
            _items = list.ToDictionary(t => t.Id);
        }
        if (_items.Count == 0)
        {
            var seed = Path.Combine(env.ContentRootPath, "Seed", "templates.json");
            var now = DateTimeOffset.UtcNow;
            foreach (var t in JsonSerializer.Deserialize<List<MemeTemplate>>(File.ReadAllText(seed), Json) ?? [])
            {
                t.FirstSeen = t.LastSeen = now;
                _items[t.Id] = t;
            }
            Save();
        }
        _log.LogInformation("Bibliothèque chargée : {Count} modèles", _items.Count);
    }

    public IReadOnlyList<MemeTemplate> All()
    {
        lock (_lock) return _items.Values.ToList();
    }

    /// <summary>Date du tout premier remplissage, pour ne pas marquer toute la bibliothèque « nouveau ».</summary>
    public DateTimeOffset OldestFirstSeen
    {
        get { lock (_lock) return _items.Count == 0 ? DateTimeOffset.UtcNow : _items.Values.Min(t => t.FirstSeen); }
    }

    public MemeTemplate? Get(string id)
    {
        lock (_lock) return _items.GetValueOrDefault(id);
    }

    /// <summary>Ajoute ou met à jour les modèles d'une source, en gardant la date de première apparition.</summary>
    public int Upsert(IEnumerable<MemeTemplate> fresh)
    {
        int added = 0;
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var t in fresh)
            {
                if (_blocked.Contains(t.Id)) continue;
                if (_items.TryGetValue(t.Id, out var old)) t.FirstSeen = old.FirstSeen;
                else { t.FirstSeen = now; added++; }
                t.LastSeen = now;
                _items[t.Id] = t;
            }
            Save();
        }
        return added;
    }

    /// <summary>Le classement n'a de sens que pour la dernière récupération : on l'efface avant d'en appliquer un nouveau.</summary>
    public void ClearRanks(string source)
    {
        lock (_lock)
            foreach (var t in _items.Values.Where(t => t.Source == source)) t.Rank = null;
    }

    public int RemoveWhere(Func<MemeTemplate, bool> predicate)
    {
        lock (_lock)
        {
            var ids = _items.Values.Where(predicate).Select(t => t.Id).ToList();
            foreach (var id in ids) _items.Remove(id);
            if (ids.Count > 0) Save();
            return ids.Count;
        }
    }

    /// <summary>Retire un modèle et empêche la tâche planifiée de le remettre (modération).</summary>
    public bool Block(string id)
    {
        lock (_lock)
        {
            _blocked.Add(id);
            File.WriteAllText(_blockedFile, JsonSerializer.Serialize(_blocked, Json));
            var removed = _items.Remove(id);
            if (removed) Save();
            return removed;
        }
    }

    public void SetStatus(SourceStatus s)
    {
        lock (_lock) _status[s.Source] = s;
    }

    public IReadOnlyList<SourceStatus> Statuses()
    {
        lock (_lock) return _status.Values.OrderBy(s => s.Source).ToList();
    }

    void Save()
    {
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items.Values.OrderBy(t => t.Source).ThenBy(t => t.Rank ?? int.MaxValue), Json));
        File.Move(tmp, _file, overwrite: true);
    }
}

/// <summary>Dossier de données (bibliothèque, propositions, fichiers envoyés).</summary>
public sealed class DataPaths
{
    public string Root { get; }
    public string Uploads { get; }

    public DataPaths(string root)
    {
        Root = root;
        Uploads = Path.Combine(root, "uploads");
        Directory.CreateDirectory(Uploads);
    }
}
