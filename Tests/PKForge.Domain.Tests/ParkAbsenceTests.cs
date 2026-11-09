using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>What happened while you were away, told from what the park knows, and anniversaries.</summary>
public sealed class ParkAbsenceTests
{
    private static readonly ParkLifeResident[] Residents =
    [
        new("zan", "Zangoose", 335, [ParkType.Normal], ParkPersonality.Bold),
        new("sev", "Seviper", 336, [ParkType.Poison], ParkPersonality.Bold),
        new("vol", "Volbeat", 313, [ParkType.Bug], ParkPersonality.Gentle),
        new("ill", "Illumise", 314, [ParkType.Bug], ParkPersonality.Gentle),
        new("gen", "Gengar", 94, [ParkType.Ghost, ParkType.Poison], ParkPersonality.Playful),
        new("cha", "Charmander", 4, [ParkType.Fire], ParkPersonality.Calm),
    ];

    [Fact]
    public void A_long_absence_tells_a_few_true_stories_in_order()
    {
        var left = new DateTime(2026, 10, 4, 20, 0, 0);
        var back = new DateTime(2026, 10, 6, 9, 0, 0);
        var before = Residents.ToDictionary(r => r.Id, r => new ParkCareState(r.Id, Hunger: 80, Bond: 50));
        var after = Residents.ToDictionary(r => r.Id, r => new ParkCareState(r.Id, Hunger: 35, Fun: 20, Bond: 50));
        var stories = ParkAbsence.Tell(Residents, before, after, left, back, new Random(3));
        Assert.InRange(stories.Count, 3, 5);
        var names = Residents.Select(r => r.Name).ToArray();
        Assert.All(stories, s => Assert.True(names.Any(s.Text.Contains) || s.Text.Contains("wild") || s.Text.Contains("rain") || s.Text.Contains("storm") || s.Text.Contains("park")));
        Assert.All(stories, s => Assert.Matches("^(Last night|This |Yesterday |[0-9]+ days ago)", s.Text));
        Assert.Contains(stories, s => s.Moment is not null || s.Text.Contains("missed you"));
    }

    [Fact]
    public void A_short_break_has_no_story()
    {
        var now = new DateTime(2026, 10, 6, 9, 0, 0);
        var care = Residents.ToDictionary(r => r.Id, r => new ParkCareState(r.Id));
        Assert.Empty(ParkAbsence.Tell(Residents, care, care, now.AddMinutes(-10), now, new Random(1)));
    }

    [Theory]
    [InlineData(2026, 10, 6, 2, "Last night")]
    [InlineData(2026, 10, 6, 8, "This morning")]
    [InlineData(2026, 10, 5, 15, "Yesterday afternoon")]
    [InlineData(2026, 10, 5, 21, "Last night")]
    [InlineData(2026, 10, 3, 12, "3 days ago")]
    public void Times_read_like_people_say_them(int year, int month, int day, int hour, string expected) =>
        Assert.Equal(expected, ParkAbsence.When(new DateTime(year, month, day, hour, 0, 0), new DateTime(2026, 10, 6, 10, 0, 0)));

    [Fact]
    public void An_anniversary_brings_the_park_together()
    {
        var life = new ParkLife(ParkMap.Park, Residents, seed: 2);
        life.Advance(1000);
        var reply = life.Celebrate("vol", 3);
        Assert.Contains("3 years ago today", reply.Line);
        Assert.Contains(life.TakeMoments(), m => m.Text?.Contains("party for Volbeat") == true);
        var guests = new HashSet<string>();
        for (long t = 1000; t < 20_000; t += 100)
        {
            life.Advance(t);
            foreach (var actor in life.Actors.Where(a => a.Activity == "At Volbeat's party")) guests.Add(actor.Resident.Id);
        }
        Assert.True(guests.Count >= 3, $"{guests.Count} guests");
    }
}
