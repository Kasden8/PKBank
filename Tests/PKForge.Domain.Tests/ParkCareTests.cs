using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>
/// Needs, the bond and the day: needs fall and come back, a hungry Pokémon goes for berries,
/// the park goes to bed at night (four in the cabin) and wakes in the morning, the player's
/// attention builds a bond capped per day, and rivals quarrel.
/// </summary>
public sealed class ParkCareTests
{
    private static readonly ParkMap Home = ParkMap.Park;

    private static readonly ParkLifeResident[] All =
    [
        new("pikachu", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Playful),
        new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Curious),
        new("bulbasaur", "Bulbasaur", 1, [ParkType.Grass], ParkPersonality.Gentle),
        new("charmander", "Charmander", 4, [ParkType.Fire], ParkPersonality.Bold),
        new("meowth", "Meowth", 52, [ParkType.Normal], ParkPersonality.Proud),
        new("riolu", "Riolu", 447, [ParkType.Fighting], ParkPersonality.Balanced),
        new("squirtle", "Squirtle", 7, [ParkType.Water], ParkPersonality.Calm),
    ];

    private static ParkLifeResident[] Residents(int count) => All[..count];

    [Fact]
    public void Needs_fall_with_time_and_come_back()
    {
        var care = new ParkCareState("a");
        var hour = care.Live(ParkPose.Idle, false, 3600);
        Assert.Equal(80 - ParkCare.HungerPerHour, hour.Hunger, 1);
        Assert.True(hour.Live(ParkPose.Eat, false, 10).Hunger > hour.Hunger + 30);
        Assert.True(hour.Live(ParkPose.Sleep, false, 30).Energy > hour.Energy + 20);
    }

    [Fact]
    public void Away_residents_look_after_themselves_but_miss_the_fun()
    {
        var away = new ParkCareState("a", Hunger: 90, Energy: 20, Fun: 90, Social: 90, Bond: 60).Away(TimeSpan.FromDays(4), companions: 0);
        Assert.Equal(35, away.Hunger);
        Assert.Equal(70, away.Energy);
        Assert.Equal(15, away.Fun);
        Assert.Equal(10, away.Social);
        Assert.InRange(away.Bond, 20, 59);
        Assert.Equal("Lonely", away.Mood());
    }

    [Fact]
    public void The_bond_grows_at_most_twenty_a_day()
    {
        var today = new DateOnly(2026, 10, 6);
        var care = new ParkCareState("a");
        for (var i = 0; i < 30; i++) care = care.AddBond(4, today);
        Assert.Equal(20, care.Bond);
        Assert.Equal(1, care.Hearts());
        care = care.AddBond(4, today.AddDays(1));
        Assert.Equal(24, care.Bond);
    }

    [Fact]
    public void A_hungry_pokemon_goes_for_berries()
    {
        var life = new ParkLife(Home, Residents(3), seed: 4);
        life.Restore([new ParkCareState("pikachu", Hunger: 5) with { LastSeen = DateTimeOffset.Now }], DateTimeOffset.Now);
        var ate = false;
        for (long t = 0; t < 3 * 60 * 1000 && !ate; t += 200)
        {
            life.Advance(t);
            ate = life["pikachu"]!.Pose == ParkPose.Eat;
        }
        Assert.True(ate);
        Assert.Equal("Hungry", new ParkCareState("x", Hunger: 5).Mood());
    }

    [Fact]
    public void At_night_the_park_sleeps_and_wakes_in_the_morning()
    {
        var life = new ParkLife(Home, Residents(7), seed: 9) { Phase = ParkDayPhase.Night };
        for (long t = 0; t <= 5 * 60 * 1000; t += 200) life.Advance(t);
        Assert.True(life.Actors.Count(a => a.IsAsleep) >= 6, string.Join(", ", life.Actors.Select(a => a.Activity)));
        Assert.InRange(life.Actors.Count(a => a.Indoors), 1, ParkLife.CabinBeds);

        life.Phase = ParkDayPhase.Morning;
        foreach (var actor in life.Actors) life.Restore([actor.Care with { Energy = 100 }], DateTimeOffset.Now);
        for (long t = 5 * 60 * 1000; t <= 12 * 60 * 1000; t += 200) life.Advance(t);
        Assert.Equal(0, life.Actors.Count(a => a.Indoors));
    }

    [Fact]
    public void Waking_a_sleeper_brings_it_out_of_the_cabin()
    {
        var life = new ParkLife(Home, Residents(7), seed: 9) { Phase = ParkDayPhase.Night };
        for (long t = 0; t <= 5 * 60 * 1000; t += 200) life.Advance(t);
        var sleeper = life.Actors.First(a => a.Indoors);
        life.Command(sleeper.Resident.Id, ParkCommand.Talk);
        life.Advance(5 * 60 * 1000 + 200);
        Assert.False(sleeper.Indoors);
        Assert.True(Home.CanOccupy(sleeper.X, sleeper.Y, ParkTraversal.Land));
    }

    [Fact]
    public void Time_with_the_player_builds_the_bond()
    {
        var life = new ParkLife(Home, Residents(2), seed: 1) { Today = new DateOnly(2026, 10, 6) };
        life.Advance(1000);
        life.Command("eevee", ParkCommand.Snack);
        for (long t = 1000; t < 12_000; t += 100) life.Advance(t);
        Assert.Equal(4, life["eevee"]!.Care.Bond);
    }

    [Fact]
    public void Friends_share_moments_and_rivals_quarrel()
    {
        var life = new ParkLife(Home, Residents(6), seed: 2) { Relation = (_, _) => (0, 100) };
        var moments = new List<ParkMoment>();
        for (long t = 0; t < 60 * 60 * 1000; t += 250)
        {
            life.Advance(t);
            moments.AddRange(life.TakeMoments());
        }
        Assert.Contains(moments, m => m.Kind == ParkSocialEventKind.Conflict);

        var friends = new ParkLife(Home, Residents(6), seed: 2) { Relation = (_, _) => (80, 0) };
        moments.Clear();
        for (long t = 0; t < 60 * 60 * 1000; t += 250)
        {
            friends.Advance(t);
            moments.AddRange(friends.TakeMoments());
        }
        // Friends never quarrel (a troll's mischief, which has its own story, is another matter).
        Assert.DoesNotContain(moments, m => m.Kind == ParkSocialEventKind.Conflict && m.Text is null);
        Assert.Contains(moments, m => m.Kind is ParkSocialEventKind.Bond or ParkSocialEventKind.Play);
    }
}

/// <summary>What the player does with a resident: berries by taste, head pats, fetch.</summary>
public sealed class ParkPlayTests
{
    private static readonly ParkMap Home = ParkMap.Park;

    private static ParkLife Life() => new(Home,
        [new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Curious),
         new("pikachu", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Playful)], seed: 3) { Today = new DateOnly(2026, 10, 6) };

    private static List<ParkPose> Watch(ParkLife life, string id, long from, long until)
    {
        var poses = new List<ParkPose>();
        for (var t = from; t <= until; t += 100)
        {
            life.Advance(t);
            if (poses.Count == 0 || poses[^1] != life[id]!.Pose) poses.Add(life[id]!.Pose);
        }
        return poses;
    }

    [Fact]
    public void A_favorite_berry_makes_it_dance_and_a_disliked_one_pull_a_face()
    {
        // Modest (nature 15) raises Sp. Atk: likes Dry (3), dislikes Spicy (0).
        Assert.Equal(1, ParkTaste.Reaction(15, 3));
        Assert.Equal(-1, ParkTaste.Reaction(15, 0));
        Assert.Equal("loves Dry · dislikes Spicy", ParkTaste.Describe(15));
        Assert.Equal("eats anything", ParkTaste.Describe(0));

        var life = Life();
        life.Advance(1000);
        Assert.Equal(1, life.Feed("eevee", 3, 15).Taste);
        Assert.Contains(ParkPose.Dance, Watch(life, "eevee", 1000, 12_000));
        Assert.Equal(8, life["eevee"]!.Care.Bond);

        Assert.Equal(-1, life.Feed("pikachu", 0, 15).Taste);
        Assert.Contains(ParkPose.Cringe, Watch(life, "pikachu", 12_000, 20_000));
    }

    [Fact]
    public void Fetch_runs_after_the_ball_and_brings_it_back()
    {
        var life = Life();
        life.Advance(1000);
        var eevee = life["eevee"]!;
        var home = (eevee.X, eevee.Y);
        Assert.True(life.Fetch("eevee", ParkMap.Park.AtHome(168, 150)).Accepted);
        bool thrown = false, held = false;
        for (long t = 1000; t < 60_000 && !(thrown && life.Ball is null); t += 100)
        {
            life.Advance(t);
            thrown |= life.Ball is not null;
            held |= life.Ball?.HolderId == "eevee";
        }
        Assert.True(held);
        Assert.Null(life.Ball);
        Assert.InRange(eevee.X, home.X - 1, home.X + 1);
        Assert.True(eevee.Care.Bond >= 4);
    }

    [Fact]
    public void A_pat_on_a_sleeper_does_not_wake_it()
    {
        var life = new ParkLife(Home, [new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm)], seed: 1) { Phase = ParkDayPhase.Night };
        for (long t = 0; t <= 120_000; t += 200) life.Advance(t);
        var eevee = life["eevee"]!;
        Assert.True(eevee.IsAsleep);
        life.Pet("eevee");
        life.Advance(120_200);
        Assert.True(eevee.IsAsleep);
        Assert.Equal(ParkEmote.Heart, eevee.Emote);
    }
}
