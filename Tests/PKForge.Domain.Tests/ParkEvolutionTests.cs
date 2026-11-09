using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>Evolution in the park: a resident starts to evolve and stops (the park never changes
/// a Pokémon from a save), and the announcement says what it was becoming.</summary>
public sealed class ParkEvolutionTests
{
    private static readonly ParkLifeResident[] Residents =
    [
        new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Curious),
        new("pikachu", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Playful),
        new("charmander", "Charmander", 4, [ParkType.Fire], ParkPersonality.Bold),
        new("snorlax", "Snorlax", 143, [ParkType.Normal], ParkPersonality.Calm),
    ];

    [Fact]
    public void Knows_what_each_species_evolves_into()
    {
        Assert.Contains(134, ParkSpecies.EvolvesInto(133));
        Assert.Contains(136, ParkSpecies.EvolvesInto(133));
        Assert.Equal([26], ParkSpecies.EvolvesInto(25));
        Assert.Empty(ParkSpecies.EvolvesInto(143));
    }

    [Fact]
    public void A_resident_starts_to_evolve_and_stops_without_changing_species()
    {
        ParkHighlight? seen = null;
        foreach (var seed in new[] { 1, 2, 3, 4 })
        {
            var life = new ParkLife(ParkMap.Park, Residents, seed: seed);
            for (long t = 0; t <= 8 * 60 * 60_000L && seen is null; t += 5000)
            {
                life.Advance(t);
                seen = life.TakeHighlights().FirstOrDefault(h => h.EvolvesInto != 0 && h.StopsEvolving);
            }
            for (var end = life.Now + 20_000; life.Now < end;) life.Advance(life.Now + 1000);
            foreach (var resident in Residents)
                Assert.Equal(resident.Species, life[resident.Id]?.Resident.Species ?? resident.Species);
            if (seen is not null) break;
        }
        Assert.NotNull(seen);
        var evolving = Residents.First(r => r.Id == seen.ActorId);
        Assert.Contains(seen.EvolvesInto, ParkSpecies.EvolvesInto(evolving.Species));
        Assert.StartsWith("What?", seen.Before);
        Assert.Contains("stopped evolving", seen.Text);
    }
}
