using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>Meetings: two who notice something about each other (a shiny and a plain one of the
/// same kind, two of a kind, what a little one may become, Skitty and Wailord…).</summary>
public sealed class ParkMeetingTests
{
    private static readonly ParkMap Park = ParkMap.Park;

    private static List<string> Journal(ParkLifeResident[] residents, int hours = 2)
    {
        var life = new ParkLife(Park, residents, seed: 5) { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day, Weather = ParkWeather.Sunny };
        var journal = new List<string>();
        for (long t = 0; t <= hours * 60 * 60 * 1000L; t += 200)
        {
            life.Advance(t);
            foreach (var actor in life.Actors)
                Assert.True(actor.InFlight || actor.Riding || Park.CanOccupy(actor.X, actor.Y, actor.Resident.Traversal, ParkNavigator.Radius - .5f),
                    $"{actor.Resident.Name} at {actor.X},{actor.Y} ({actor.Activity})");
            journal.AddRange(life.TakeMoments().Where(m => m.Text is not null).Select(m => m.Text!));
        }
        return journal;
    }

    private static ParkLifeResident Mon(string name, int species, int[] types, bool shiny = false, int form = 0) =>
        new(name.ToLowerInvariant(), name, species, types, ParkPersonality.Curious, Shiny: shiny, Form: form);

    [Fact]
    public void A_shiny_and_a_plain_one_of_the_same_kind_are_puzzled_by_each_other()
    {
        var journal = Journal([Mon("Sparky", 25, [ParkType.Electric], shiny: true), Mon("Pika", 25, [ParkType.Electric])]);
        Assert.Contains(journal, line => line.Contains("colours"));
    }

    [Fact]
    public void Two_of_a_kind_copy_each_other()
    {
        var journal = Journal([Mon("Bulba", 1, [ParkType.Grass, ParkType.Poison]), Mon("Saur", 1, [ParkType.Grass, ParkType.Poison])]);
        Assert.Contains(journal, line => line.Contains("copying each other"));
    }

    [Fact]
    public void Pikachu_meets_Raichu_and_stays_as_it_is()
    {
        var journal = Journal([Mon("Pikachu", 25, [ParkType.Electric]), Mon("Raichu", 26, [ParkType.Electric])]);
        Assert.Contains(journal, line => line.Contains("happy as it is"));
    }

    [Fact]
    public void A_regional_form_meets_the_one_from_here()
    {
        var journal = Journal([Mon("Meowth", 52, [ParkType.Normal]), Mon("Nyarth", 52, [ParkType.Dark], form: 1)], hours: 4);
        Assert.Contains(journal, line => line.Contains("far away"));
    }

    [Fact]
    public void Skitty_and_Wailord_get_on_wonderfully()
    {
        var journal = Journal([Mon("Skitty", 300, [ParkType.Normal]), Mon("Wailord", 321, [ParkType.Water])], hours: 4);
        Assert.Contains(journal, line => line.Contains("got on wonderfully"));
    }

    [Fact]
    public void The_same_two_do_not_meet_like_this_again_and_again()
    {
        // At most once in three hours: one or two meetings in five.
        var journal = Journal([Mon("Ditto", 132, [ParkType.Normal]), Mon("Mew", 132, [ParkType.Normal])], hours: 5);
        Assert.InRange(journal.Count(line => line.Contains("copy each other")), 1, 2);
    }

    [Fact]
    public void Evolution_lines_come_from_the_table()
    {
        Assert.True(ParkSpecies.BecomesOneDay(4, 6));
        Assert.True(ParkSpecies.BecomesOneDay(172, 26));
        Assert.False(ParkSpecies.BecomesOneDay(6, 4));
        Assert.False(ParkSpecies.BecomesOneDay(1, 4));
    }
}
