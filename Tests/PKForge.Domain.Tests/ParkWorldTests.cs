using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>The world around the residents: fresh lines when you talk, the weather, wild
/// visitors, tastes you find out over time.</summary>
public sealed class ParkWorldTests
{
    private static readonly ParkMap Home = ParkMap.Park;

    [Fact]
    public void Talking_eight_times_never_repeats_a_line()
    {
        var life = new ParkLife(Home, [new("pika", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Playful)], seed: 1);
        life.Restore([new ParkCareState("pika", Bond: 70)], DateTimeOffset.Now);
        var lines = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            life.Advance(1000 + i * 10_000);
            var reply = life.Command("pika", ParkCommand.Talk);
            if (reply.Accepted) lines.Add(reply.Line!);
        }
        Assert.True(lines.Count >= 6);
        Assert.Equal(lines.Count, lines.Distinct().Count());
    }

    [Fact]
    public void A_fire_type_runs_for_cover_in_the_rain()
    {
        var life = new ParkLife(Home, [new("charm", "Charmander", 4, [ParkType.Fire], ParkPersonality.Calm)], seed: 3) { Weather = ParkWeather.Rain };
        var sheltered = false;
        for (long t = 0; t < 20 * 60 * 1000 && !sheltered; t += 250)
        {
            life.Advance(t);
            sheltered = life["charm"]!.Activity.Contains("Shelter") || life["charm"]!.Indoors;
        }
        Assert.True(sheltered);
    }

    [Fact]
    public void Wild_visitors_come_and_go_and_are_never_saved()
    {
        var life = new ParkLife(Home, [new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm)], seed: 4);
        var seen = new HashSet<string>();
        var places = new HashSet<string>();
        var moments = new List<ParkMoment>();
        for (long t = 0; t < 3 * 60 * 60 * 1000; t += 500)
        {
            life.Advance(t);
            moments.AddRange(life.TakeMoments());
            foreach (var actor in life.Actors.Where(a => ParkLife.IsVisitor(a.Resident.Id) && !ParkLife.IsKeeper(a.Resident.Id)))
                if (seen.Add(actor.Resident.Id) && !actor.InFlight) places.Add(Home.RegionAt(actor.X, actor.Y).Id);
            Assert.DoesNotContain(life.Snapshot(DateTimeOffset.Now), c => ParkLife.IsVisitor(c.Id));
        }
        Assert.NotEmpty(seen);
        Assert.Contains(moments, m => m.Text?.Contains("wandered into") == true);
        Assert.True(seen.Count >= 2, "visitors should leave and others come");
        // They come in all round the park, not all at the home park's arch.
        Assert.True(places.Count >= 2, string.Join(", ", places));
    }

    [Fact]
    public void Tastes_are_found_out_one_berry_at_a_time()
    {
        // Modest: loves Dry (3), dislikes Spicy (0).
        Assert.Equal("loves ??? · dislikes ???", ParkTaste.Describe(15, 0));
        var life = new ParkLife(Home, [new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Gentle)], seed: 1);
        life.Restore([new ParkCareState("eevee", Bond: 90)], DateTimeOffset.Now);
        life.Advance(1000);
        life.Feed("eevee", 3, 15);
        Assert.Equal("loves Dry · dislikes ???", ParkTaste.Describe(15, life["eevee"]!.Care.TasteKnown));
    }

    [Fact]
    public void The_weather_holds_for_a_block_of_the_day()
    {
        var morning = new DateTime(2026, 10, 6, 7, 0, 0);
        Assert.Equal(ParkCare.WeatherAt(morning), ParkCare.WeatherAt(morning.AddHours(4)));
        var kinds = Enumerable.Range(0, 400).Select(d => ParkCare.WeatherAt(morning.AddDays(d))).ToHashSet();
        Assert.Equal(4, kinds.Count);
    }
}
