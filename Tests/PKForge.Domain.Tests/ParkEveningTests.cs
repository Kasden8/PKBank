using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class ParkEveningTests
{
    private static readonly ParkMap Home = ParkMap.Park;

    private static List<ParkMoment> Run(ParkLife life, long from, long until)
    {
        var moments = new List<ParkMoment>();
        for (var t = from; t <= until; t += 250) { life.Advance(t); moments.AddRange(life.TakeMoments()); }
        return moments;
    }

    private static ParkLife Evening(int seed, params ParkLifeResident[] residents)
    {
        var life = new ParkLife(Home, residents, seed) { Phase = ParkDayPhase.Evening, Weather = ParkWeather.Sunny };
        life.Restore(residents.Select(r => new ParkCareState(r.Id, Bond: 70) with { LastSeen = DateTimeOffset.Now }), DateTimeOffset.Now);
        return life;
    }

    [Fact]
    public void A_fire_type_lights_a_campfire_in_the_evening_and_the_others_sit_round_it()
    {
        var life = Evening(3,
            new("charmander", "Charmander", 4, [ParkType.Fire], ParkPersonality.Bold),
            new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm),
            new("bulbasaur", "Bulbasaur", 1, [ParkType.Grass], ParkPersonality.Gentle));
        var seated = false;
        for (long t = 0; t <= 40 * 60 * 1000 && !seated; t += 250)
        {
            life.Advance(t);
            seated = life.Campfire is not null && life.Actors.Any(a => a.Activity == "Sitting by the campfire");
        }
        Assert.True(seated);
        Assert.Equal("charmander", life.Campfire!.LighterId);
    }

    [Fact]
    public void Rain_puts_the_campfire_out_and_no_one_lights_it_in_the_rain()
    {
        var life = Evening(3, new ParkLifeResident("charmander", "Charmander", 4, [ParkType.Fire], ParkPersonality.Bold));
        long t = 0;
        for (; t <= 40 * 60 * 1000 && life.Campfire is null; t += 250) life.Advance(t);
        Assert.NotNull(life.Campfire);
        life.Weather = ParkWeather.Rain;
        Run(life, t, t + 30 * 60 * 1000);
        Assert.Null(life.Campfire);
    }

    [Fact]
    public void No_campfire_by_day()
    {
        var life = Evening(3, new ParkLifeResident("charmander", "Charmander", 4, [ParkType.Fire], ParkPersonality.Bold));
        life.Phase = ParkDayPhase.Day;
        for (long t = 0; t <= 30 * 60 * 1000; t += 250) { life.Advance(t); Assert.Null(life.Campfire); }
    }

    [Fact]
    public void A_trusting_resident_comes_when_called()
    {
        var life = Evening(1, new ParkLifeResident("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm));
        life.Phase = ParkDayPhase.Day;
        life.Restore([new ParkCareState("eevee", Bond: 95) with { LastSeen = DateTimeOffset.Now }], DateTimeOffset.Now);
        life.Advance(0);
        var target = ParkMap.Park.AtHome(200, 165);
        var reply = life.Call("eevee", target);
        Assert.True(reply.Accepted, reply.Line);
        Run(life, 250, 8_000);
        var eevee = life.Actors.Single();
        Assert.True(MathF.Abs(eevee.X - target.X) + MathF.Abs(eevee.Y - target.Y) < 16, $"{eevee.X},{eevee.Y} {eevee.Activity}");
    }

    [Fact]
    public void Unwell_comes_from_hunger_and_exhaustion_together()
    {
        Assert.True(ParkLife.IsUnwell(new ParkCareState("a", Hunger: 5, Energy: 10)));
        Assert.False(ParkLife.IsUnwell(new ParkCareState("a", Hunger: 5, Energy: 60)));
        Assert.False(ParkLife.IsUnwell(new ParkCareState("a", Hunger: 60, Energy: 5)));
    }

    [Fact]
    public void An_unwell_resident_says_so_and_slows_down()
    {
        var life = new ParkLife(Home, [new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm)], 4);
        life.Restore([new ParkCareState("eevee", Hunger: 4, Energy: 8) with { LastSeen = DateTimeOffset.Now }], DateTimeOffset.Now);
        life.Advance(0);
        Assert.Equal("Unwell", life.Actors.Single().Mood);
        Assert.Equal("Pain", ParkLife.PortraitFor("Unwell"));
    }
}

public sealed class ParkGoalsTests
{
    private static readonly DateOnly Day = new(2026, 10, 6);

    [Fact]
    public void Three_goals_a_day_the_same_all_day_and_different_the_next()
    {
        var today = ParkGoals.For(Day, 6, fighter: true);
        Assert.Equal(3, today.Goals.Count);
        Assert.Equal(3, today.Goals.Distinct().Count());
        Assert.Equal(today.Goals, ParkGoals.For(Day, 6, fighter: true).Goals);
        Assert.Contains(Enumerable.Range(1, 6), d => !ParkGoals.For(Day.AddDays(d), 6, true).Goals.SequenceEqual(today.Goals));
    }

    [Fact]
    public void Only_goals_this_park_can_do()
    {
        for (var d = 0; d < 60; d++)
        {
            var goals = ParkGoals.For(Day.AddDays(d), 1, fighter: false).Goals;
            Assert.DoesNotContain(goals, g => g.Deed is ParkDeed.Sparred or ParkDeed.RaceWon or ParkDeed.Pat or ParkDeed.Talked);
        }
    }

    [Fact]
    public void Patting_the_same_Pokemon_twice_counts_once()
    {
        var state = ParkGoals.For(Day, 5, true) with { Goals = [new(ParkDeed.Pat, 3, "Pat 3", true)], Progress = [0] };
        state = ParkGoals.Record(state, ParkDeed.Pat, "a", out _);
        state = ParkGoals.Record(state, ParkDeed.Pat, "a", out _);
        state = ParkGoals.Record(state, ParkDeed.Pat, "b", out var none);
        Assert.Empty(none);
        state = ParkGoals.Record(state, ParkDeed.Pat, "c", out var done);
        Assert.Single(done);
        Assert.True(state.AllDone);
    }

    [Fact]
    public void Saved_goals_come_back_the_same_day_and_reset_the_next()
    {
        var state = ParkGoals.For(Day, 6, true);
        state = ParkGoals.Record(state, state.Goals[0].Deed, "x", out _) with { Rewarded = true };
        var line = ParkGoals.Save(state);
        var back = ParkGoals.Load(line, Day, 6, true);
        Assert.Equal(state.Goals, back.Goals);
        Assert.Equal(state.Progress, back.Progress);
        Assert.Equal(state.Seen, back.Seen);
        Assert.True(back.Rewarded);
        Assert.False(ParkGoals.Load(line, Day.AddDays(1), 6, true).Rewarded);
        Assert.Equal(3, ParkGoals.Load("garbage", Day, 6, true).Goals.Count);
    }

    [Fact]
    public void A_full_day_sets_off_a_party()
    {
        var residents = new ParkLifeResident[] { new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm), new("pikachu", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Playful) };
        var life = new ParkLife(ParkMap.Park, residents, 2);
        life.Advance(0);
        life.Rejoice();
        life.Advance(1500);
        Assert.All(life.Actors, a => Assert.Equal("Celebrating with you", a.Activity));
    }
}

public sealed class ParkWildsTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    private static ParkWildFriend Friend(string key = "k1", int hearts = 0, int daysAgo = 3, int[]? types = null) =>
        new(key, "Bulby", 1, 0, false, types ?? [ParkType.Grass, ParkType.Poison], ParkPersonality.Gentle, "Lyra", "HeartGold",
            new DateTimeOffset(Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(12, 0))), hearts);

    [Fact]
    public void An_old_friend_comes_back_through_the_arch_and_never_runs()
    {
        var life = new ParkLife(ParkMap.Park, [new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm)], 5)
            { OldFriends = [Friend()], Today = Today };
        ParkActor? back = null;
        for (long t = 0; t <= 6 * 60 * 60 * 1000 && back is null; t += 250)
        {
            life.Advance(t);
            back = life.Actors.FirstOrDefault(a => life.OldFriendOf(a.Resident.Id) is not null);
        }
        Assert.NotNull(back);
        Assert.Equal("Bulby", back!.Resident.Name);
        for (var i = 0; i < 20; i++)
        {
            var reply = life.Befriend(back.Resident.Id);
            Assert.True(reply.Accepted);
            Assert.Contains("Bulby", reply.Line);
            Assert.NotNull(reply.Gift);
        }
    }

    [Fact]
    public void Lines_follow_time_types_and_closeness()
    {
        var random = new Random(1);
        Assert.Contains("wildflower", ParkWilds.Greeting(Friend(daysAgo: 5), Today, random));
        Assert.Contains("river", ParkWilds.Greeting(Friend(daysAgo: 5, types: [ParkType.Water]), Today, random));
        Assert.Contains("long time", ParkWilds.Greeting(Friend(daysAgo: 400), Today, random));
        Assert.Equal("yesterday", ParkWilds.Ago(Friend(daysAgo: 1), Today));
        Assert.Equal("3 weeks ago", ParkWilds.Ago(Friend(daysAgo: 21), Today));
        Assert.Contains("will not go", ParkWilds.Farewell(Friend(hearts: 5), new Random(0)) + ParkWilds.Farewell(Friend(hearts: 5), new Random(3)));
    }

    [Fact]
    public void The_wilds_keep_the_most_recent_and_no_duplicates()
    {
        IReadOnlyList<ParkWildFriend> wilds = [];
        for (var i = 0; i < ParkWilds.Capacity + 20; i++) wilds = ParkWilds.Remember(wilds, Friend($"k{i}", daysAgo: 0) with { ReleasedAt = DateTimeOffset.UnixEpoch.AddDays(i) });
        Assert.Equal(ParkWilds.Capacity, wilds.Count);
        Assert.DoesNotContain(wilds, w => w.Key == "k0");
        wilds = ParkWilds.Remember(wilds, Friend("k210") with { ReleasedAt = DateTimeOffset.UnixEpoch.AddDays(1000) });
        Assert.Single(wilds, w => w.Key == "k210");
    }
}

public sealed class ParkDaycareTests
{
    private static ParkLife Daycare(int seed, bool breed)
    {
        var life = new ParkLife(ParkMap.Park, [
            new("ditto", "Ditto", 132, [ParkType.Normal], ParkPersonality.Calm),
            new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Playful),
            new("pikachu", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Playful)], seed)
        { DaycareOpen = true, DaycareGuests = ["ditto", "eevee"] };
        if (breed) life.CanBreed = (a, b) => true;
        return life;
    }

    [Fact]
    public void Blissey_and_Audino_keep_the_daycare_and_are_not_residents()
    {
        var life = Daycare(1, breed: false);
        for (long t = 0; t <= 60_000; t += 250) life.Advance(t);
        var keepers = life.Actors.Where(a => ParkLife.IsKeeper(a.Resident.Id)).Select(a => a.Resident.Name).Order().ToArray();
        Assert.Equal(["Audino", "Blissey"], keepers);
        Assert.DoesNotContain(life.Snapshot(DateTimeOffset.Now), c => ParkLife.IsKeeper(c.Id));
        Assert.Equal(ParkReply.None, life.Befriend(keepers.Length > 0 ? ParkLife.KeeperPrefix + "blissey" : ""));
        life.DaycareOpen = false;
        for (long t = 60_250; t <= 62_000; t += 250) life.Advance(t);
        Assert.DoesNotContain(life.Actors, a => ParkLife.IsKeeper(a.Resident.Id));
    }

    [Fact]
    public void Two_guests_who_can_breed_leave_an_egg_that_Blissey_keeps_for_you()
    {
        var life = Daycare(2, breed: true);
        long t = 0;
        for (; t <= 6 * 60 * 60 * 1000 && life.Egg is null; t += 250) life.Advance(t);
        Assert.Equal(("ditto", "eevee"), life.Egg);
        Assert.Contains(life.TakeMoments(), m => m.Text?.Contains("found an egg") == true);
        life.TakeEgg();
        Assert.Null(life.Egg);
    }

    [Fact]
    public void No_egg_without_the_game_saying_they_can()
    {
        var life = Daycare(2, breed: false);
        for (long t = 0; t <= 3 * 60 * 60 * 1000; t += 500) life.Advance(t);
        Assert.Null(life.Egg);
    }

    [Fact]
    public void Guests_are_fed_by_the_keepers()
    {
        var life = Daycare(3, breed: false);
        life.Restore([new ParkCareState("ditto", Hunger: 5) with { LastSeen = DateTimeOffset.Now }], DateTimeOffset.Now);
        for (long t = 0; t <= 2000; t += 250) life.Advance(t);
        Assert.True(life["ditto"]!.Care.Hunger >= 30);
    }
}
