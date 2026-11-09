namespace PKForge.Domain;

/// <summary>Species facts the park needs that no game table holds.</summary>
public static class ParkSpecies
{
    /// <summary>Water Pokémon without legs (fish, jellyfish, sea serpents, shells): they live in
    /// the water and never come ashore. National Pokédex numbers.</summary>
    public static readonly IReadOnlySet<int> WaterBound = new HashSet<int>
    {
        72, 73,                 // Tentacool, Tentacruel
        90, 91,                 // Shellder, Cloyster
        116, 117, 118, 119,     // Horsea, Seadra, Goldeen, Seaking
        120, 121,               // Staryu, Starmie
        129, 130, 131,          // Magikarp, Gyarados, Lapras
        170, 171,               // Chinchou, Lanturn
        211, 223, 224, 226, 230, // Qwilfish, Remoraid, Octillery, Mantine, Kingdra
        318, 319, 320, 321,     // Carvanha, Sharpedo, Wailmer, Wailord
        339, 340, 349, 350,     // Barboach, Whiscash, Feebas, Milotic
        366, 367, 368, 369, 370, // Clamperl, Huntail, Gorebyss, Relicanth, Luvdisc
        382,                    // Kyogre
        456, 457, 458,          // Finneon, Lumineon, Mantyke
        489,                    // Phione
        550, 592, 593, 594,     // Basculin, Frillish, Jellicent, Alomomola
        690, 691,               // Skrelp, Dragalge
        746, 771, 779,          // Wishiwashi, Pyukumuku, Bruxish
        846, 847, 882,          // Arrokuda, Barraskewda, Dracovish
        902, 904,               // Basculegion, Overqwil
        963, 964, 976, 977, 978 // Finizen, Palafin, Veluza, Dondozo, Tatsugiri
    };

    /// <summary>The giants of the sea: too big for the lake, they live out at sea, far from the
    /// shore, and spend long stretches deep under the water.</summary>
    public static readonly IReadOnlySet<int> Leviathans = new HashSet<int>
    {
        130, 131,               // Gyarados, Lapras
        321,                    // Wailord
        382,                    // Kyogre
        977,                    // Dondozo
    };

    private static readonly Lazy<Dictionary<int, (int From, int Family)>> Evolutions = new(() =>
    {
        var table = new Dictionary<int, (int, int)>();
        using var stream = typeof(ParkSpecies).Assembly.GetManifestResourceStream("PKForge.Domain.evolutions.tsv");
        if (stream is null) return table;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#')) continue;
            var cells = line.Split('\t');
            table[int.Parse(cells[0])] = (int.Parse(cells[1]), int.Parse(cells[2]));
        }
        return table;
    });

    /// <summary>The species it evolves from, or 0.</summary>
    public static int EvolvesFrom(int species) => Evolutions.Value.TryGetValue(species, out var e) ? e.From : 0;

    /// <summary>Whether <paramref name="later"/> is what <paramref name="earlier"/> may become
    /// (one or more evolutions further down the same line).</summary>
    public static bool BecomesOneDay(int earlier, int later)
    {
        for (var species = EvolvesFrom(later); species != 0; species = EvolvesFrom(species))
            if (species == earlier) return true;
        return false;
    }

    private static readonly Lazy<ILookup<int, int>> Into = new(() =>
        Evolutions.Value.Where(e => e.Value.From != 0).ToLookup(e => e.Value.From, e => e.Key));

    /// <summary>The species it evolves into (several for Eevee, Tyrogue…), none for a last form.</summary>
    public static IReadOnlyList<int> EvolvesInto(int species) => Into.Value[species].ToArray();

    private static readonly Lazy<HashSet<int>> Evolving = new(() => [.. Evolutions.Value.Values.Select(e => e.From).Where(f => f != 0)]);

    /// <summary>Whether the species evolves into anything.</summary>
    public static bool Evolves(int species) => Evolving.Value.Contains(species);
}
