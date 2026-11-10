using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>The park's rhythms: the morning call, Hoothoot's hours, the rain chorus.</summary>
public sealed class ParkRhythmTests
{
    private static ParkLifeResident Mon(string name, int species, params int[] types) =>
        new(name.ToLowerInvariant(), name, species, types, ParkPersonality.Curious);

    private static (List<string> Journal, HashSet<string> Labels) Run(ParkLife life, double hours)
    {
        var journal = new List<string>(); var labels = new HashSet<string>();
        for (long t = 0; t <= hours * 60 * 60 * 1000L; t += 250)
        {
            life.Advance(t);
            foreach (var a in life.Actors) labels.Add(a.Activity);
            journal.AddRange(life.TakeMoments().Where(m => m.Text is not null).Select(m => m.Text!));
        }
        return (journal, labels);
    }

    [Fact]
    public void Every_morning_the_park_lines_up_for_the_morning_call_once()
    {
        var life = new ParkLife(ParkMap.Park, [Mon("Loudred", 294, ParkType.Normal), Mon("Pikachu", 25, ParkType.Electric), Mon("Eevee", 133, ParkType.Normal),
            Mon("Bulbasaur", 1, ParkType.Grass), Mon("Meowth", 52, ParkType.Normal)], seed: 2)
            { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Morning, Weather = ParkWeather.Sunny };
        var (journal, labels) = Run(life, 1);
        Assert.Contains("Leading the morning call", labels);
        Assert.Single(journal, l => l.StartsWith("Morning call"));
    }

    [Fact]
    public void Hoothoot_hoots_on_the_hour()
    {
        var life = new ParkLife(ParkMap.Park, [Mon("Hoothoot", 163, ParkType.Normal, ParkType.Flying)], seed: 2)
            { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day };
        var (_, labels) = Run(life, 1.1);
        Assert.Contains("Hooting the hour", labels);
    }

    [Fact]
    public void Politoed_leads_the_rain_chorus()
    {
        var life = new ParkLife(ParkMap.Park, [Mon("Politoed", 186, ParkType.Water), Mon("Poliwag", 60, ParkType.Water), Mon("Wooper", 194, ParkType.Water, ParkType.Ground)], seed: 2)
            { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day, Weather = ParkWeather.Rain };
        var (journal, labels) = Run(life, 3);
        Assert.Contains("Leading the rain chorus", labels);
        Assert.Contains(journal, l => l.Contains("rain chorus"));
    }

    [Fact]
    public void Sudowoodo_runs_from_the_rain()
    {
        var life = new ParkLife(ParkMap.Park, [Mon("Sudowoodo", 185, ParkType.Rock)], seed: 2)
            { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day, Weather = ParkWeather.Sunny };
        long t = 0;
        for (; t <= 3 * 60 * 60 * 1000L && life["sudowoodo"]!.Activity != "Pretending to be a tree"; t += 250) life.Advance(t);
        Assert.Equal("Pretending to be a tree", life["sudowoodo"]!.Activity);
        life.Weather = ParkWeather.Rain;
        for (var i = 0; i < 20; i++) life.Advance(t += 250);
        Assert.Equal("A tree that hates the rain", life["sudowoodo"]!.Activity);
    }

    [Fact]
    public void Volbeat_draws_a_heart_of_light_for_Illumise_at_dusk()
    {
        var life = new ParkLife(ParkMap.Park, [Mon("Volbeat", 313, ParkType.Bug), Mon("Illumise", 314, ParkType.Bug)], seed: 2)
            { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Evening };
        var (journal, labels) = Run(life, 1);
        Assert.Contains(labels, l => l.StartsWith("Drawing a heart of light"));
        Assert.Single(journal, l => l.Contains("heart of light"));
    }
}
