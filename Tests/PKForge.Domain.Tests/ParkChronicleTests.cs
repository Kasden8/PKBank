using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>Stories that go on from one day to the next.</summary>
public sealed class ParkChronicleTests
{
    private static readonly ParkMap Park = ParkMap.Park;

    private static List<string> Run(ParkLife life, long hours)
    {
        var journal = new List<string>();
        for (long t = 0; t <= hours * 60 * 60 * 1000L; t += 250)
        {
            life.Advance(t);
            journal.AddRange(life.TakeMoments().Where(m => m.Text is not null).Select(m => m.Text!));
        }
        return journal;
    }

    [Fact]
    public void Bidoof_builds_its_dam_trip_by_trip_and_a_storm_washes_it_away()
    {
        var life = new ParkLife(Park, [new ParkLifeResident("bidoof", "Bidoof", 399, [ParkType.Normal], ParkPersonality.Calm)], seed: 3)
            { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day, Weather = ParkWeather.Sunny };
        var journal = Run(life, 4);
        Assert.True(life.Chronicle.Dam >= 3, $"{life.Chronicle.Dam} sticks");
        Assert.Contains(journal, l => l.Contains("building something"));
        // The dam is kept across days…
        var again = ParkChronicle.Load(life.Chronicle.Save());
        Assert.Equal(life.Chronicle.Dam, again.Dam);
        // …until a storm.
        life.Weather = ParkWeather.Storm;
        var storm = new List<string>();
        for (var t = 4 * 60 * 60 * 1000L; t <= 4 * 60 * 60 * 1000L + 120_000; t += 250)
        {
            life.Advance(t);
            storm.AddRange(life.TakeMoments().Where(m => m.Text is not null).Select(m => m.Text!));
        }
        Assert.Equal(0, life.Chronicle.Dam);
        Assert.Contains(storm, l => l.Contains("washed"));
    }

    [Fact]
    public void A_victim_picked_on_five_times_gets_its_own_back()
    {
        var life = new ParkLife(Park, [new ParkLifeResident("meowth", "Meowth", 52, [ParkType.Normal], ParkPersonality.Playful),
            new ParkLifeResident("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm)], seed: 4)
            { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day, Weather = ParkWeather.Sunny };
        var journal = Run(life, 4);
        Assert.Contains(journal, l => l.Contains("got its own back"));
    }

    [Fact]
    public void A_broken_saved_chronicle_starts_fresh() => Assert.Equal(0, ParkChronicle.Load("{nope").Dam);
}
