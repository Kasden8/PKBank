using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>What is worth watching is announced the moment it starts, with whom to look at.</summary>
public sealed class ParkHighlightTests
{
    [Fact]
    public void Rare_moments_are_announced_with_who_to_watch()
    {
        var life = new ParkLife(ParkMap.Park, [
            new("lapras", "Lapras", 131, [ParkType.Water, ParkType.Ice], ParkPersonality.Gentle),
            new("pikachu", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Curious),
            new("charmander", "Charmander", 4, [ParkType.Fire], ParkPersonality.Curious),
            new("m1", "Magnemite", 81, [ParkType.Electric, ParkType.Steel], ParkPersonality.Calm),
            new("m2", "Magnemite", 81, [ParkType.Electric, ParkType.Steel], ParkPersonality.Calm),
            new("m3", "Magnemite", 81, [ParkType.Electric, ParkType.Steel], ParkPersonality.Calm)], seed: 5)
            { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day, Weather = ParkWeather.Sunny };
        var seen = new List<ParkHighlight>();
        for (long t = 0; t <= 3 * 60 * 60 * 1000L; t += 250)
        {
            life.Advance(t);
            foreach (var h in life.TakeHighlights())
            {
                seen.Add(h);
                Assert.NotNull(life[h.ActorId]);
                Assert.True(h.Until > h.Start);
                Assert.Contains(life.Highlights, now => now.ActorId == h.ActorId);
            }
        }
        Assert.Contains(seen, h => h.Text.Contains("ferrying") && h.Priority == 3);
        Assert.Contains(seen, h => h.Text.Contains("Magneton") && h.Priority == 3);
    }
}
