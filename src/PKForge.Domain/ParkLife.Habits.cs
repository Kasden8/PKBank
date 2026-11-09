namespace PKForge.Domain;

/// <summary>
/// What makes each resident itself rather than one more walker: habits that come from what it is
/// (a Fire type basks on warm stones, a Grass type soaks up the sun, a Ground type digs, a Ghost
/// fades into the shadows, a Bug climbs into the bushes…), one of them its favorite, done far
/// more often than the others; a spot of its own it keeps going back to; a little gesture of its
/// own at the end of a walk; its own pace (the quick ones trot, the slow ones amble) and its own
/// range (homebodies keep close to home, roamers wander the whole park).
/// </summary>
public sealed partial class ParkLife
{
    private enum Habit
    {
        Sniff, Roll, Groom, Bask, Embers, Splash, Drink, FloatAbout, Sunbathe, Tend, ShakeLeaves, Static, Dash, Shade, FrostBreath,
        PunchTree, Meditate, Bubble, Dig, DustBath, Preen, Lookout, Levitate, Daydream, Bushes, Flowers, Still, Fade, Haunting,
        Survey, Roar, Lurk, Gleam, FlowerDance, Twirl
    }

    private static readonly Dictionary<int, Habit[]> HabitsByType = new()
    {
        [ParkType.Normal] = [Habit.Sniff, Habit.Roll, Habit.Groom],
        [ParkType.Fire] = [Habit.Bask, Habit.Embers],
        [ParkType.Water] = [Habit.Splash, Habit.Drink, Habit.FloatAbout],
        [ParkType.Grass] = [Habit.Sunbathe, Habit.Tend, Habit.ShakeLeaves],
        [ParkType.Electric] = [Habit.Static, Habit.Dash],
        [ParkType.Ice] = [Habit.Shade, Habit.FrostBreath],
        [ParkType.Fighting] = [Habit.PunchTree, Habit.Meditate],
        [ParkType.Poison] = [Habit.Bubble, Habit.Lurk],
        [ParkType.Ground] = [Habit.Dig, Habit.DustBath],
        [ParkType.Flying] = [Habit.Preen, Habit.Lookout],
        [ParkType.Psychic] = [Habit.Levitate, Habit.Daydream, Habit.Meditate],
        [ParkType.Bug] = [Habit.Bushes, Habit.Flowers],
        [ParkType.Rock] = [Habit.Still, Habit.DustBath],
        [ParkType.Ghost] = [Habit.Fade, Habit.Haunting],
        [ParkType.Dragon] = [Habit.Survey, Habit.Roar],
        [ParkType.Dark] = [Habit.Lurk, Habit.Sniff],
        [ParkType.Steel] = [Habit.Gleam, Habit.Still],
        [ParkType.Fairy] = [Habit.FlowerDance, Habit.Twirl],
    };

    private IReadOnlyList<Habit> HabitsOf(ParkLifeResident resident)
    {
        var habits = resident.Types.SelectMany(t => HabitsByType.GetValueOrDefault(t) ?? []).Distinct().ToList();
        if (resident.WaterBound) habits = [Habit.FloatAbout, Habit.Splash];
        return habits.Count > 0 ? habits : [Habit.Sniff];
    }

    /// <summary>The habit a resident is known for: one of its own, for good.</summary>
    private Habit FavoriteHabit(ParkLifeResident resident)
    {
        var habits = HabitsOf(resident);
        return habits[(int)((uint)Hash(resident.Id + "habit") % habits.Count)];
    }

    // Its little gesture: what it does when it stops at the end of a walk.
    private static readonly (ParkPose Pose, ParkEmote Emote, int Ms)[] Tics =
    [
        (ParkPose.DeepBreath, ParkEmote.None, 1600), (ParkPose.LookUp, ParkEmote.None, 1500), (ParkPose.Nod, ParkEmote.Note, 1400),
        (ParkPose.Hop, ParkEmote.None, 1000), (ParkPose.Spin, ParkEmote.None, 1200), (ParkPose.Sit, ParkEmote.None, 3000),
        (ParkPose.Idle, ParkEmote.Note, 1800), (ParkPose.Idle, ParkEmote.Question, 1600)
    ];

    private static (ParkPose Pose, ParkEmote Emote, int Ms) TicOf(ParkLifeResident resident) => Tics[(int)((uint)Hash(resident.Id + "tic") % Tics.Length)];

    /// <summary>How long it lingers between things: the calm and the lazy take their time, the
    /// energetic hardly stop.</summary>
    private static float Lingering(ParkLifeResident resident) => resident.Temperament == ParkTemperament.Lazy ? 1.8f
        : resident.Personality switch
        {
            ParkPersonality.Calm or ParkPersonality.Gentle => 1.4f,
            ParkPersonality.Energetic or ParkPersonality.Playful => .6f,
            _ => 1
        };

    /// <summary>Whether it keeps close to home (the shy, calm, gentle and lazy) or wanders the
    /// whole park (the curious and the energetic); the rest, a bit of both.</summary>
    private static int Range(ParkLifeResident resident) =>
        resident.Personality is ParkPersonality.Shy or ParkPersonality.Calm or ParkPersonality.Gentle || resident.Temperament == ParkTemperament.Lazy ? -1
        : resident.Personality is ParkPersonality.Curious or ParkPersonality.Energetic ? 1 : 0;

    /// <summary>A spot of its own in the place it calls home, the same one every day.</summary>
    private ParkSpot? Haunt(ParkActor actor)
    {
        var home = HomePlace(actor.Resident);
        var spots = _map.Spots.Where(s => s.Kind is ParkSpotKind.Nap or ParkSpotKind.Statue or ParkSpotKind.Shore or ParkSpotKind.Berries or ParkSpotKind.Flag
                && home.Contains(s.X, s.Y) && actor.Navigator.CanStand(s.X, s.Y))
            .OrderBy(s => s.X).ThenBy(s => s.Y).ToArray();
        return spots.Length == 0 ? null : spots[(int)((uint)Hash(actor.Resident.Id + "haunt") % spots.Length)];
    }

    private bool PlanHaunt(ParkActor actor)
    {
        if (Haunt(actor) is not { } spot || Crowded(actor, spot.X, spot.Y, PersonalSpace) ||
            !CanFly(actor.Resident) && !actor.Navigator.Connected(new(actor.X, actor.Y), new(spot.X, spot.Y))) return false;
        var random = actor.Random;
        var tic = TicOf(actor.Resident);
        var linger = Lingering(actor.Resident);
        Add(actor, new Label("Going to its favorite spot", "Content"), new GoTo(new(spot.X, spot.Y)),
            new Label("At its favorite spot", "Content"), new Act(tic.Pose, tic.Ms, spot.Facing, tic.Emote),
            new Act(random.Next(2) == 0 ? ParkPose.Sit : ParkPose.Idle, (int)(random.Next(6000, 12_000) * linger), spot.Facing));
        // Its favorite thing, done right there when it can be.
        if (random.Next(2) == 0) PlanHabit(actor, FavoriteHabit(actor.Resident), here: true);
        return true;
    }

    private bool PlanHabit(ParkActor actor)
    {
        // Its favorite most of the time; another of its own now and then.
        var habits = HabitsOf(actor.Resident);
        var habit = actor.Random.Next(5) < 3 ? FavoriteHabit(actor.Resident) : habits[actor.Random.Next(habits.Count)];
        return PlanHabit(actor, habit, here: false) || PlanHabit(actor, habits[actor.Random.Next(habits.Count)], here: false);
    }

    /// <param name="here">Where it stands, rather than walking somewhere that suits the habit.</param>
    private bool PlanHabit(ParkActor actor, Habit habit, bool here)
    {
        var random = actor.Random;
        var name = actor.Resident.Name;
        var linger = Lingering(actor.Resident);
        int Ms(int min, int max) => (int)(random.Next(min, max) * linger);
        var sunny = Phase is ParkDayPhase.Morning or ParkDayPhase.Day && !Wet;
        // Somewhere of these kinds that suits the habit, or simply where it is.
        ParkPoint? Somewhere(params ParkSpotKind[] kinds)
        {
            if (here) return new(actor.X, actor.Y);
            foreach (var kind in kinds.OrderBy(_ => random.Next()))
                if (Spot(actor, kind) is { } spot) return new(spot.X, spot.Y);
            return FreePoint(actor.Navigator, random, actor, land: true);
        }
        bool Go(string going, ParkPoint? to, params Step[] then)
        {
            if (to is not { } at) return false;
            if (!here) Add(actor, new Label(going, "Content"), new GoTo(at));
            Add(actor, then);
            return true;
        }
        switch (habit)
        {
            case Habit.Sniff:
                if (here || FreePoint(actor.Navigator, random, actor, land: true) is not { } a || FreePoint(actor.Navigator, random, actor, land: true) is not { } b) return false;
                Add(actor, new Label("Sniffing around", "Curious"), new GoTo(a, .7f), new Act(ParkPose.Idle, 900, Emote: ParkEmote.Question),
                    new GoTo(b, .7f), new Act(ParkPose.LookUp, 1200), new Act(ParkPose.Nod, 1000, Emote: random.Next(3) == 0 ? ParkEmote.Exclaim : ParkEmote.None));
                return true;
            case Habit.Roll:
                return Go("Looking for soft grass", Somewhere(ParkSpotKind.Nap),
                    new Label("Rolling in the grass", "Joyful"), new Act(ParkPose.Spin, 1400), new Spark(ParkEffectKind.Rustle), new Act(ParkPose.Spin, 1400),
                    new Act(ParkPose.Lie, Ms(2500, 4500), Emote: ParkEmote.Note), new Gain(ParkNeed.Fun, 8));
            case Habit.Groom:
                return Go("Finding a quiet patch", Somewhere(ParkSpotKind.Nap, ParkSpotKind.Shore),
                    new Label("Grooming itself", "Content"), new Act(ParkPose.Sit, Ms(3000, 5000)), new Act(ParkPose.DeepBreath, 1600),
                    new Act(ParkPose.Sit, Ms(2000, 4000), Emote: ParkEmote.Sparkle));
            case Habit.Bask when sunny:
                return Go("Looking for a warm stone", Somewhere(ParkSpotKind.Statue, ParkSpotKind.Nap),
                    new Label("Basking in the warmth", "Content"), new Act(ParkPose.Lie, Ms(6000, 11_000), Emote: ParkEmote.Sparkle),
                    new Spark(ParkEffectKind.Burn), new Act(ParkPose.DeepBreath, 1600), new Gain(ParkNeed.Energy, 8));
            case Habit.Embers when !Wet:
                return Go("Wandering off", Somewhere(ParkSpotKind.Gate, ParkSpotKind.Statue),
                    new Label("Puffing little embers", "Playful"), new Act(ParkPose.Charge, 1400), new Spark(ParkEffectKind.Burn),
                    new Act(ParkPose.Idle, 900, Emote: ParkEmote.Note), new Act(ParkPose.Charge, 1200), new Spark(ParkEffectKind.Burn));
            case Habit.Splash:
            {
                var shore = here ? null : Spot(actor, ParkSpotKind.Shore);
                if (!here && shore is null) return false;
                return Go("Off to the water", here ? new ParkPoint(actor.X, actor.Y) : new ParkPoint(shore!.X, shore.Y),
                    new Label("Splashing at the water's edge", "Joyful"), new Act(ParkPose.Hop, 900), new Spark(ParkEffectKind.Splash),
                    new Act(ParkPose.Hop, 900, Emote: ParkEmote.Note), new Spark(ParkEffectKind.Splash), new Gain(ParkNeed.Fun, 8));
            }
            case Habit.Drink:
                return !here && Spot(actor, ParkSpotKind.Shore) is { } edge && Go("Heading for a drink", new ParkPoint(edge.X, edge.Y),
                    new Label("Having a drink", "Content"), new Act(ParkPose.Eat, Ms(3000, 5000), edge.Facing), new Act(ParkPose.DeepBreath, 1400),
                    new Gain(ParkNeed.Hunger, 4));
            case Habit.FloatAbout:
                if (!actor.Resident.Swims || FreePoint(actor.Navigator, random, actor, water: true) is not { } pool) return false;
                Add(actor, new Label("Floating on its back", "Peaceful"), new GoTo(pool, .6f), new Act(ParkPose.Float, Ms(8000, 14_000), Emote: ParkEmote.Note));
                return true;
            case Habit.Sunbathe when sunny:
                return Go("Looking for sunshine", Somewhere(ParkSpotKind.Nap, ParkSpotKind.Berries),
                    new Label("Soaking up the sun", "Peaceful"), new Act(ParkPose.LookUp, 1800, 0), new Act(ParkPose.Sit, Ms(6000, 10_000), 0, ParkEmote.Sparkle),
                    new Spark(ParkEffectKind.Pollen), new Gain(ParkNeed.Energy, 10));
            case Habit.Tend:
                return Go("Off to the berry bushes", Somewhere(ParkSpotKind.Berries),
                    new Label("Tending the berry bushes", "Caring"), new Act(ParkPose.Pull, 1800), new Spark(ParkEffectKind.Rustle),
                    new Act(ParkPose.Pull, 1600), new Act(ParkPose.Nod, 1200, Emote: ParkEmote.Heart));
            case Habit.ShakeLeaves:
                return Go("Wandering off", Somewhere(ParkSpotKind.Nap),
                    new Label("Shaking out its leaves", "Playful"), new Act(ParkPose.Spin, 1200), new Spark(ParkEffectKind.Pollen), new Act(ParkPose.Hop, 900));
            case Habit.Static:
                return Go("Wandering off", Somewhere(ParkSpotKind.Flag, ParkSpotKind.Gate),
                    new Label("Building up static", "Determined"), new Act(ParkPose.Charge, Ms(2500, 4000)), new Spark(ParkEffectKind.Zap),
                    new Act(ParkPose.Hop, 900, Emote: ParkEmote.Exclaim), new Gain(ParkNeed.Fun, 6));
            case Habit.Dash:
            {
                if (here) return false;
                var points = Enumerable.Range(0, 3).Select(_ => FreePoint(actor.Navigator, random, actor, land: true)).OfType<ParkPoint>().ToArray();
                if (points.Length < 2) return false;
                Add(actor, new Label("Dashing about for the fun of it", "Joyful"));
                foreach (var p in points) Add(actor, new GoTo(p, 2f));
                Add(actor, new Spark(ParkEffectKind.Zap), new Act(ParkPose.Hop, 1000, Emote: ParkEmote.Note), new Gain(ParkNeed.Fun, 10), new Gain(ParkNeed.Energy, -4));
                return true;
            }
            case Habit.Shade:
                return Go("Looking for somewhere cool", Somewhere(ParkSpotKind.Hide, ParkSpotKind.Nap),
                    new Label(sunny ? "Keeping cool in the shade" : "Enjoying the cool air", "Content"), new Act(ParkPose.Sit, Ms(6000, 10_000)),
                    new Spark(ParkEffectKind.Frost));
            case Habit.FrostBreath:
                return Go("Wandering off", Somewhere(ParkSpotKind.Gate, ParkSpotKind.Shore),
                    new Label("Breathing frost patterns", "Playful"), new Act(ParkPose.Charge, 1500), new Spark(ParkEffectKind.Frost),
                    new Act(ParkPose.LookUp, 1400, Emote: ParkEmote.Sparkle));
            case Habit.PunchTree:
                return Go("Looking for a sturdy tree", Somewhere(ParkSpotKind.Nap, ParkSpotKind.Hide),
                    new Label("Punching a tree", "Determined"), new Act(ParkPose.Attack, 800), new Spark(ParkEffectKind.Rustle), new Act(ParkPose.Attack, 800),
                    new Act(ParkPose.Attack, 800, Emote: ParkEmote.Exclaim), new Spark(ParkEffectKind.Rustle), new Act(ParkPose.DeepBreath, 1600));
            case Habit.Meditate:
                return Go("Looking for a quiet place", Somewhere(ParkSpotKind.Statue, ParkSpotKind.Shore),
                    new Label("Meditating", "Peaceful"), new Act(ParkPose.Sit, Ms(8000, 14_000), 0, ParkEmote.Ellipsis),
                    new Act(ParkPose.DeepBreath, 1800), new Gain(ParkNeed.Energy, 6));
            case Habit.Bubble:
                return Go("Wandering off", Somewhere(ParkSpotKind.Shore, ParkSpotKind.Hide),
                    new Label("Bubbling quietly to itself", "Content"), new Act(ParkPose.Idle, Ms(3000, 5000)), new Spark(ParkEffectKind.Poison),
                    new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Note));
            case Habit.Dig:
                return Go("Looking for soft ground", Somewhere(ParkSpotKind.Nap, ParkSpotKind.Berries),
                    new Label("Digging a hole", "Determined"), new Act(ParkPose.Pull, 1600), new Spark(ParkEffectKind.Dirt),
                    new Act(ParkPose.Pull, 1600), new Spark(ParkEffectKind.Dirt), new Act(ParkPose.Hop, 900, Emote: random.Next(4) == 0 ? ParkEmote.Exclaim : ParkEmote.None),
                    new Gain(ParkNeed.Fun, 6));
            case Habit.DustBath:
                return Go("Looking for a dusty patch", Somewhere(ParkSpotKind.Gate, ParkSpotKind.Flag),
                    new Label("Rolling in the dust", "Joyful"), new Act(ParkPose.Spin, 1300), new Spark(ParkEffectKind.Dirt), new Act(ParkPose.Lie, Ms(2000, 3500)));
            case Habit.Preen:
                return Go("Finding a perch", Somewhere(ParkSpotKind.Statue, ParkSpotKind.Flag),
                    new Label("Preening its feathers", "Content"), new Act(ParkPose.Idle, Ms(3000, 5000)), new Spark(ParkEffectKind.Gust),
                    new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Sparkle));
            case Habit.Lookout:
            case Habit.Survey:
            {
                if (here || Lookout(actor) is not { } high) return false;
                Add(actor, new Label("Off to somewhere high", "Curious"), new GoTo(new(high.X, high.Y)),
                    new Label(habit == Habit.Survey ? "Surveying the land" : "Keeping watch from up high", "Proud"),
                    new Act(ParkPose.LookUp, 1800, high.Facing), new Act(ParkPose.Idle, Ms(6000, 10_000), high.Facing));
                return true;
            }
            case Habit.Levitate:
                return Go("Wandering off", Somewhere(ParkSpotKind.Statue, ParkSpotKind.Nap),
                    new Label("Levitating a pebble", "Inspired"), new Act(ParkPose.Charge, 2000), new Spark(ParkEffectKind.Psychic),
                    new Act(ParkPose.Idle, 1800, Emote: ParkEmote.Sparkle));
            case Habit.Daydream:
                return Go("Wandering off", Somewhere(ParkSpotKind.Shore, ParkSpotKind.Nap),
                    new Label("Staring into space", "Dreamy"), new Act(ParkPose.Idle, Ms(5000, 9000), Emote: ParkEmote.Ellipsis),
                    new Spark(ParkEffectKind.Psychic), new Act(ParkPose.Idle, 1200, Emote: ParkEmote.Question));
            case Habit.Bushes:
                return Go("Heading for the bushes", Somewhere(ParkSpotKind.Hide),
                    new Label("Climbing about in the bushes", "Curious"), new Spark(ParkEffectKind.Rustle), new Act(ParkPose.Idle, Ms(4000, 7000)),
                    new Spark(ParkEffectKind.Rustle), new Act(ParkPose.Hop, 900));
            case Habit.Flowers:
            {
                if (here) return false;
                var points = Enumerable.Range(0, 3).Select(_ => FreePoint(actor.Navigator, random, actor, land: true)).OfType<ParkPoint>().ToArray();
                if (points.Length < 2) return false;
                Add(actor, new Label("Buzzing from flower to flower", "Joyful"));
                foreach (var p in points) Add(actor, new GoTo(p, 1.3f), new Act(ParkPose.Eat, 1400), new Spark(ParkEffectKind.Pollen));
                Add(actor, new Gain(ParkNeed.Hunger, 6));
                return true;
            }
            case Habit.Still:
                return Go("Finding a spot", Somewhere(ParkSpotKind.Statue, ParkSpotKind.Gate),
                    new Label("Sitting perfectly still", "Peaceful"), new Act(ParkPose.Idle, Ms(12_000, 20_000), Emote: ParkEmote.Ellipsis));
            case Habit.Fade:
                return Go("Drifting toward the shadows", Somewhere(ParkSpotKind.Hide),
                    new Label("Fading into the shadows", "Mischievous"), new Spark(ParkEffectKind.Ghost), new Act(ParkPose.Idle, Ms(6000, 10_000)),
                    new Spark(ParkEffectKind.Ghost));
            case Habit.Haunting when Phase is ParkDayPhase.Evening or ParkDayPhase.Night:
            {
                if (here || FreePoint(actor.Navigator, random, actor, land: true) is not { } drift) return false;
                Add(actor, new Label("Drifting about in the dark", "Mischievous"), new GoTo(drift, .5f), new Spark(ParkEffectKind.Ghost),
                    new Act(ParkPose.Idle, 2000, Emote: ParkEmote.Note));
                return true;
            }
            case Habit.Roar:
                return Go("Wandering off", Somewhere(ParkSpotKind.Gate, ParkSpotKind.Statue),
                    new Label("Letting out a mighty roar", "Proud"), new Act(ParkPose.Charge, 1400), new Spark(ParkEffectKind.Gust),
                    new Act(ParkPose.Pose, 1600, Emote: ParkEmote.Exclaim), new Startle(40));
            case Habit.Lurk:
                return Go("Slinking off", Somewhere(ParkSpotKind.Hide),
                    new Label("Lurking in the shade", "Mischievous"), new Act(ParkPose.Idle, Ms(6000, 10_000), Emote: ParkEmote.Ellipsis));
            case Habit.Gleam when sunny:
                return Go("Finding the sunlight", Somewhere(ParkSpotKind.Statue, ParkSpotKind.Flag),
                    new Label("Gleaming in the sun", "Proud"), new Act(ParkPose.Pose, 1800, Emote: ParkEmote.Sparkle), new Spark(ParkEffectKind.Sparkle),
                    new Act(ParkPose.Idle, Ms(3000, 5000)));
            case Habit.FlowerDance:
                return Go("Off to the flowers", Somewhere(ParkSpotKind.Berries, ParkSpotKind.Nap),
                    new Label("Dancing among the flowers", "Joyful"), new Act(ParkPose.Dance, Ms(3000, 5000), Emote: ParkEmote.Heart),
                    new Spark(ParkEffectKind.Hearts), new Gain(ParkNeed.Fun, 8));
            case Habit.Twirl:
                return Go("Wandering off", Somewhere(ParkSpotKind.Nap, ParkSpotKind.Flag),
                    new Label("Twirling in a shower of sparkles", "Joyful"), new Act(ParkPose.Spin, 1400), new Spark(ParkEffectKind.Sparkle),
                    new Act(ParkPose.Hop, 900, Emote: ParkEmote.Sparkle));
        }
        return false;
    }
}
