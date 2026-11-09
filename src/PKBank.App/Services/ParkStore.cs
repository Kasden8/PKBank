using System.Text.Json;

namespace PKBank.App.Services;

/// <summary>One park resident: identity as recorded (Bank and Save provenance preserved).</summary>
public sealed record ParkPokemon(string Id, int Species, int Form, bool Shiny, string Name, string Source,
    string GameName = "", string TrainerName = "", string Origin = "");

/// <summary>
/// Park residents persisted as the same <see cref="ParkPokemon"/> shape PKForge uses,
/// so the data model stays compatible if the full simulation lands later.
/// </summary>
public sealed class ParkStore
{
    public const int MaximumResidents = 24;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKBank", "park.json");
    private readonly Lock _gate = new();
    private ParkData _data;

    private sealed class ParkData
    {
        public List<ParkPokemon> Roster { get; set; } = [];
        public Dictionary<string, ParkCare> Care { get; set; } = new(StringComparer.Ordinal);
        public List<string> Journal { get; set; } = [];
    }

    public sealed record ParkCare(int Hunger, int Friendship, DateTimeOffset LastCare);

    public ParkStore() => _data = Load();

    public IReadOnlyList<ParkPokemon> Roster
    {
        get { lock (_gate) return [.. _data.Roster]; }
    }

    public IReadOnlyList<string> Journal
    {
        get { lock (_gate) return [.. _data.Journal.TakeLast(80)]; }
    }

    public ParkCare? CareOf(string id)
    {
        lock (_gate) return _data.Care.GetValueOrDefault(id);
    }

    public void AddVisitor(ParkPokemon visitor, string welcome)
    {
        lock (_gate)
        {
            if (_data.Roster.Any(p => p.Id == visitor.Id)) return;
            if (_data.Roster.Count >= MaximumResidents) return;
            _data.Roster.Add(visitor);
            _data.Care.TryAdd(visitor.Id, new ParkCare(Hunger: 0, Friendship: 0, LastCare: DateTimeOffset.Now));
            _data.Journal.Add(welcome);
            Save();
        }
    }

    public void RemoveVisitor(string id, string goodbye)
    {
        lock (_gate)
        {
            _data.Roster.RemoveAll(p => p.Id == id);
            _data.Care.Remove(id);
            _data.Journal.Add(goodbye);
            Save();
        }
    }

    public void Care(string id, bool feed)
    {
        lock (_gate)
        {
            if (!_data.Care.TryGetValue(id, out var care)) return;
            _data.Care[id] = feed
                ? care with { Hunger = Math.Max(0, care.Hunger - 20), LastCare = DateTimeOffset.Now }
                : care with { Friendship = Math.Min(100, care.Friendship + 4), LastCare = DateTimeOffset.Now };
            if (feed) _data.Journal.Add($"Fed {NameOfLocked(id)}.");
            else _data.Journal.Add($"Played with {NameOfLocked(id)}.");
            Save();
        }
    }

    public string CareStatus(string id)
    {
        var care = CareOf(id);
        if (care is null) return "Visitor";
        return care.Hunger >= 70
            ? "Hungry!"
            : care.Friendship >= 50
                ? $"Happy ({care.Friendship})"
                : "Settling in";
    }

    /// <summary>The pair scenes ("X and Y did Z") — lite social engine; templates from ParkLore style.</summary>
    public void Bond(ParkPokemon a, ParkPokemon b)
    {
        lock (_gate)
        {
            if (a.Id == b.Id) return;
            if (!_data.Care.TryGetValue(a.Id, out var ca)) return;
            if (!_data.Care.TryGetValue(b.Id, out var cb)) return;
            _data.Care[a.Id] = ca with { Friendship = Math.Min(100, ca.Friendship + 6), Hunger = Math.Max(0, ca.Hunger + 5), LastCare = DateTimeOffset.Now };
            _data.Care[b.Id] = cb with { Friendship = Math.Min(100, cb.Friendship + 6), Hunger = Math.Max(0, cb.Hunger + 5), LastCare = DateTimeOffset.Now };
            var line = BondLine(a.Name, b.Name, Random.Shared.Next(0, 8));
            if (line is not null) _data.Journal.Add(line);
            Save();
        }
    }

    private static string? BondLine(string a, string b, int roll) => roll switch
    {
        0 => $"{a} and {b} chased each other in circles.",
        1 => $"{a} and {b} shared a snack under the tree.",
        2 => $"{a} matched strides with {b} at the pond.",
        3 => $"{a} and {b} napped side by side.",
        4 => $"{a} challenged {b} to a top-speed race through the path.",
        5 => $"{a} traded fruit with {b} at the grove.",
        6 or _ => $"{a} and {b} watched the sunset together.",
    };

    private string NameOfLocked(string id)
    {
        return _data.Roster.FirstOrDefault(p => p.Id == id)?.Name ?? id;
    }

    private static ParkData Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new ParkData();
            return JsonSerializer.Deserialize<ParkData>(File.ReadAllText(FilePath)) ?? new ParkData();
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return new ParkData();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Park state is soft; a failed write must not break the app.
        }
    }
}
