using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>The journal tells what is new: the same everyday moment between the same pair is
/// written once in a few hours, and the very same line never twice in a day; the friendship
/// still grows each time.</summary>
public sealed class ParkSocialJournalTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Greeting_the_same_friend_all_day_is_written_once()
    {
        var state = new ParkSocialState([], [], Noon);
        for (var i = 0; i < 10; i++)
            state = ParkSocialSimulation.Witness(state, new ParkMoment(ParkSocialEventKind.Rest, "a", "b"), "Pikachu", "Eevee", Noon.AddMinutes(10 * i)).State;
        Assert.Single(state.Journal);
        Assert.True(state.Relationships.Single().Friendship > 2);
        state = ParkSocialSimulation.Witness(state, new ParkMoment(ParkSocialEventKind.Rest, "a", "b"), "Pikachu", "Eevee", Noon.AddHours(26)).State;
        Assert.Equal(2, state.Journal.Length);
    }

    [Fact]
    public void A_story_is_not_told_twice_in_a_day()
    {
        var state = new ParkSocialState([], [], Noon);
        var moment = new ParkMoment(ParkSocialEventKind.Rest, "absol", "", "Absol stood at Lighthouse Point looking dramatic for twenty minutes. Nobody was watching.");
        state = ParkSocialSimulation.Witness(state, moment, "Absol", "", Noon).State;
        state = ParkSocialSimulation.Witness(state, moment, "Absol", "", Noon.AddHours(2)).State;
        Assert.Single(state.Journal);
    }
}
