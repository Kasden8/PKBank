namespace PKForge.Domain;

/// <summary>Where a Pokémon's PMDCollab sprite lives: the folder of its form (Alolan Raichu,
/// Unown B, a Vivillon pattern…), its shiny, its female, from the table that
/// tools/SpriteForms/pmd.py builds by matching PKHeX's form names against PMDCollab's.</summary>
public static class ParkSprites
{
    private readonly record struct Entry(string Folder, bool Shiny, bool Female);

    private static readonly Lazy<Dictionary<(int, int), Entry>> Table = new(Load);

    private static Dictionary<(int, int), Entry> Load()
    {
        var table = new Dictionary<(int, int), Entry>();
        using var stream = typeof(ParkSprites).Assembly.GetManifestResourceStream("PKForge.Domain.pmdforms.tsv");
        if (stream is null) return table;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#')) continue;
            var cells = line.Split('\t');
            var key = cells[0].Split('-');
            var flags = int.Parse(cells[2]);
            table[(int.Parse(key[0]), int.Parse(key[1]))] = new(cells[1], (flags & 2) != 0, (flags & 4) != 0);
        }
        return table;
    }

    /// <summary>The sprite's path under PMDCollab's sprite/ folder ("0026/0001" for Alolan
    /// Raichu, "0025/0000/0001" for a shiny Pikachu), or null when PMDCollab has not drawn that
    /// form or that shiny: the park then shows the still sprite rather than the wrong one.</summary>
    public static string? Key(int species, int form, bool shiny)
    {
        if (!Table.Value.TryGetValue((species, form), out var entry) || shiny && !entry.Shiny) return null;
        var key = $"{species:D4}";
        if (entry.Female) return key + "/0000/" + (shiny ? "0001" : "0000") + "/0002";
        if (entry.Folder != "0000" || shiny) key += "/" + entry.Folder;
        return shiny ? key + "/0001" : key;
    }

    /// <summary>The portrait's path under PMDCollab's portrait/ folder: the form's, never the
    /// shiny's (few shiny portraits exist).</summary>
    public static string? PortraitKey(int species, int form) => Key(species, form, false);
}
