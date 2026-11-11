using PKForge.Domain;
using PKHeX.Core;

namespace PKBank.App.Services.Pokepark;

/// <summary>The look of a habitat, for the park's panels and the residents' cards.</summary>
public sealed record HabitatProfile(string Name, string Subtitle, string Ground, string Water, string Accent);

/// <summary>
/// The park's view of the species data: its canonical types and its base Speed (for the
/// races). Ported from PKForge's app layer, with the same personal-table preference order:
/// the newest game first, because park snapshots carry no generation.
/// </summary>
public static class HabitatCatalog
{
    private static readonly object CacheGate = new();
    private static readonly Dictionary<(int Species, int Form), IReadOnlyList<int>> TypeCache = new();

    private static readonly IPersonalTable[] Tables =
        [PersonalTable.SV, PersonalTable.ZA, PersonalTable.SWSH, PersonalTable.LA, PersonalTable.USUM];

    /// <summary>The species' base Speed stat (60 when no game has it), for park races.</summary>
    public static int SpeedFor(int species, int form = 0)
    {
        if (species <= 0 || species > ushort.MaxValue || form < 0 || form > byte.MaxValue) return 60;
        foreach (var table in Tables)
            if (table.IsPresentInGame((ushort)species, (byte)form))
                return table.GetFormEntry((ushort)species, (byte)form).SPE;
        return form != 0 ? SpeedFor(species) : 60;
    }

    /// <summary>Canonical 1–18 park type IDs for a species/form. Never returns raw PKHeX values.</summary>
    public static IReadOnlyList<int> TypesFor(int species, int form = 0)
    {
        lock (CacheGate)
            if (TypeCache.TryGetValue((species, form), out var cached))
                return cached;

        IReadOnlyList<int> result;
        if (species <= 0 || species > ushort.MaxValue || form < 0 || form > byte.MaxValue)
            result = [ParkType.Unknown];
        else
        {
            result = [ParkType.Unknown];
            foreach (var table in Tables)
            {
                if (!table.IsPresentInGame((ushort)species, (byte)form)) continue;
                var entry = table.GetFormEntry((ushort)species, (byte)form);
                var first = ParkType.FromPkhex(entry.Type1);
                var second = ParkType.FromPkhex(entry.Type2);
                // Single-typed forms report the same type twice: one entry is enough.
                result = first == second ? [first] : [first, second];
                break;
            }
            // Invalid or stale form metadata must not select another species.
            if (result.Count == 1 && result[0] == ParkType.Unknown && form != 0)
                result = TypesFor(species);
        }
        lock (CacheGate)
            return TypeCache[(species, form)] = result;
    }

    /// <summary>Warms the park type lookups off the UI thread.</summary>
    public static void Warm(IEnumerable<(int Species, int Form)> forms)
    {
        foreach (var (species, form) in forms.Distinct())
            _ = TypesFor(species, form);
    }

    public static HabitatProfile Profile(IReadOnlyList<int> types)
    {
        if (types.Contains(ParkType.Water)) return new("Cascade Lake", "Cool shallows and open swimming water", "#B7DCE0", "#4DA6BB", "#E6FBFF");
        if (types.Contains(ParkType.Fire)) return new("Ember Grove", "Warm basalt shelves and sleepy sparks", "#D99B70", "#BA604A", "#FFE0A8");
        if (types.Contains(ParkType.Flying) || types.Contains(ParkType.Ice)) return new("Sky Summit", "Mountain breezes above the cloud line", "#D5E2ED", "#8DA9C5", "#FFFFFF");
        if (types.Contains(ParkType.Grass)) return new("Verdant Grove", "Broad leaves, flowers, and soft moss", "#B9D990", "#6FAE76", "#F0F6BE");
        if (types.Contains(ParkType.Electric)) return new("Static Meadow", "Tall grass humming with little lights", "#D7D38E", "#93B06A", "#FFF5A6");
        return new("Meadow Commons", "A welcoming green meadow for everyone", "#C8E5A0", "#80B477", "#F8E7B4");
    }
}
