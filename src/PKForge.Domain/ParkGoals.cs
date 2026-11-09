namespace PKForge.Domain;

/// <summary>Something the player did in the park that a daily goal can count.</summary>
public enum ParkDeed { Pat, LovedBerry, Fetch, Found, RaceWon, Brushed, Sparred, Dug, Called, Talked }

public sealed record ParkGoal(ParkDeed Deed, int Count, string Text, bool Distinct = false);

/// <summary>Today's goals and how far along they are. <see cref="Seen"/> holds "deed:id" for goals
/// that count different Pokémon.</summary>
public sealed record ParkGoalsState(DateOnly Day, IReadOnlyList<ParkGoal> Goals, IReadOnlyList<int> Progress, IReadOnlyList<string> Seen, bool Rewarded)
{
    public bool Done(int index) => Progress[index] >= Goals[index].Count;
    public bool AllDone => Goals.Count > 0 && Enumerable.Range(0, Goals.Count).All(Done);
}

/// <summary>
/// Three small goals a day, the same for the whole day and different the next, picked among what
/// this park can actually do (no training without a Fighting type, no race alone). Finishing all
/// three throws a party and leaves a rare find.
/// </summary>
public static class ParkGoals
{
    public const int PerDay = 3;

    private static readonly ParkGoal[] Pool =
    [
        new(ParkDeed.Pat, 3, "Pat 3 different Pokémon", Distinct: true),
        new(ParkDeed.LovedBerry, 1, "Give a Pokémon the berry it loves"),
        new(ParkDeed.Fetch, 2, "Play fetch twice"),
        new(ParkDeed.Found, 1, "Find a Pokémon at hide and seek"),
        new(ParkDeed.RaceWon, 1, "Cheer a Pokémon to victory in a race"),
        new(ParkDeed.Brushed, 1, "Brush a Pokémon until its coat shines"),
        new(ParkDeed.Sparred, 1, "Go all the rounds of a training session"),
        new(ParkDeed.Dug, 1, "Dig up a real treasure"),
        new(ParkDeed.Called, 2, "Call a Pokémon over to you twice"),
        new(ParkDeed.Talked, 4, "Chat with 4 different Pokémon", Distinct: true),
    ];

    /// <summary>The rare finds handed out for a full day, in turn.</summary>
    public static readonly ParkTreasure[] Rewards =
    [
        new("a Star Piece", 3), new("a Pearl", 2), new("a Shiny Stone", 3), new("a Big Mushroom", 2),
        new("a Stardust", 2), new("a Dusk Stone", 3), new("a Heart Scale", 2), new("a Rare Bone", 3),
    ];

    /// <summary>Today's goals for a park with these residents.</summary>
    public static ParkGoalsState For(DateOnly day, int residents, bool fighter)
    {
        var possible = Pool.Where(goal => goal.Deed switch
        {
            ParkDeed.Sparred => fighter,
            ParkDeed.RaceWon => residents >= 2,
            ParkDeed.Pat or ParkDeed.Talked => residents >= goal.Count,
            _ => residents >= 1
        }).ToList();
        var random = new Random(day.DayNumber * 7919);
        var goals = possible.OrderBy(_ => random.Next()).Take(PerDay).ToList();
        return new(day, goals, goals.Select(_ => 0).ToArray(), [], false);
    }

    /// <summary>Counts a deed. <paramref name="completed"/> lists the goals it just finished.</summary>
    public static ParkGoalsState Record(ParkGoalsState state, ParkDeed deed, string id, out IReadOnlyList<ParkGoal> completed)
    {
        var done = new List<ParkGoal>();
        completed = done;
        var progress = state.Progress.ToArray();
        var seen = state.Seen.ToList();
        for (var i = 0; i < state.Goals.Count; i++)
        {
            var goal = state.Goals[i];
            if (goal.Deed != deed || progress[i] >= goal.Count) continue;
            if (goal.Distinct)
            {
                var key = $"{deed}:{id}";
                if (seen.Contains(key)) continue;
                seen.Add(key);
            }
            progress[i]++;
            if (progress[i] >= goal.Count) done.Add(goal);
        }
        return state with { Progress = progress, Seen = seen };
    }

    /// <summary>The finished game, as a deed, if it counts.</summary>
    public static ParkDeed? DeedOf(ParkGame game) => game switch
    {
        { Kind: ParkGameKind.Race } when game.Finished.Count > 0 && game.Finished[0] == game.HostId && game.Players.Count > 1 => ParkDeed.RaceWon,
        { Kind: ParkGameKind.Brush, Progress: >= 1 } => ParkDeed.Brushed,
        { Kind: ParkGameKind.Spar } when game.Round >= ParkLife.SparRounds => ParkDeed.Sparred,
        { Kind: ParkGameKind.Dig, Treasure.Rarity: > 0 } => ParkDeed.Dug,
        _ => null
    };

    /// <summary>The reward for a full day: a rare find that changes from one day to the next.</summary>
    public static ParkTreasure RewardFor(DateOnly day) => Rewards[day.DayNumber % Rewards.Length];

    /// <summary>Saved as one line: day | goal indexes | progress | seen | rewarded.</summary>
    public static string Save(ParkGoalsState state) => string.Join("|",
        state.Day.ToString("yyyy-MM-dd"), string.Join(",", state.Goals.Select(g => Array.IndexOf(Pool, g))),
        string.Join(",", state.Progress), string.Join(",", state.Seen), state.Rewarded ? "1" : "0");

    /// <summary>Today's state from a saved line, or fresh goals when it is from another day or unreadable.</summary>
    public static ParkGoalsState Load(string? saved, DateOnly today, int residents, bool fighter)
    {
        var parts = saved?.Split('|');
        if (parts is not { Length: 5 } || !DateOnly.TryParse(parts[0], out var day) || day != today) return For(today, residents, fighter);
        try
        {
            var goals = parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).Select(i => Pool[i]).ToArray();
            var progress = parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
            if (goals.Length != progress.Length) return For(today, residents, fighter);
            return new(day, goals, progress, parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries), parts[4] == "1");
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException or OverflowException)
        {
            return For(today, residents, fighter);
        }
    }
}
