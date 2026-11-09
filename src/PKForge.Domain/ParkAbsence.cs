namespace PKForge.Domain;

/// <summary>
/// The story of what happened while the player was away, built from what the park knows: the
/// weather of those hours, the nights, who got hungry or bored, lovers, rivals and the hunted,
/// the troublemakers, wild visitors, and who kept looking at the arch. A handful of lines, oldest
/// first, each with when it happened. Moments between two residents also count in their bond.
/// </summary>
public static class ParkAbsence
{
    public sealed record Story(string Text, ParkMoment? Moment = null);

    public static readonly TimeSpan Minimum = TimeSpan.FromMinutes(20);

    private static readonly string[] Wild = ["Pidgey", "Bidoof", "Rattata", "Hoothoot", "Zubat", "Oddish", "Sentret", "Starly", "Lillipup", "Wooper"];

    /// <param name="before">Needs when the player left, by resident id.</param>
    /// <param name="now">Needs now, after the time away.</param>
    public static IReadOnlyList<Story> Tell(IReadOnlyList<ParkLifeResident> residents, IReadOnlyDictionary<string, ParkCareState> before,
        IReadOnlyDictionary<string, ParkCareState> now, DateTime left, DateTime back, Random random, IReadOnlyList<ParkWildFriend>? wilds = null)
    {
        var away = back - left;
        if (away < Minimum || residents.Count == 0) return [];
        var stories = new List<(DateTime At, Story Story, int Weight)>();
        DateTime Somewhen() => left + TimeSpan.FromTicks((long)(away.Ticks * (.1 + random.NextDouble() * .8)));
        T Pick<T>(IReadOnlyList<T> list) => list[random.Next(list.Count)];
        void Add(DateTime at, string text, int weight, ParkMoment? moment = null) => stories.Add((at, new Story($"{When(at, back)}, {text}", moment), weight));

        // The weather of the hours away.
        var wet = Enumerable.Range(0, (int)Math.Min(12, away.TotalHours / 6 + 1)).Select(i => left.AddHours(i * 6))
            .FirstOrDefault(t => ParkCare.WeatherAt(t) is ParkWeather.Rain or ParkWeather.Storm);
        if (wet != default)
        {
            var fire = residents.Where(r => r.Types.Contains(ParkType.Fire)).ToList();
            var water = residents.Where(r => r.Swims).ToList();
            var storm = ParkCare.WeatherAt(wet) == ParkWeather.Storm;
            var text = storm ? "a storm rolled over the park." : "it rained.";
            if (fire.Count > 0) text += $" {Pick(fire).Name} ran for the cabin and would not come out.";
            if (water.Count > 0) text += $" {Pick(water).Name} danced in the puddles the whole time.";
            Add(wet.AddHours(2), text, 3);
        }

        // A night went by.
        if (away.TotalHours >= 8 || left.Hour >= 18 && back.Hour < 12 || back.Date > left.Date)
        {
            var night = back.Hour < 12 ? back.Date.AddHours(-1) : back.Date.AddHours(-24 + 23);
            var owls = residents.Where(r => r.Has(ParkQuirk.Nocturnal) || ParkLife.IsTroll(r)).ToList();
            Add(night, owls.Count > 0
                ? $"everyone went to bed, except {Pick(owls).Name}, who spent the night keeping the others awake."
                : "the park went to sleep. A few snored. Nobody will admit to it.", 2);
        }

        // Needs that ran low.
        foreach (var resident in residents)
        {
            if (!now.TryGetValue(resident.Id, out var care) || !before.TryGetValue(resident.Id, out var was)) continue;
            if (care.Hunger < 40 && was.Hunger >= 40)
                Add(Somewhen(), $"{resident.Name} got hungry and went looking for berries. It has its eye on your bag now.", 2);
            else if (care.Fun < 25)
                Add(back.AddHours(-1), $"{resident.Name} ran out of things to do and has been sighing loudly ever since.", 2);
            if (away.TotalHours >= 48 && was.Bond >= 40)
                Add(Somewhen(), $"{resident.Name} kept looking down the path you always come by. It missed you.", 4);
        }

        // Pairs by species.
        for (var i = 0; i < residents.Count; i++)
        for (var j = 0; j < residents.Count; j++)
        {
            if (i == j) continue;
            ParkLifeResident a = residents[i], b = residents[j];
            var at = Somewhen();
            switch (ParkLore.Feeling(a.Species, b.Species, Stable(a.Id + b.Id)))
            {
                case ParkPairKind.Love when i < j:
                    Add(at, $"{a.Name} and {b.Name} spent the afternoon side by side by the lake.", 3,
                        new ParkMoment(ParkSocialEventKind.Bond, a.Id, b.Id, $"{a.Name} and {b.Name} spent the afternoon side by side by the lake."));
                    break;
                case ParkPairKind.Rivals when i < j:
                    var winner = random.Next(2) == 0 ? a : b;
                    var loser = winner == a ? b : a;
                    Add(at, $"{a.Name} and {b.Name} fought again. {winner.Name} won, and {loser.Name} has been sulking since.", 3,
                        new ParkMoment(ParkSocialEventKind.Conflict, a.Id, b.Id, $"{a.Name} and {b.Name} fought again. {winner.Name} won."));
                    break;
                case ParkPairKind.Fears:
                    Add(at, $"{a.Name} spent hours hiding from {b.Name} behind the statue.", 2);
                    break;
            }
        }

        // The troublemakers.
        var trolls = residents.Where(ParkLife.IsTroll).ToList();
        if (trolls.Count > 0 && residents.Count > 1)
        {
            var troll = Pick(trolls);
            var victim = Pick(residents.Where(r => r != troll).ToList());
            var deed = random.Next(3) switch
            {
                0 => $"{troll.Name} stole {victim.Name}'s berry and ate it in front of it.",
                1 => $"{troll.Name} woke {victim.Name} up three times just to see its face.",
                _ => $"{troll.Name} hid {victim.Name}'s favorite spot under a pile of leaves."
            };
            Add(Somewhen(), deed + $" {victim.Name} has not forgiven it.", 3,
                new ParkMoment(ParkSocialEventKind.Conflict, victim.Id, troll.Id, deed));
        }

        // Habits.
        foreach (var resident in residents)
        {
            if (resident.Has(ParkQuirk.Lullaby) && residents.Count > 1)
                Add(Somewhen(), $"{resident.Name} sang its song. Everyone fell asleep before the end, and {resident.Name} was furious.", 2);
            if (resident.Has(ParkQuirk.CoinFinder))
                Add(Somewhen(), $"{resident.Name} dug up a shiny coin and buried it again somewhere secret.", 1);
            if (resident.Has(ParkQuirk.MoonDancer) && away.TotalHours >= 8)
                Add(back.Date.AddHours(-2), $"{resident.Name} danced by the statue under the moon.", 1);
        }

        // News of a Pokémon let go into the Wilds.
        if (wilds is { Count: > 0 } && away.TotalHours >= 2 && random.Next(2) == 0)
            Add(Somewhen(), ParkWilds.Sighting(Pick(wilds), random), 3);

        // A wild visitor.
        if (away.TotalHours >= 3 && random.Next(3) > 0)
            Add(Somewhen(), $"a wild {Pick(Wild)} wandered in, looked around, and left again.", 1);

        return stories.OrderByDescending(s => s.Weight + random.NextDouble()).Take(5)
            .OrderBy(s => s.At).Select(s => s.Story).ToList();
    }

    private static int Stable(string text)
    {
        uint hash = 2166136261;
        foreach (var letter in text) hash = (hash ^ letter) * 16777619;
        return (int)(hash & 0x7fffffff);
    }

    /// <summary>When something happened, as people say it.</summary>
    public static string When(DateTime at, DateTime now)
    {
        var day = (now.Date - at.Date).Days;
        var part = at.Hour switch { < 6 => "night", < 12 => "morning", < 18 => "afternoon", _ => "evening" };
        return day switch
        {
            0 when part == "night" => "Last night",
            0 => $"This {part}",
            1 when part is "evening" or "night" => "Last night",
            1 => $"Yesterday {part}",
            _ => $"{day} days ago"
        };
    }
}
