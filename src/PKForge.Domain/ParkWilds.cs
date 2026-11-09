namespace PKForge.Domain;

/// <summary>A Pokémon the player let go into the Wilds, the wild lands beyond the park's woods and
/// shores. It remembers the player, and now and then it comes back to visit.</summary>
public sealed record ParkWildFriend(string Key, string Name, int Species, int Form, bool Shiny, IReadOnlyList<int> Types,
    ParkPersonality Personality, string Trainer, string Game, DateTimeOffset ReleasedAt, int Hearts = 0, int Visits = 0);

/// <summary>
/// The words around letting a Pokémon go and seeing it again: the goodbye, how it comes back
/// in from the wild lands, how it greets you, what it brings, and what people saw of it out there.
/// Lines depend on how long it has been, its types, its temperament and how close you were.
/// </summary>
public static class ParkWilds
{
    /// <summary>The most remembered at once; the oldest move on further when it is full.</summary>
    public const int Capacity = 200;

    /// <summary>The goodbye, when the player releases it to the Wilds.</summary>
    public static string Farewell(ParkWildFriend friend, Random random)
    {
        var name = friend.Name;
        var temperament = ParkLore.TemperamentOf(friend.Species, friend.Personality);
        if (friend.Hearts >= 4)
            return Pick(random,
                $"{name} will not go at first. It keeps coming back to your feet. In the end you walk it to the edge of the woods, and it slips between the trees without looking back, because looking back would be too hard.",
                $"{name} presses its head against your hand for a long moment, then turns and runs for the trees. At the edge of the woods it stops, and calls out once.");
        return temperament switch
        {
            ParkTemperament.Timid => Pick(random,
                $"{name} hesitates at the edge of the park, peeks at the wild lands, then at you. A gentle nod is all it needs. It tiptoes off into the Wilds.",
                $"{name} hides behind your legs for a while. Then something rustles out in the grass, something friendly, and it goes to see."),
            ParkTemperament.Fierce or ParkTemperament.Feisty => Pick(random,
                $"{name} does not need to be told twice. It charges off into the woods and you hear it roar somewhere far off, very pleased with itself.",
                $"{name} gives you one hard look, as if to say it was always going to be fine on its own, and strides out into the Wilds."),
            ParkTemperament.Aloof => $"{name} walks off without ceremony. At the edge of the trees it flicks its tail at you. That is a lot, for {name}.",
            ParkTemperament.Mischievous => $"{name} snatches one last berry from your bag on its way out, and you hear it laughing all the way across the field.",
            ParkTemperament.Lazy => $"{name} yawns, ambles to the edge of the woods, and lies down in the first patch of sun on the other side. The Wilds can wait until after its nap.",
            _ when friend.Types.Contains(ParkType.Water) => $"{name} swims out past the end of the pier, splashing as it goes, until it is only a ripple on the sea.",
            _ when friend.Types.Contains(ParkType.Flying) => $"{name} circles the Summit twice, low over your head, then climbs and climbs until it is a dot over the Wilds.",
            _ => Pick(random,
                $"{name} takes a few steps toward the trees, looks back at you once, then runs off to see what is out there.",
                $"{name} sniffs the air from the wild lands, wags its whole body, and bounds off into the Wilds.")
        };
    }

    /// <summary>Its arrival, for the journal.</summary>
    public static string Arrival(ParkWildFriend friend, DateOnly today) =>
        $"{friend.Name}, who you let go {Ago(friend, today)}, came back from the Wilds to visit.";

    /// <summary>What the player sees when it walks in: how the Wilds have changed it.</summary>
    public static string Greeting(ParkWildFriend friend, DateOnly today, Random random)
    {
        var name = friend.Name;
        var days = Days(friend, today);
        var change = days switch
        {
            < 2 => "It still looks a little lost out there, but it is fine.",
            < 14 => friend.Types.Contains(ParkType.Water) ? "It smells of the river." :
                friend.Types.Contains(ParkType.Grass) ? "A wildflower is caught on it." :
                friend.Types.Contains(ParkType.Fire) ? "There is soot on its nose, and it looks proud of it." :
                friend.Types.Contains(ParkType.Flying) ? "Its feathers are ruffled from the wind up high." : "There are burrs stuck all over it.",
            < 90 => "It is stronger now, and a little wilder. It has scars it did not have before, and stories with them.",
            _ => "It has been a long time. It is not the Pokémon you let go anymore, but its eyes light up when it sees you."
        };
        var greet = friend.Hearts >= 3
            ? Pick(random, $"It's {name}! It runs straight to you and nearly knocks you over.", $"It's {name}! It nuzzles your hand like it never left.")
            : Pick(random, $"It's {name}! It stops a few steps away and looks at you, then comes closer.", $"It's {name}! It sniffs your hand. It remembers.");
        return $"{greet} {change}";
    }

    /// <summary>A small thing from the Wilds, brought back for the player.</summary>
    public static ParkTreasure Gift(ParkWildFriend friend, Random random)
    {
        string[] finds = friend.Types.Contains(ParkType.Water) ? ["a pretty shell", "a smooth river stone", "a Mystic Water drop"]
            : friend.Types.Contains(ParkType.Grass) ? ["a Tiny Mushroom", "a four-leaf clover", "a Miracle Seed"]
            : friend.Types.Contains(ParkType.Flying) ? ["a Pretty Feather", "a twig for a nest", "a Sharp Beak"]
            : friend.Types.Contains(ParkType.Rock) || friend.Types.Contains(ParkType.Ground) ? ["a Hard Stone", "a Soft Sand pouch", "a shiny pebble"]
            : ["an Apricorn", "a Tiny Mushroom", "a wild Oran Berry"];
        return new ParkTreasure($"{finds[random.Next(finds.Length)]} (from {friend.Name}, back from the Wilds)", 2);
    }

    /// <summary>A sighting, for the story of your absence.</summary>
    public static string Sighting(ParkWildFriend friend, Random random) => friend.Name + " " + (friend.Types.Contains(ParkType.Water)
        ? Pick(random, "was seen swimming upstream with a school of wild Pokémon.", "was spotted sunning itself on a rock by the far river.")
        : friend.Types.Contains(ParkType.Flying) ? Pick(random, "was seen flying with a wild flock over the hills.", "was spotted on the Summit at dawn, watching the park.")
        : Pick(random, "was seen at the edge of the woods, watching the park for a while.", "was spotted out in the Wilds, leading a little group of wild Pokémon.",
            "left footprints at the edge of the woods. Nobody saw it come or go."));

    public static string Ago(ParkWildFriend friend, DateOnly today) => Days(friend, today) switch
    {
        0 => "earlier today",
        1 => "yesterday",
        < 14 and var d => $"{d} days ago",
        < 60 and var d => $"{d / 7} weeks ago",
        < 365 and var d => $"{d / 30} months ago",
        var d => d < 730 ? "over a year ago" : $"{d / 365} years ago"
    };

    /// <summary>Adds a newly released friend, keeping the most recent <see cref="Capacity"/>.</summary>
    public static IReadOnlyList<ParkWildFriend> Remember(IReadOnlyList<ParkWildFriend> wilds, ParkWildFriend friend) =>
        wilds.Where(w => w.Key != friend.Key).Append(friend).OrderByDescending(w => w.ReleasedAt).Take(Capacity).ToArray();

    private static int Days(ParkWildFriend friend, DateOnly today) => Math.Max(0, today.DayNumber - DateOnly.FromDateTime(friend.ReleasedAt.LocalDateTime).DayNumber);

    private static string Pick(Random random, params string[] lines) => lines[random.Next(lines.Length)];
}
