using PKForge.Domain;
using Xunit;
using Xunit.Abstractions;

namespace PKForge.Domain.Tests;

/// <summary>Residents of different kinds do different things: each has habits of its own, a
/// favorite among them, and its own spot.</summary>
public sealed class ParkHabitTests(ITestOutputHelper output)
{
    [Fact]
    public void A_mixed_park_is_full_of_different_doings()
    {
        ParkLifeResident[] residents =
        [
            new("charmander", "Charmander", 4, [ParkType.Fire], ParkPersonality.Bold, BaseSpeed: 65),
            new("bulbasaur", "Bulbasaur", 1, [ParkType.Grass, ParkType.Poison], ParkPersonality.Gentle, BaseSpeed: 45),
            new("squirtle", "Squirtle", 7, [ParkType.Water], ParkPersonality.Playful, BaseSpeed: 43),
            new("pikachu", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Energetic, BaseSpeed: 90),
            new("sandshrew", "Sandshrew", 27, [ParkType.Ground], ParkPersonality.Curious, BaseSpeed: 40),
            new("gastly", "Gastly", 92, [ParkType.Ghost, ParkType.Poison], ParkPersonality.Shy, BaseSpeed: 80),
            new("abra", "Abra", 63, [ParkType.Psychic], ParkPersonality.Calm, BaseSpeed: 90),
            new("caterpie", "Caterpie", 10, [ParkType.Bug], ParkPersonality.Curious, BaseSpeed: 45),
            new("onix", "Onix", 95, [ParkType.Rock, ParkType.Ground], ParkPersonality.Calm, BaseSpeed: 70),
            new("clefairy", "Clefairy", 35, [ParkType.Fairy], ParkPersonality.Gentle, BaseSpeed: 35),
        ];
        var life = new ParkLife(ParkMap.Park, residents, seed: 9) { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day, Weather = ParkWeather.Sunny };
        var seen = residents.ToDictionary(r => r.Id, _ => new Dictionary<string, int>());
        for (long t = 0; t <= 2 * 60 * 60 * 1000L; t += 1000)
        {
            life.Advance(t);
            foreach (var a in life.Actors.Where(a => seen.ContainsKey(a.Resident.Id)))
                seen[a.Resident.Id][a.Activity] = seen[a.Resident.Id].GetValueOrDefault(a.Activity) + 1;
        }
        var all = seen.Values.SelectMany(d => d.Keys).Distinct().ToList();
        output.WriteLine($"{all.Count} different doings");
        // What each spends the most time on, walking aside.
        var favorites = seen.ToDictionary(p => p.Key, p => p.Value.Where(kv => !kv.Key.StartsWith("Taking") && !kv.Key.StartsWith("Going") &&
            !kv.Key.StartsWith("Looking") && !kv.Key.StartsWith("Wandering") && !kv.Key.StartsWith("Heading") && !kv.Key.StartsWith("Off ")).MaxBy(kv => kv.Value).Key);
        foreach (var (id, favorite) in favorites) output.WriteLine($"{id}: {favorite}");
        Assert.True(all.Count >= 45, $"only {all.Count} different doings");
        Assert.True(favorites.Values.Distinct().Count() >= 7, string.Join(", ", favorites.Values));
    }
}
