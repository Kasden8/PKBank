using System.Text.Json;
using System.Text.Json.Serialization;
using PKHeX.Core;

namespace PKBank.App.Services;

/// <summary>One save the user has opened, remembered by where it lives on disk.</summary>
public sealed record RegisteredSave(
    string Path,
    string GameLabel,
    int Generation,
    string TrainerName,
    int BoxCount,
    DateTimeOffset LastOpened)
{
    [JsonIgnore]
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>The file is still where it was when it was opened.</summary>
    [JsonIgnore]
    public bool Exists
    {
        get
        {
            try { return File.Exists(Path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
    }
}

/// <summary>
/// Remembers the saves the user opened, so the editor can offer them again without a file
/// dialog. Entries keep the save's location plus the identity read at open time (game,
/// trainer, generation, box count) and the last time it was opened. Newest first.
/// </summary>
public sealed class SaveRegistry
{
    private const string Key = "registered_saves";
    private const int MaxEntries = 60;

    /// <summary>The app-wide registry, backed by <see cref="SettingsStore"/>.</summary>
    public static SaveRegistry Default { get; } = new();

    private readonly Func<string?> _read;
    private readonly Action<string?> _write;
    private readonly Lock _gate = new();
    private List<RegisteredSave> _entries;

    public SaveRegistry()
        : this(() => SettingsStore.Get(Key, (string?)null), raw => SettingsStore.Set(Key, raw))
    {
    }

    /// <summary>Builds a registry over an arbitrary store (tests, alternate profiles).</summary>
    public SaveRegistry(Func<string?> read, Action<string?> write)
    {
        _read = read;
        _write = write;
        _entries = Deserialize(read());
    }

    /// <summary>Every registered save, most recently opened first.</summary>
    public IReadOnlyList<RegisteredSave> GetAll()
    {
        lock (_gate)
            return _entries.OrderByDescending(entry => entry.LastOpened).ToArray();
    }

    /// <summary>Registers (or refreshes) the save at <paramref name="path"/>.</summary>
    public RegisteredSave Register(string path, string gameLabel, int generation, string trainerName, int boxCount, DateTimeOffset? when = null)
    {
        var fullPath = Normalize(path);
        var entry = new RegisteredSave(fullPath, gameLabel, generation, trainerName, boxCount, when ?? DateTimeOffset.Now);
        lock (_gate)
        {
            _entries.RemoveAll(existing => SamePath(existing.Path, fullPath));
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries = _entries.OrderByDescending(item => item.LastOpened).Take(MaxEntries).ToList();
            Persist();
        }
        return entry;
    }

    /// <summary>Registers a freshly parsed save using the identity it carries.</summary>
    public RegisteredSave Register(string path, SaveFile save, DateTimeOffset? when = null)
    {
        var label = GameInfo.GetVersionName(save.Version);
        if (string.IsNullOrWhiteSpace(label)) label = save.Context.ToString();
        return Register(path, label, save.Generation, save.OT, save.BoxCount, when);
    }

    /// <summary>Forgets the save at <paramref name="path"/>. Returns true when it was listed.</summary>
    public bool Remove(string path)
    {
        var fullPath = Normalize(path);
        lock (_gate)
        {
            if (_entries.RemoveAll(entry => SamePath(entry.Path, fullPath)) == 0) return false;
            Persist();
            return true;
        }
    }

    /// <summary>Absolute path used as the entry's identity (case-insensitive on Windows).</summary>
    public static string Normalize(string path)
    {
        try { return System.IO.Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private void Persist() =>
        _write(_entries.Count == 0 ? null : JsonSerializer.Serialize(_entries));

    private static List<RegisteredSave> Deserialize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<RegisteredSave>>(raw) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
