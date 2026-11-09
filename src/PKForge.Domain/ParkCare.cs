namespace PKForge.Domain;

public enum ParkNeed { Hunger, Energy, Fun, Social }

/// <summary>The part of the day, from the real clock: the park sleeps at night.</summary>
public enum ParkDayPhase { Morning, Day, Evening, Night }

/// <summary>The sky over the park, the same for everyone on a given date and time of day.</summary>
public enum ParkWeather { Sunny, Cloudy, Rain, Storm }

/// <summary>What a resident needs, 0 (none left) to 100 (full), and its bond with the player
/// (0–100, five hearts). Saved between visits.</summary>
public sealed record ParkCareState(string Id, float Hunger = 80, float Energy = 80, float Fun = 70, float Social = 70,
    float Bond = 0, DateTimeOffset LastSeen = default, DateOnly BondDay = default, float BondToday = 0, DateOnly GiftDay = default,
    int SparBest = 0, int SparCount = 0, int TasteKnown = 0);

/// <summary>The rules of care: how needs fall with time, what each pose gives back, how the
/// player's attention builds the bond, and the mood that comes out of it all.</summary>
public static class ParkCare
{
    /// <summary>Points lost per hour while awake in the park.</summary>
    public const float HungerPerHour = 12, EnergyPerHour = 9, FunPerHour = 14, SocialPerHour = 10;
    /// <summary>The most bond the player can build in one day.</summary>
    public const float BondPerDay = 20;
    public const float Low = 25;

    /// <summary>The weather for a date and a six-hour block of it: mostly fair, sometimes rain,
    /// now and then a storm. It holds for the block, so the park does not flicker between skies.</summary>
    public static ParkWeather WeatherAt(DateTime local)
    {
        var seed = local.Year * 10_000 + local.DayOfYear * 10 + local.Hour / 6;
        var roll = new Random(seed).Next(100);
        return roll < 50 ? ParkWeather.Sunny : roll < 75 ? ParkWeather.Cloudy : roll < 94 ? ParkWeather.Rain : ParkWeather.Storm;
    }

    public static ParkDayPhase PhaseAt(DateTime local) => local.Hour switch
    {
        >= 6 and < 9 => ParkDayPhase.Morning,
        >= 9 and < 18 => ParkDayPhase.Day,
        >= 18 and < 21 => ParkDayPhase.Evening,
        _ => ParkDayPhase.Night
    };

    public static float Get(this ParkCareState care, ParkNeed need) => need switch
    {
        ParkNeed.Hunger => care.Hunger, ParkNeed.Energy => care.Energy, ParkNeed.Fun => care.Fun, _ => care.Social
    };

    public static ParkCareState Add(this ParkCareState care, ParkNeed need, float amount) => need switch
    {
        ParkNeed.Hunger => care with { Hunger = Clamp(care.Hunger + amount) },
        ParkNeed.Energy => care with { Energy = Clamp(care.Energy + amount) },
        ParkNeed.Fun => care with { Fun = Clamp(care.Fun + amount) },
        _ => care with { Social = Clamp(care.Social + amount) }
    };

    /// <summary>The player's attention, at most <see cref="BondPerDay"/> a day so one long visit
    /// cannot buy five hearts.</summary>
    public static ParkCareState AddBond(this ParkCareState care, float amount, DateOnly today)
    {
        var spent = care.BondDay == today ? care.BondToday : 0;
        var gain = Math.Clamp(amount, 0, Math.Max(0, BondPerDay - spent));
        return care with { Bond = Math.Clamp(care.Bond + gain, 0, 100), BondDay = today, BondToday = spent + gain };
    }

    /// <summary>Hearts shown for the bond, 0 to 5.</summary>
    public static int Hearts(this ParkCareState care) => (int)Math.Floor(care.Bond / 20);

    /// <summary>A moment of park life: needs fall, and the current pose gives some back.</summary>
    public static ParkCareState Live(this ParkCareState care, ParkPose pose, bool swimming, float seconds)
    {
        var hours = seconds / 3600;
        var asleep = pose is ParkPose.Sleep or ParkPose.Lie;
        var hunger = care.Hunger - HungerPerHour * hours * (asleep ? .4f : 1);
        var energy = care.Energy - EnergyPerHour * hours * (asleep ? 0 : 1);
        var fun = care.Fun - FunPerHour * hours * (asleep ? .2f : 1);
        var social = care.Social - SocialPerHour * hours * (asleep ? .2f : 1);
        switch (pose)
        {
            case ParkPose.Eat: hunger += 4 * seconds; break;
            case ParkPose.Sleep: energy += .8f * seconds; break;
            case ParkPose.Lie or ParkPose.Sit: energy += .25f * seconds; fun += .1f * seconds; break;
            case ParkPose.Dance or ParkPose.Spin or ParkPose.Hop or ParkPose.Attack or ParkPose.Hurt: fun += 1.5f * seconds; energy -= .1f * seconds; break;
            case ParkPose.Float: fun += .8f * seconds; energy += .1f * seconds; break;
        }
        if (swimming && pose == ParkPose.Walk) fun += .6f * seconds;
        return care with { Hunger = Clamp(hunger), Energy = Clamp(energy), Fun = Clamp(fun), Social = Clamp(social) };
    }

    /// <summary>The hours the app was closed. The residents look after themselves: they eat and
    /// sleep in the park, so hunger and energy only sink to a comfortable floor, while fun and
    /// company fade further without the player (a little less with friends around).</summary>
    public static ParkCareState Away(this ParkCareState care, TimeSpan elapsed, int companions)
    {
        var hours = (float)Math.Clamp(elapsed.TotalHours, 0, 72);
        float Toward(float value, float floor, float perHour) => value <= floor ? value : Math.Max(floor, value - perHour * hours);
        return care with
        {
            Hunger = Toward(care.Hunger, 35, 6),
            Energy = care.Energy < 70 ? Math.Min(70, care.Energy + 8 * hours) : Toward(care.Energy, 70, 4),
            Fun = Toward(care.Fun, 15, 5),
            Social = Toward(care.Social, companions > 0 ? 45 : 10, companions > 0 ? 3 : 5),
            // Missed for a few days, the bond fades a little: never below one heart once earned.
            Bond = hours > 48 ? Math.Max(Math.Min(care.Bond, 20), care.Bond - (hours - 48) / 24 * 3) : care.Bond
        };
    }

    /// <summary>The mood its needs give: a need running out comes first.</summary>
    public static string Mood(this ParkCareState care)
    {
        if (care.Energy < Low) return "Sleepy";
        if (care.Hunger < Low) return "Hungry";
        if (care.Social < Low) return "Lonely";
        if (care.Fun < Low) return "Bored";
        var average = (care.Hunger + care.Energy + care.Fun + care.Social) / 4;
        return average > 75 ? "Joyful" : average > 55 ? "Happy" : "Content";
    }

    /// <summary>How much more a need-meeting activity appeals: 1 when the need is fine, up to 5
    /// when it is empty, and less than 1 when it is already full.</summary>
    public static float Urge(float value) => value > 90 ? .25f : 1 + 4 * MathF.Pow(Math.Max(0, (60 - value) / 60), 2);

    private static float Clamp(float value) => Math.Clamp(value, 0, 100);
}

/// <summary>Berry tastes, the games' way: a nature likes the flavor of the stat it raises and
/// dislikes the one it lowers.</summary>
public static class ParkTaste
{
    /// <summary>One berry per flavor, in flavor order: Spicy, Sour… as the games pair them.</summary>
    public static readonly IReadOnlyList<string> Berries = ["Cheri", "Aspear", "Pecha", "Chesto", "Rawst"];

    /// <summary>The nature to use: the real one when known, else one the park picks from the id.</summary>
    public static int NatureOrGuess(int? nature, string id)
    {
        if (nature is { } known && NatureFacts.IsValid(known)) return known;
        uint hash = 2166136261;
        foreach (var letter in id) hash = (hash ^ letter) * 16777619;
        return (int)(hash % NatureFacts.Count);
    }

    /// <summary>The tastes the player has found out so far (bit 1 the favorite, bit 2 the disliked);
    /// the rest show as ???.</summary>
    public static string Describe(int nature, int known)
    {
        if (NatureFacts.LikedFlavor(nature) is not { } liked || NatureFacts.DislikedFlavor(nature) is not { } disliked)
            return known != 0 ? "eats anything" : "loves ??? · dislikes ???";
        var love = (known & 1) != 0 ? NatureFacts.FlavorNames[liked] : "???";
        var hate = (known & 2) != 0 ? NatureFacts.FlavorNames[disliked] : "???";
        return $"loves {love} · dislikes {hate}";
    }

    /// <summary>"loves Sweet · dislikes Dry", or "eats anything" for a neutral nature.</summary>
    public static string Describe(int nature) =>
        NatureFacts.LikedFlavor(nature) is { } liked && NatureFacts.DislikedFlavor(nature) is { } disliked
            ? $"loves {NatureFacts.FlavorNames[liked]} · dislikes {NatureFacts.FlavorNames[disliked]}"
            : "eats anything";

    /// <summary>+1 a berry of that flavor is a favorite, −1 it is disliked, 0 otherwise.</summary>
    public static int Reaction(int nature, int flavor) =>
        NatureFacts.LikedFlavor(nature) == flavor ? 1 : NatureFacts.DislikedFlavor(nature) == flavor ? -1 : 0;
}
