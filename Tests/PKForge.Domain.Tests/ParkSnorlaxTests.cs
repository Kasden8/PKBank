using PKForge.Domain;
using Xunit;
using Xunit.Abstractions;

namespace PKForge.Domain.Tests;

/// <summary>Snorlax asleep across the way holds everyone up; at night the little ones sleep on it.</summary>
public sealed class ParkSnorlaxTests(ITestOutputHelper output)
{
    private static readonly ParkMap Park = ParkMap.Park;

    [Fact]
    public void Snorlax_asleep_across_the_way_holds_walkers_up()
    {
        ParkLifeResident[] residents =
        [
            new("snorlax", "Snorlax", 143, [ParkType.Normal], ParkPersonality.Calm, BaseSpeed: 30),
            .. new[] { 25, 1, 4, 52, 133, 39, 27, 35, 74, 16 }.Select((s, i) =>
                new ParkLifeResident($"r{i}", $"R{i}", s, [ParkType.Normal], i % 2 == 0 ? ParkPersonality.Curious : ParkPersonality.Energetic)),
        ];
        var life = new ParkLife(Park, residents, seed: 6) { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day, Weather = ParkWeather.Sunny };
        var blocked = false; var held = 0; var lines = new List<string>();
        for (long t = 0; t <= 6 * 60 * 60 * 1000L && !(blocked && held > 0); t += 250)
        {
            life.Advance(t);
            var snorlax = life["snorlax"]!;
            blocked |= snorlax.Activity.StartsWith("Fast asleep in");
            foreach (var a in life.Actors.Where(a => a.Activity.StartsWith("Waiting for Snorlax")))
            {
                held++;
                // Held up short of it, never walking through it.
                Assert.True(MathF.Sqrt((a.X - snorlax.X) * (a.X - snorlax.X) + (a.Y - snorlax.Y) * (a.Y - snorlax.Y)) > 6);
            }
            lines.AddRange(life.TakeMoments().Where(m => m.Text is not null).Select(m => m.Text!));
        }
        foreach (var line in lines.Where(l => l.Contains("Snorlax"))) output.WriteLine(line);
        Assert.True(blocked, "Snorlax never fell asleep across a narrow way");
        Assert.True(held > 0, "nobody was held up");
    }

    [Fact]
    public void At_night_the_little_ones_sleep_on_and_around_Snorlax()
    {
        ParkLifeResident[] residents =
        [
            new("snorlax", "Snorlax", 143, [ParkType.Normal], ParkPersonality.Calm, BaseSpeed: 30),
            new("pichu", "Pichu", 172, [ParkType.Electric], ParkPersonality.Playful),
            new("bulbasaur", "Bulbasaur", 1, [ParkType.Grass], ParkPersonality.Gentle),
            new("charmander", "Charmander", 4, [ParkType.Fire], ParkPersonality.Calm),
            new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Shy),
        ];
        var life = new ParkLife(Park, residents, seed: 3) { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Night, Weather = ParkWeather.Sunny };
        var onBelly = false; var lines = new List<string>();
        for (long t = 0; t <= 2 * 60 * 60 * 1000L; t += 250)
        {
            life.Advance(t);
            Assert.False(life["snorlax"]!.Indoors, "Snorlax does not fit in the cabin");
            onBelly |= life.Actors.Any(a => a.Activity.Contains("on Snorlax's belly") && a.Altitude > 0 && a.Pose == ParkPose.Sleep);
            lines.AddRange(life.TakeMoments().Where(m => m.Text is not null).Select(m => m.Text!));
        }
        Assert.True(onBelly, "nobody slept on Snorlax's belly");
        Assert.Contains(lines, l => l.Contains("did not notice"));
    }

    [Fact]
    public void The_Poke_Flute_wakes_a_Snorlax_across_the_way()
    {
        var life = new ParkLife(Park, [new ParkLifeResident("snorlax", "Snorlax", 143, [ParkType.Normal], ParkPersonality.Calm, BaseSpeed: 30)], seed: 6)
            { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day, Weather = ParkWeather.Sunny };
        long t = 0;
        for (; t <= 6 * 60 * 60 * 1000L && !life["snorlax"]!.Activity.StartsWith("Fast asleep in"); t += 250) life.Advance(t);
        life.Advance(t += 3000);
        var reply = life.Command("snorlax", ParkCommand.Talk);
        Assert.True(reply.Accepted);
        Assert.Contains("Poké Flute", reply.Line);
        life.Advance(t += 6000);
        Assert.False(life["snorlax"]!.IsAsleep);
    }
}
