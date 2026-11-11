using System.Text.Json;
using PKForge.Domain;

namespace PKBank.App.Services.Pokepark;

/// <summary>One park resident: the identity as recorded, with the provenance of its real data
/// (the Bank entry or the save slot it came from).</summary>
public sealed record ParkPokemon(string Id, int Species, int Form, bool Shiny, string Name, string Source,
    string GameName = "", string TrainerName = "", string Origin = "");

/// <summary>Where the park may take its residents from.</summary>
public enum PokeparkSource { Both, Bank, Save, Disabled }

/// <summary>The park's settings, as the user set them.</summary>
public sealed record PokeparkSettings
{
    public PokeparkSource Source { get; init; } = PokeparkSource.Both;
    public bool Random { get; init; } = true;
    public int Count { get; init; } = 6;
    public string[] SelectedIds { get; init; } = [];
    /// <summary>The daycare writes EXP and eggs back to saves and the Bank, where the game allows it.</summary>
    public bool DaycareWritesBack { get; init; } = true;
}

/// <summary>A resident left at the daycare: when it arrived and how much EXP it has grown.</summary>
public sealed record ParkStay(string Id, DateTimeOffset Since, uint Exp);

/// <summary>Everything the park keeps between visits, in one file.</summary>
public sealed class ParkData
{
    public List<ParkPokemon> Roster { get; set; } = [];
    public List<ParkCareState> Care { get; set; } = [];
    public List<ParkJournalEvent> Journal { get; set; } = [];
    public List<ParkTreasure> Treasures { get; set; } = [];
    public List<ParkWildFriend> Wilds { get; set; } = [];
    public List<ParkStay> Daycare { get; set; } = [];
    /// <summary>The relationships between the residents (friendship, affection, rivalry).</summary>
    public ParkSocialState? Social { get; set; }
    public string? Goals { get; set; }
    public PokeparkSettings Settings { get; set; } = new();
    public bool Initialized { get; set; }
    public bool AutoFillSuppressed { get; set; }
    public long SavedAt { get; set; }
}

/// <summary>
/// The park's own storage: the roster, the care, the journal, the treasures and the wild
/// friends, kept in %AppData%\PKBank\park. Writes are atomic (tmp then rename) and a failed
/// write never breaks the app: the park is soft data, the games are the real thing.
/// </summary>
public sealed class PokeparkStore
{
    private static readonly string DefaultFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKBank", "park");

    /// <summary>Where the park keeps its files. The tests point it at a temporary folder.</summary>
    public static string Folder { get; set; } = DefaultFolder;

    private static string ParkFolder => Folder;
    private static string FilePath => Path.Combine(ParkFolder, "park.json");
    private static readonly Lock Gate = new();
    private static ParkData? _cached;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The folder with the photos taken in the park.</summary>
    public static string AlbumFolder => Path.Combine(ParkFolder, "album");

    /// <summary>Forgets the cached file (the tests move the folder between runs).</summary>
    public static void Reset()
    {
        lock (Gate) _cached = null;
    }

    public static ParkData Load()
    {
        lock (Gate)
        {
            if (_cached is not null) return _cached;
            try
            {
                if (File.Exists(FilePath))
                    _cached = JsonSerializer.Deserialize<ParkData>(File.ReadAllText(FilePath)) ?? new ParkData();
                else
                    _cached = new ParkData();
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                _cached = new ParkData();
            }
            return _cached;
        }
    }

    public static void Save(ParkData data)
    {
        lock (Gate)
        {
            _cached = data;
            data.SavedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            try
            {
                Directory.CreateDirectory(ParkFolder);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The park state is soft; a failed write must not break the app.
            }
        }
    }

    /// <summary>Saves a photo of the park in the album folder and returns its path.</summary>
    public static string SavePhoto(byte[] jpeg)
    {
        Directory.CreateDirectory(AlbumFolder);
        var path = Path.Combine(AlbumFolder, $"park-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.jpg");
        File.WriteAllBytes(path, jpeg);
        // The album keeps the newest 200 photos.
        foreach (var old in new DirectoryInfo(AlbumFolder).GetFiles("*.jpg").OrderByDescending(f => f.LastWriteTimeUtc).Skip(200))
            try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return path;
    }

    /// <summary>The photos in the album, newest first.</summary>
    public static IReadOnlyList<string> Photos()
    {
        try
        {
            if (!Directory.Exists(AlbumFolder)) return [];
            return new DirectoryInfo(AlbumFolder).GetFiles("*.jpg")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
