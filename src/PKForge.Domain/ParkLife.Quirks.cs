namespace PKForge.Domain;

/// <summary>Species habits and species pairs (see <see cref="ParkLore"/>): lullabies that put the
/// park to sleep, Chansey checking on the sad, ghosts sneaking up on people, Meowth finding
/// coins, Ditto copying a friend, lovers spending time together, sworn rivals fighting, and the
/// hunted running for their lives.</summary>
public sealed partial class ParkLife
{
    internal sealed record Lull(float Radius) : Step;
    internal sealed record Serenade(float Radius) : Step;
    internal sealed record Startle(float Radius) : Step;
    internal sealed record Copy(string TargetId) : Step;
    internal sealed record Spark(ParkEffectKind Kind) : Step;
    internal sealed record Journal(string Text, string OtherId = "", ParkSocialEventKind Kind = ParkSocialEventKind.Rest) : Step;

    private bool RunQuirkStep(ParkActor actor, Step step, long now)
    {
        switch (step)
        {
            case Lull lull:
                // Everyone awake within earshot drops off, one after another.
                foreach (var other in Near(actor, lull.Radius).Where(o => !o.IsAsleep && !o.Busy))
                {
                    Interrupt(other);
                    Add(other, new Label($"Lulled to sleep by {actor.Resident.Name}", "Sleepy"), new Act(ParkPose.Lie, 800 + other.Random.Next(1200)),
                        new Act(ParkPose.Sleep, other.Random.Next(12_000, 20_000), Emote: ParkEmote.Sleep), new Act(ParkPose.Wake, 1200));
                }
                return true;
            case Serenade serenade:
                foreach (var other in Near(actor, serenade.Radius).Where(o => !o.IsAsleep))
                {
                    other.Care = other.Care.Add(ParkNeed.Fun, 6).Add(ParkNeed.Social, 4);
                    other.Say(ParkEmote.Note, now);
                }
                return true;
            case Startle startle:
                foreach (var other in Near(actor, startle.Radius).Where(o => !o.IsAsleep)) other.Say(ParkEmote.Exclaim, now);
                return true;
            case Copy copy:
                if (_actors.TryGetValue(copy.TargetId, out var model))
                {
                    actor.Disguise = model.Resident.Species;
                    actor.DisguiseTinted = actor.Resident.Has(ParkQuirk.Copycat);
                    actor.DisguiseUntil = now + 40_000;
                }
                return true;
            case Spark spark:
                Burst(spark.Kind, actor);
                return true;
            case Journal journal:
                _moments.Add(new ParkMoment(journal.Kind, actor.Resident.Id, journal.OtherId, journal.Text));
                return true;
        }
        if (RunHighlightStep(actor, step) || RunPowerStep(actor, step, now) || RunChronicleStep(actor, step) || RunSceneStep(actor, step, now) || RunSnorlaxStep(actor, step) || RunIconStep(actor, step, now) || RunTrollStep(actor, step) || RunEveningStep(actor, step, now) || RunDaycareStep(actor, step)) return true;
        return step is Leave ? RunVisitorStep(actor, step) : RunGameStep(actor, step, now);
    }

    private IEnumerable<ParkActor> Near(ParkActor actor, float radius) => _actors.Values.Where(other =>
        other != actor && !other.Indoors && Distance(actor.X, actor.Y, other.X, other.Y) <= radius);

    private readonly Dictionary<(string, string), ParkPairKind> _feelings = new();

    /// <summary>How one resident feels about another by species (love, family, friends, rivals, fear…).</summary>
    public ParkPairKind FeelingOf(string a, string b) { lock (_gate) return FeelingOfCore(a, b); }

    private ParkPairKind FeelingOfCore(string a, string b) =>
        _actors.TryGetValue(a, out var first) && _actors.TryGetValue(b, out var second) ? Feeling(first, second) : ParkPairKind.None;

    /// <summary>How <paramref name="a"/> feels about <paramref name="b"/> by species (worked out once per pair).</summary>
    private ParkPairKind Feeling(ParkActor a, ParkActor b)
    {
        var key = (a.Resident.Id, b.Resident.Id);
        if (!_feelings.TryGetValue(key, out var feeling))
            _feelings[key] = feeling = ParkLore.Feeling(a.Resident.Species, b.Resident.Species, Math.Abs(Hash(a.Resident.Id + b.Resident.Id)));
        return feeling;
    }

    // The hunted and the scared run when what they fear comes close.
    private void FleeIfScared(ParkActor actor, long now)
    {
        // A look around four times a second is plenty.
        if (now % 250 >= TickMilliseconds || actor.Busy || actor.IsAsleep || actor.Indoors || now < actor.CalmUntil) return;
        var threat = _actors.Values.FirstOrDefault(other => other != actor && !other.Indoors && !other.IsAsleep &&
            Feeling(actor, other) == ParkPairKind.Fears && Distance(actor.X, actor.Y, other.X, other.Y) < 30);
        if (threat is null) return;
        actor.CalmUntil = now + 10_000;
        Interrupt(actor);
        // Away from the threat: the farthest of a few free points.
        var away = Enumerable.Range(0, 6).Select(_ => FreePoint(actor.Navigator, actor.Random, actor)).OfType<ParkPoint>()
            .OrderByDescending(p => Distance(p.X, p.Y, threat.X, threat.Y)).FirstOrDefault();
        Add(actor, new Label($"Running away from {threat.Resident.Name}", "Scared"),
            new Act(ParkPose.Cringe, 500, ParkMotion.Facing(threat.X - actor.X, threat.Y - actor.Y), ParkEmote.Exclaim));
        if (away != default) Add(actor, new GoTo(away, 2.1f));
        RaiseBarrier(actor, threat);
        Add(actor, new Act(ParkPose.Idle, 2000, Emote: ParkEmote.Sweat));
        if (threat.Resident.Species != actor.Resident.Species && Feeling(threat, actor) == ParkPairKind.Hunts)
            _moments.Add(new ParkMoment(ParkSocialEventKind.Conflict, threat.Resident.Id, actor.Resident.Id,
                $"{threat.Resident.Name} stalked {actor.Resident.Name} across the park. {actor.Resident.Name} got away, just."));
    }

    // How much more (or less) the species' habits make each activity appeal.
    private void QuirkWeights(ParkActor actor, Dictionary<Activity, float> weights)
    {
        var resident = actor.Resident;
        void Scale(Activity activity, float factor) => weights[activity] *= factor;
        var night = Phase == ParkDayPhase.Night;
        if (resident.Has(ParkQuirk.Nocturnal))
        {
            // Up all night, napping by day.
            if (night) { weights[Activity.Bedtime] = 0; foreach (var key in weights.Keys.ToArray()) weights[key] *= 10; }
            else Scale(Activity.Nap, 2.5f);
        }
        if (resident.Has(ParkQuirk.Sleepyhead)) Scale(Activity.Nap, 3);
        if (resident.Has(ParkQuirk.Glutton)) Scale(Activity.Snack, 2.2f);
        if (resident.Has(ParkQuirk.Loner)) { Scale(Activity.Greet, .2f); Scale(Activity.PlayFight, .2f); Scale(Activity.Sit, 2); }
        switch (resident.Temperament)
        {
            case ParkTemperament.Fierce: Scale(Activity.PlayFight, 1.8f); Scale(Activity.Train, 1.8f); Scale(Activity.Greet, .5f); break;
            case ParkTemperament.Lazy: Scale(Activity.Nap, 2); Scale(Activity.Stroll, .5f); Scale(Activity.Train, .3f); break;
            case ParkTemperament.Aloof: Scale(Activity.Greet, .4f); Scale(Activity.Gate, 2); break;
            case ParkTemperament.Timid: Scale(Activity.PlayFight, .3f); break;
        }
        // Visitors do not sleep over or knock on the cabin.
        if (IsVisitor(resident.Id)) { weights[Activity.Bedtime] = 0; weights[Activity.Door] = 0; }
        weights[Activity.Quirk] = HasQuirkActivity(resident) || IsTroll(resident) ? (night && resident.Has(ParkQuirk.MoonDancer) ? 400 : IsTroll(resident) ? 22 : 14) : 0;
        if (actor.LastActivity == Activity.Quirk) weights[Activity.Quirk] *= .2f;
    }

    private static bool HasQuirkActivity(ParkLifeResident resident) => resident.Has(ParkQuirk.Lullaby) || resident.Has(ParkQuirk.Singer) ||
        resident.Has(ParkQuirk.MoonDancer) || resident.Has(ParkQuirk.Healer) || resident.Has(ParkQuirk.Prankster) ||
        resident.Has(ParkQuirk.Splasher) || resident.Has(ParkQuirk.Headache) || resident.Has(ParkQuirk.CoinFinder) ||
        resident.Has(ParkQuirk.PretendTree) || resident.Has(ParkQuirk.Loner) || resident.Species == Snorlax || HasIcon(resident) || HasPower(resident);

    /// <returns>False when nothing of its habits fits right now.</returns>
    private bool PlanQuirk(ParkActor actor)
    {
        var resident = actor.Resident;
        var name = resident.Name;
        var random = actor.Random;
        // Trolls go looking for trouble half the time.
        // A troublemaker is always up to something, with a short breather between tricks.
        if (IsTroll(resident) && Now >= actor.NextMischief && random.Next(2) == 0 && PlanMischief(actor))
        {
            actor.NextMischief = Now + random.Next(180, 480) * 1000L;
            return true;
        }
        if (resident.Species == Snorlax && PlanRoadblock(actor)) return true;
        if (HasIcon(resident) && random.Next(3) > 0 && PlanIcon(actor)) return true;
        if (HasPower(resident) && random.Next(2) == 0 && PlanPower(actor)) return true;
        var awake = _actors.Values.Where(o => o != actor && !o.IsAsleep && !o.Busy && !o.Indoors &&
            actor.Navigator.Connected(new(actor.X, actor.Y), new(o.X, o.Y))).ToArray();

        if (resident.Has(ParkQuirk.MoonDancer) && Phase is ParkDayPhase.Night or ParkDayPhase.Evening &&
            Spot(actor, ParkSpotKind.Statue) is { } stage)
        {
            Add(actor, new Label("Dancing under the moon", "Joyful"), new GoTo(new(stage.X, stage.Y)),
                new Act(ParkPose.LookUp, 1500, 4), new Act(ParkPose.Dance, random.Next(6000, 10_000), Emote: ParkEmote.Sparkle),
                new Spark(ParkEffectKind.Hearts), new Gain(ParkNeed.Fun, 15),
                new Journal($"{name} danced by the statue under the moonlight, all by itself."));
            return true;
        }
        if (resident.Has(ParkQuirk.Lullaby) && awake.Any(o => Distance(o.X, o.Y, actor.X, actor.Y) < 70))
        {
            var audience = awake.OrderBy(o => Distance(o.X, o.Y, actor.X, actor.Y)).First();
            Add(actor, new Label("Singing a lullaby", "Joyful"), new GoTo(new(audience.X + 20, audience.Y)), new Spotlight($"{name} is singing its lullaby…", 2, 15_000),
                new Act(ParkPose.Sing, 4500, 0, ParkEmote.Note, ParkProp.Notes), new Lull(48), new Act(ParkPose.Idle, 1200, 0, ParkEmote.Ellipsis),
                new Label("Furious that everyone fell asleep", "Grumpy"), new Act(ParkPose.Attack, 900, Emote: ParkEmote.Anger),
                // Jigglypuff's revenge: a marker on every sleeping face.
                new Act(ParkPose.Pull, 1600, Emote: ParkEmote.Anger), new Doodle(52),
                new Act(ParkPose.Hop, 1400, Emote: ParkEmote.Anger),
                new Journal($"{name} sang its lullaby. Everyone nearby fell asleep before the end, and {name} was furious about it.", audience.Resident.Id));
            return true;
        }
        if (resident.Has(ParkQuirk.Singer))
        {
            var stage2 = resident.Swims && Spot(actor, ParkSpotKind.Shore) is { } shore ? new ParkPoint(shore.X, shore.Y) : new ParkPoint(actor.X, actor.Y);
            Add(actor, new Label("Singing for the park", "Joyful"), new GoTo(stage2),
                new Act(ParkPose.Sing, random.Next(5000, 8000), Emote: ParkEmote.Note, Prop: ParkProp.Notes), new Serenade(70),
                new Journal($"{name} sang, and the whole park stopped to listen."));
            return true;
        }
        if (resident.Has(ParkQuirk.Healer))
        {
            // The resident who needs it most: a need running low, or simply a bad mood.
            var patient = awake.Where(o => Math.Min(Math.Min(o.Care.Hunger, o.Care.Energy), Math.Min(o.Care.Fun, o.Care.Social)) < 45)
                .OrderByDescending(o => IsUnwell(o.Care)).ThenBy(o => o.Care.Fun + o.Care.Social).FirstOrDefault();
            if (patient is not null && new ParkPoint(patient.X + 18, patient.Y) is var bedside && actor.Navigator.CanStand(bedside.X, bedside.Y))
            {
                Interrupt(patient);
                actor.PartnerId = patient.Resident.Id; patient.PartnerId = resident.Id;
                Add(patient, new Face(resident.Id), new Act(ParkPose.Idle, 20_000), new Release());
                Add(actor, new Label($"Checking on {patient.Resident.Name}", "Caring"), new GoTo(bedside, 1.2f), new Face(patient.Resident.Id),
                    new Engage(patient.Resident.Id, [new Face(resident.Id), new Act(ParkPose.Idle, 500), new Act(ParkPose.Nod, 1400, Emote: ParkEmote.Heart),
                        new Gain(ParkNeed.Social, 14), new Gain(ParkNeed.Fun, 10), new Act(ParkPose.Hop, 1200)]),
                    new Act(ParkPose.Nod, 1400, Emote: ParkEmote.Heart), new Spark(ParkEffectKind.Hearts),
                    new Journal($"{name} checked on {patient.Resident.Name} and cheered it up.", patient.Resident.Id, ParkSocialEventKind.Help),
                    new Release());
                return true;
            }
        }
        if (resident.Has(ParkQuirk.Prankster) && awake.Where(o => !o.Resident.Types.Contains(ParkType.Ghost)).OrderBy(_ => random.Next()).FirstOrDefault() is { } victim)
        {
            // Sneak up from behind (the north side of the victim), then boo.
            var behind = new ParkPoint(victim.X, victim.Y - 12);
            if (!actor.Navigator.CanStand(behind.X, behind.Y)) behind = new ParkPoint(victim.X - 16, victim.Y);
            if (actor.Navigator.CanStand(behind.X, behind.Y))
            {
                actor.PartnerId = victim.Resident.Id;
                Add(actor, new Label($"Sneaking up on {victim.Resident.Name}", "Mischievous"), new GoTo(behind, .7f),
                    new Face(victim.Resident.Id), new Spark(ParkEffectKind.Scare),
                    new Engage(victim.Resident.Id, [new Act(ParkPose.Hurt, 600, Emote: ParkEmote.Exclaim), new Act(ParkPose.Cringe, 900, Emote: ParkEmote.Sweat),
                        new Face(resident.Id), new Label($"Pranked by {name}", "Grumpy"), new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Anger)]),
                    new Act(ParkPose.Hop, 1800, Emote: ParkEmote.Note), new Gain(ParkNeed.Fun, 12),
                    new Journal($"{name} crept up behind {victim.Resident.Name} and scared it half to death. {name} laughed for ages.", victim.Resident.Id, ParkSocialEventKind.Play),
                    new Release());
                victim.PartnerId = resident.Id;
                return true;
            }
        }
        if (resident.Has(ParkQuirk.Splasher) && Phase != ParkDayPhase.Night)
        {
            Add(actor, new Label("Using Splash", "Determined"), new Act(ParkPose.Hop, 2500, Emote: ParkEmote.Exclaim), new Spark(ParkEffectKind.Splash),
                new Act(ParkPose.Hop, 2000), new Spark(ParkEffectKind.Splash), new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Ellipsis));
            return true;
        }
        if (resident.Has(ParkQuirk.Headache))
        {
            Add(actor, new Label("Holding its aching head", "Confused"), new Act(ParkPose.Pain, 3000, 0, ParkEmote.Question));
            if (random.Next(3) == 0)
                Add(actor, new Spark(ParkEffectKind.Psychic), new Startle(60), new Act(ParkPose.Idle, 1500, 0, ParkEmote.Exclaim),
                    new Journal($"{name}'s headache got so bad that a psychic wave rippled across the park. Nobody knows how it did that."));
            return true;
        }
        if (resident.Has(ParkQuirk.CoinFinder) && FreePoint(actor.Navigator, random, actor) is { } dig)
        {
            Add(actor, new Label("Looking for something shiny", "Curious"), new GoTo(dig), new Act(ParkPose.Pull, 1800),
                new Spark(ParkEffectKind.Coin), new Act(ParkPose.Hop, 1200, Emote: ParkEmote.Sparkle), new Gain(ParkNeed.Fun, 10));
            if (random.Next(4) == 0) Add(actor, new Journal($"{name} found a shiny coin in the grass and will not stop admiring it."));
            return true;
        }
        if (resident.Has(ParkQuirk.PretendTree) && Spot(actor, ParkSpotKind.Nap) is { } grove)
        {
            Add(actor, new Label("Pretending to be a tree", "Proud"), new GoTo(new(grove.X, grove.Y)),
                new Act(ParkPose.Idle, random.Next(15_000, 30_000), 0, ParkEmote.Ellipsis));
            return true;
        }
        return resident.Has(ParkQuirk.Loner) && PlanSolitude(actor);
    }

    private bool Wet => Weather is ParkWeather.Rain or ParkWeather.Storm;

    // Fire types hate the rain; so do the timid when there is thunder.
    private bool ShelterFromRain(ParkLifeResident resident) =>
        resident.Types.Contains(ParkType.Fire) && !resident.Types.Contains(ParkType.Water) ||
        Weather == ParkWeather.Storm && resident.Temperament == ParkTemperament.Timid;

    // The sky changes the day: swimmers love the rain, Fire types run for cover, Electric types
    // drink in the storm, everyone lazes on a sunny afternoon.
    private void WeatherWeights(ParkActor actor, Dictionary<Activity, float> weights)
    {
        var resident = actor.Resident;
        void Scale(Activity activity, float factor) => weights[activity] *= factor;
        switch (Weather)
        {
            case ParkWeather.Sunny when Phase == ParkDayPhase.Day:
                Scale(Activity.Nap, 1.3f); Scale(Activity.Sit, 1.3f);
                break;
            case ParkWeather.Rain or ParkWeather.Storm:
                Scale(Activity.Stroll, .6f); Scale(Activity.Statue, .4f); Scale(Activity.Flag, .4f); Scale(Activity.Gate, .4f);
                if (resident.Swims) { Scale(Activity.Swim, 2); Scale(Activity.Dance, 2); }
                if (resident.Types.Contains(ParkType.Grass)) Scale(Activity.Sit, 1.8f);
                if (Weather == ParkWeather.Storm && resident.Types.Contains(ParkType.Electric)) Scale(Activity.Train, 4);
                if (ShelterFromRain(resident) && Phase != ParkDayPhase.Night) weights[Activity.Bedtime] = Math.Max(weights[Activity.Bedtime], 300);
                break;
        }
    }

    /// <summary>Lovers spend time side by side; sworn rivals fight for real.</summary>
    private void PlanPair(ParkActor actor, ParkActor partner, ParkPairKind kind, ParkPoint meet)
    {
        string them = partner.Resident.Name, me = actor.Resident.Name, myId = actor.Resident.Id, theirId = partner.Resident.Id;
        if (kind == ParkPairKind.Love)
        {
            Add(actor, new Label($"Going to see {them}", "In love"), new GoTo(meet, 1.3f), new Face(theirId),
                new Label($"Spending time with {them}", "In love"),
                new Engage(theirId, [new Face(myId), new Label($"Spending time with {me}", "In love"), new Act(ParkPose.Nod, 1200, Emote: ParkEmote.Heart),
                    new Act(ParkPose.Sit, 7000, Emote: ParkEmote.Heart), new Gain(ParkNeed.Social, 20)]),
                new Act(ParkPose.Nod, 1200, Emote: ParkEmote.Heart), new Spark(ParkEffectKind.Hearts), new Act(ParkPose.Sit, 7000),
                new Gain(ParkNeed.Social, 20),
                new Journal($"{me} and {them} sat side by side for a long while. Neither wanted to be the first to leave.", theirId, ParkSocialEventKind.Bond), new Release());
            CroagunkWatches(actor, partner);
            return;
        }
        // Sworn rivals: a real fight, with blows exchanged and a winner who gloats.
        var iWin = actor.Random.Next(2) == 0;
        Add(actor, new Label($"Squaring up to {them}", "Furious"), new GoTo(meet, 1.4f), new Face(theirId),
            new Label($"Fighting {them}", "Furious"),
            new Engage(theirId, [new Face(myId), new Label($"Fighting {me}", "Furious"), new Act(ParkPose.Idle, 700, Emote: ParkEmote.Anger),
                new Act(ParkPose.Hurt, 800), new Act(ParkPose.Attack, 900), new Act(ParkPose.Idle, 400), new Act(ParkPose.Hurt, 700),
                iWin ? new Act(ParkPose.Tumble, 1400, Emote: ParkEmote.Sweat) : new Act(ParkPose.Pose, 1600, Emote: ParkEmote.Sparkle),
                new Gain(ParkNeed.Social, -6), new Act(ParkPose.Idle, 2000, Emote: ParkEmote.Anger),
                .. iWin ? new Step[] { new Defend(theirId, myId) } : []]),
            new Act(ParkPose.Idle, 700, Emote: ParkEmote.Anger), new Act(ParkPose.Attack, 900), new Spark(ParkEffectKind.Bite),
            new Act(ParkPose.Hurt, 900), new Act(ParkPose.Attack, 900),
            iWin ? new Act(ParkPose.Pose, 1600, Emote: ParkEmote.Sparkle) : new Act(ParkPose.Tumble, 1400, Emote: ParkEmote.Sweat),
            new Gain(ParkNeed.Social, -6),
            new Journal(iWin ? $"{me} and {them} fought again, as they always do. {me} won this round and will not let anyone forget it."
                : $"{me} picked a fight with {them} and lost. {me} is pretending it never happened.", theirId, ParkSocialEventKind.Conflict),
            new Act(ParkPose.Idle, 2000, Emote: ParkEmote.Anger), new Release());
        // The loser's own may come to take its side.
        if (!iWin) Add(actor, new Defend(myId, theirId));
    }
}
