namespace PKForge.Domain;

/// <summary>What the player does with a resident, and how it takes it: it may go along, refuse
/// in its own way, or set off an accident it cannot help (a zap, a singe, a lick…).</summary>
public sealed partial class ParkLife
{
    private readonly List<ParkEffect> _effects = new();

    /// <summary>Bursts drawn over the park for a few seconds (sparks, flames, hearts…).</summary>
    /// <summary>The effects showing now (a copy, safe to draw from another thread).</summary>
    public IReadOnlyList<ParkEffect> Effects { get { lock (_gate) return _effects.ToArray(); } }

    private void Burst(ParkEffectKind kind, ParkActor actor) => _effects.Add(new(kind, actor.X, actor.Y - 10, Now));

    private int Roll(ParkActor actor) => actor.Random.Next(1000);

    // Whether it goes along this time: its docility (temperament, bond, mood, nature) plus how
    // appealing the offer is.
    private bool Willing(ParkActor actor, float appeal)
    {
        var docility = ParkLore.Docility(actor.Resident.Temperament, actor.Care.Bond, actor.Mood, actor.Resident.Nature);
        // Still sore about something you did.
        if (Now < actor.GrudgeUntil) docility -= .3f;
        return actor.Random.NextDouble() < docility + appeal;
    }

    private void Add(ParkActor actor, params Step[] steps) { foreach (var step in steps) actor.Plan.Enqueue(step); }

    // Woken up: out of the cabin if it slept there, grumpy or drowsy about it.
    private void Wake(ParkActor actor)
    {
        var grumpy = actor.Resident.Temperament is ParkTemperament.Fierce or ParkTemperament.Feisty or ParkTemperament.Aloof ||
                     actor.Care.Energy < ParkCare.Low;
        if (actor.Indoors && _map.Spots.FirstOrDefault(spot => spot.Kind == ParkSpotKind.Door) is { } door)
        {
            actor.X = door.X; actor.Y = door.Y + 6;
            Add(actor, new Exit());
        }
        if (grumpy) actor.GrudgeUntil = Now + 10 * 60_000;
        Add(actor, new Label("Woken up", grumpy ? "Grumpy" : "Sleepy"),
            new Act(ParkPose.Wake, 1200, 0, grumpy ? ParkEmote.Anger : ParkEmote.Ellipsis), new Act(ParkPose.Idle, 600, 0));
    }

    // The way it says no, by temperament.
    // game: the game it was asked to play, when it was not the ball.
    private ParkReply Refuse(ParkActor actor, ParkCommand command, ParkGameKind? game = null)
    {
        var resident = actor.Resident;
        var line = ParkLore.RefusalLine(resident.Temperament, resident.Name, command, Roll(actor), game);
        WillOWisp(actor);
        switch (resident.Temperament)
        {
            case ParkTemperament.Fierce or ParkTemperament.Feisty:
                Add(actor, new Label("In no mood for you", "Grumpy"), new Act(ParkPose.Idle, 800, 0, ParkEmote.Anger),
                    new Act(ParkPose.Idle, 2500, 4));
                return new(false, line, "Angry");
            case ParkTemperament.Timid:
                if (FreePoint(actor.Navigator, actor.Random, actor) is { } hide)
                    Add(actor, new Label("Keeping its distance", "Nervous"), new Act(ParkPose.Idle, 400, 0, ParkEmote.Sweat), new GoTo(hide, 1.6f),
                        new Act(ParkPose.Idle, 2000, 0, ParkEmote.Ellipsis));
                return new(false, line, "Worried");
            case ParkTemperament.Mischievous:
                Add(actor, new Label("Teasing you", "Playful"), new Act(ParkPose.Hop, 1200, 0, ParkEmote.Note), new Act(ParkPose.Spin, 900));
                return new(false, line, "Happy");
            default:
                Add(actor, new Label("Ignoring you", "Aloof"), new Act(ParkPose.Idle, 2800, 4, ParkEmote.Ellipsis));
                return new(false, line, "Normal");
        }
    }

    /// <summary>The player spends a moment with a resident: it drops what it was doing, if it agrees to.</summary>
    public ParkReply Command(string id, ParkCommand command) { lock (_gate) return CommandCore(id, command); }

    private ParkReply CommandCore(string id, ParkCommand command)
    {
        if (!_actors.TryGetValue(id, out var actor)) return ParkReply.None;
        var resident = actor.Resident;
        // The player steps into the park's moments: the Poké Flute for a Snorlax across the way,
        // a hand for a Psyduck in trouble, a berry for one who found the bush bare.
        if (actor.Blocking) return PokeFlute(actor);
        if (actor.RescueShore is { } shore && actor.Activity == "Forgot it cannot swim") return PullOut(actor, shore);
        var asleep = actor.IsAsleep;
        // A lazy sleeper does not even open its eyes.
        if (asleep && resident.Temperament == ParkTemperament.Lazy && command != ParkCommand.Snack && actor.Random.Next(3) > 0)
            return new(false, $"{resident.Name} does not even open its eyes.", "Sigh");
        Interrupt(actor);
        if (asleep) Wake(actor);
        var appeal = command switch { ParkCommand.Snack => .45f, ParkCommand.Talk => .15f, ParkCommand.Relax => .1f, _ => 0 };
        if (!Willing(actor, appeal)) return Refuse(actor, command);
        switch (command)
        {
            case ParkCommand.Snack when Now - actor.FoundBareAt < 120_000:
                actor.FoundBareAt = long.MinValue / 2;
                Add(actor, new Label("Saved by your berry", "Delighted"), new Act(ParkPose.Eat, 6000, 0, Prop: ParkProp.Berry), new Gain(ParkNeed.Hunger, 25, 8),
                    new Act(ParkPose.Hop, 1400, Emote: ParkEmote.Heart), new Act(ParkPose.Dance, 1800, Emote: ParkEmote.Note),
                    new Journal($"{resident.Name} found the berry bush stripped bare, and then you came along with a berry. It has not forgotten."));
                return new(true, $"{resident.Name} found the bush bare, and here you are with a berry. It eats it in two bites and looks at you like you saved its day.", "Joyous");
            case ParkCommand.Snack:
                Add(actor, new Label("Enjoying a berry from you", "Delighted"),
                    new Act(ParkPose.Eat, 6000, 0, Prop: ParkProp.Berry), new Gain(ParkNeed.Hunger, 10, 4),
                    new Act(ParkPose.Hop, 1400, Emote: ParkEmote.Heart));
                return new(true, null, "Happy");
            case ParkCommand.Play:
                Add(actor, new Label("Bouncing along with the ball", "Playful"),
                    new Act(ParkPose.Hop, 4500, Emote: ParkEmote.Note, Prop: ParkProp.Ball), new Gain(ParkNeed.Fun, 15, 4),
                    new Act(ParkPose.Spin, 1500, Emote: ParkEmote.Sparkle));
                return new(true, null, "Joyous");
            case ParkCommand.Relax:
                Add(actor, new Label("Listening to the meadow with you", "Peaceful"),
                    new Act(ParkPose.Sit, 8000, Prop: ParkProp.Notes), new Gain(ParkNeed.Energy, 8, 2));
                return new(true, null, "Normal");
        }
        Add(actor, new Label("Happy to see you!", "Delighted"),
            new Act(ParkPose.Nod, 1600, 0, ParkEmote.Heart), new Gain(ParkNeed.Social, 12, 3), new Act(ParkPose.Idle, 3000, 0));
        // Chansey and Blissey bring a gift once a day to someone they love.
        if (resident.Has(ParkQuirk.EggGiver) && actor.Care.Bond >= 60 && actor.Care.GiftDay != Today)
        {
            actor.Care = actor.Care with { GiftDay = Today };
            Burst(ParkEffectKind.Egg, actor);
            return new(true, $"{resident.Name} hands you an egg, holding it out with both hands. It looks very proud of itself.", "Joyous");
        }
        // A line it has not said lately, from what it is, the time, the weather and how it feels.
        var turn = actor.LineTurn++;
        var line = ParkTalk.Pick(ParkTalk.For(resident, actor.Care, Phase, Weather, turn), actor.RecentLines, turn);
        actor.RecentLines.Enqueue(line.Id);
        while (actor.RecentLines.Count > 12) actor.RecentLines.Dequeue();
        return new(true, line.Text, "Happy");
    }

    // The Poké Flute: Snorlax wakes, yawns and rolls out of the way; those stuck behind it cheer.
    private ParkReply PokeFlute(ParkActor snorlax)
    {
        var name = snorlax.Resident.Name;
        var stuck = _actors.Values.Where(a => a.WaitingSince > 0 && Distance(a.X, a.Y, snorlax.X, snorlax.Y) < StuckDistance + 20).ToArray();
        Interrupt(snorlax);
        Burst(ParkEffectKind.Sparkle, snorlax);
        Add(snorlax, new Label("Woken by the Poké Flute", "Surprised"), new Act(ParkPose.Wake, 1400, Emote: ParkEmote.Exclaim),
            new Act(ParkPose.DeepBreath, 1800), new Gain(ParkNeed.Energy, 20, 3), new Label("Ambling off, still yawning", "Sleepy"));
        if (FreePoint(snorlax.Navigator, snorlax.Random, snorlax, land: true) is { } away) Add(snorlax, new GoTo(away, .7f));
        foreach (var fan in stuck) fan.Say(ParkEmote.Note, Now);
        _moments.Add(new ParkMoment(ParkSocialEventKind.Help, snorlax.Resident.Id, "",
            $"You played the Poké Flute and {name} finally woke up and moved.{(stuck.Length > 0 ? $" {Names(stuck)} cheered." : "")}"));
        return new(true, $"You play the Poké Flute. ♪ {name} wakes up with a start, yawns enormously, and rolls out of the way.", "Joyous");
    }

    // A hand for Psyduck: back to the shore, embarrassed but safe.
    private ParkReply PullOut(ParkActor psyduck, ParkPoint shore)
    {
        var name = psyduck.Resident.Name;
        Interrupt(psyduck);
        psyduck.RescueShore = null;
        Add(psyduck, new Label("Pulled out of the water by you", "Embarrassed"), new GoTo(shore, .9f), new Act(ParkPose.Pain, 1800, Emote: ParkEmote.Sweat),
            new Act(ParkPose.Nod, 1400, Emote: ParkEmote.Heart), new Gain(ParkNeed.Social, 10, 6),
            new Journal($"{name} forgot it cannot swim, again. You pulled it back to the shore before anyone else could."));
        return new(true, $"You reach out and pull {name} back to the shore. It holds its head, very embarrassed.", "Happy");
    }

    /// <summary>A head pat from the player (a long press on the Pokémon). Some cannot help zapping,
    /// singeing or licking you; wary ones step out of reach.</summary>
    public ParkReply Pet(string id) { lock (_gate) return PetCore(id); }

    private ParkReply PetCore(string id)
    {
        if (!_actors.TryGetValue(id, out var actor)) return ParkReply.None;
        var resident = actor.Resident;
        if (actor.IsAsleep)
        {
            // A gentle pat does not wake it: it smiles in its sleep.
            actor.Say(ParkEmote.Heart, Now);
            actor.Care = actor.Care.AddBond(1, Today);
            return new(true, null, "Happy");
        }
        // Mimikyu will not have its disguise lifted, not even by you.
        if (resident.Species == 778 && actor.Random.Next(3) == 0)
        {
            Interrupt(actor);
            Burst(ParkEffectKind.Ghost, actor);
            Add(actor, new Label("Guarding its disguise", "Grumpy"), new Act(ParkPose.Cringe, 600, 0, ParkEmote.Exclaim), new Act(ParkPose.Idle, 1800, 0, ParkEmote.Anger));
            return new(false, $"Your hand brushes the corner of {resident.Name}'s disguise. It whips round, furious. Nobody looks under there.", "Angry");
        }
        var (accident, chance) = ParkLore.AccidentOf(resident.Species, resident.Types, resident.Temperament, actor.Care.Bond, actor.Mood);
        if (actor.Random.NextDouble() < chance)
        {
            Interrupt(actor);
            Burst(accident switch
            {
                ParkAccident.Zap => ParkEffectKind.Zap, ParkAccident.Burn => ParkEffectKind.Burn, ParkAccident.Frost => ParkEffectKind.Frost,
                ParkAccident.Poison => ParkEffectKind.Poison, ParkAccident.Ghost => ParkEffectKind.Ghost, ParkAccident.Splash => ParkEffectKind.Splash,
                ParkAccident.Pollen => ParkEffectKind.Pollen, ParkAccident.Sting => ParkEffectKind.Sting, ParkAccident.Lick => ParkEffectKind.Lick,
                ParkAccident.Psychic => ParkEffectKind.Psychic, _ => ParkEffectKind.Bite
            }, actor);
            // It did not mean it: the gentle ones look sorry, the rascals look pleased, the fierce do not care.
            var (pose, emote, mood, emotion) = resident.Temperament switch
            {
                ParkTemperament.Fierce => (ParkPose.Attack, ParkEmote.Anger, "Grumpy", "Angry"),
                ParkTemperament.Mischievous => (ParkPose.Hop, ParkEmote.Note, "Playful", "Happy"),
                _ when accident == ParkAccident.Lick => (ParkPose.Hop, ParkEmote.Heart, "Delighted", "Joyous"),
                _ => (ParkPose.Cringe, ParkEmote.Sweat, "Embarrassed", "Worried")
            };
            Add(actor, new Label(accident == ParkAccident.Lick ? "Licking you" : "Sorry about that", mood), new Act(pose, 1200, 0, emote),
                new Act(ParkPose.Idle, 1500, 0));
            return new(false, ParkLore.AccidentLine(accident, resident.Name), emotion, accident);
        }
        if (!Willing(actor, .25f))
        {
            Interrupt(actor);
            var away = new ParkPoint(actor.X + (actor.Random.Next(2) == 0 ? -18 : 18), actor.Y);
            Add(actor, new Label("Out of reach", "Aloof"), new GoTo(away, 1.4f), new Act(ParkPose.Idle, 1800, 0, ParkEmote.Ellipsis));
            return new(false, $"{resident.Name} steps out of reach before your hand gets there.", "Normal");
        }
        Interrupt(actor);
        Burst(ParkEffectKind.Hearts, actor);
        Add(actor, new Label("Enjoying a head pat", "Delighted"), new Act(ParkPose.Nod, 1200, 0, ParkEmote.Heart),
            new Gain(ParkNeed.Social, 5, 2), new Act(ParkPose.Idle, 1500, 0));
        if (resident.Species == 25) MimikyuSees(actor);
        return new(true, null, "Happy");
    }

    /// <summary>The player throws the ball toward a point. A playful Pokémon runs after it and
    /// brings it back; an aloof or lazy one just watches it roll (and a playful neighbor may go
    /// instead); a rascal runs off with it.</summary>
    public ParkReply Fetch(string id, ParkPoint toward) { lock (_gate) return FetchCore(id, toward); }

    private ParkReply FetchCore(string id, ParkPoint toward)
    {
        if (!_actors.TryGetValue(id, out var actor) || actor.Indoors) return ParkReply.None;
        var cell = actor.Navigator.CellNear(toward.X, toward.Y);
        if (cell < 0) return ParkReply.None;
        var landing = actor.Navigator.CanStand(toward.X, toward.Y) && actor.Navigator.Connected(new(actor.X, actor.Y), toward)
            ? toward : actor.Navigator.Center(cell);
        Interrupt(actor);
        var home = new ParkPoint(actor.X, actor.Y);
        var facing = ParkMotion.Facing(landing.X - home.X, landing.Y - home.Y);
        var temperament = actor.Resident.Temperament;
        var keen = temperament is ParkTemperament.Playful or ParkTemperament.Friendly or ParkTemperament.Gentle ? .2f : -.15f;
        if (!Willing(actor, keen) || temperament is ParkTemperament.Aloof or ParkTemperament.Lazy && actor.Care.Bond < 60)
        {
            if (temperament == ParkTemperament.Mischievous && FreePoint(actor.Navigator, actor.Random, actor) is { } hideout)
            {
                Add(actor, new Throw(landing), new Label("Running off with your ball", "Playful"), new GoTo(landing, 1.9f), new Pickup(),
                    new Act(ParkPose.Hop, 600, Emote: ParkEmote.Note), new GoTo(hideout, 1.9f), new Act(ParkPose.Spin, 1200), new Drop(),
                    new Gain(ParkNeed.Fun, 15));
                return new(false, ParkLore.RefusalLine(temperament, actor.Resident.Name, ParkCommand.Play, Roll(actor)), "Happy");
            }
            Add(actor, new Throw(landing), new Label("Watching the ball roll away", "Aloof"),
                new Act(ParkPose.Idle, 2500, facing, ParkEmote.Ellipsis), new Act(ParkPose.Idle, 1500, 4));
            var line = ParkLore.RefusalLine(temperament, actor.Resident.Name, ParkCommand.Play, Roll(actor));
            // A playful neighbor cannot resist a ball nobody wants.
            var keener = _actors.Values.Where(other => other != actor && !other.Busy && !other.IsAsleep && !other.Indoors &&
                    other.Resident.Temperament is ParkTemperament.Playful or ParkTemperament.Friendly &&
                    other.Navigator.Connected(new(other.X, other.Y), landing))
                .OrderBy(other => Distance(other.X, other.Y, landing.X, landing.Y)).FirstOrDefault();
            if (keener is not null)
            {
                Interrupt(keener);
                var back = new ParkPoint(keener.X, keener.Y);
                Add(keener, new Act(ParkPose.Idle, 500, Emote: ParkEmote.Exclaim), new Label("Chasing a ball nobody wanted", "Playful"),
                    new GoTo(landing, 1.9f), new Pickup(), new Act(ParkPose.Hop, 800, Emote: ParkEmote.Note), new GoTo(back, 1.3f),
                    new Act(ParkPose.Hop, 1200, 0, ParkEmote.Heart), new Drop(), new Gain(ParkNeed.Fun, 18, 2));
                line += $" {keener.Resident.Name} dashes after it instead!";
            }
            else Add(actor, new Drop());
            return new(false, line, temperament == ParkTemperament.Fierce ? "Angry" : "Normal");
        }
        Add(actor, new Label("Chasing the ball", "Playful"),
            new Throw(landing), new Act(ParkPose.Idle, 250, facing, ParkEmote.Exclaim),
            new GoTo(landing, 1.8f), new Pickup(), new Act(ParkPose.Hop, 800, Emote: ParkEmote.Note),
            new Label("Bringing the ball back", "Playful"), new GoTo(home, 1.3f),
            new Act(ParkPose.Hop, 1200, 0, ParkEmote.Heart), new Drop(), new Gain(ParkNeed.Fun, 18, 4));
        return new(true, null, "Joyous");
    }

    /// <summary>A berry from the player: loved, disliked or just eaten, by the nature's taste. A
    /// fierce one may snatch it and eat it alone.</summary>
    public ParkReply Feed(string id, int flavor, int nature) { lock (_gate) return FeedCore(id, flavor, nature); }

    private ParkReply FeedCore(string id, int flavor, int nature)
    {
        if (!_actors.TryGetValue(id, out var actor)) return ParkReply.None;
        var taste = ParkTaste.Reaction(nature, flavor);
        var berry = ParkTaste.Berries[Math.Clamp(flavor, 0, 4)];
        var name = actor.Resident.Name;
        var unwell = IsUnwell(actor.Care);
        if (actor.IsAsleep) { Interrupt(actor); Wake(actor); }
        else Interrupt(actor);
        actor.BerryFlavor = flavor;
        // Too weak to be difficult: it eats from your hand, and the berry puts it back on its feet.
        if (unwell)
        {
            Add(actor, new Label($"Eating the {berry} Berry, slowly", "Peaceful"), new Act(ParkPose.Eat, 6000, 0, Prop: ParkProp.Berry),
                new Gain(ParkNeed.Hunger, 30, 6), new Gain(ParkNeed.Energy, 25), new Act(ParkPose.Hop, 1400, Emote: ParkEmote.Sparkle),
                new Label("Feeling better", "Joyful"), new Act(ParkPose.Idle, 3000, 0, ParkEmote.Heart));
            return new(true, $"{name} eats the {berry} Berry slowly, nibble by nibble. A little color comes back to its face. It looks much better.", "Happy", Taste: taste);
        }
        if (!Willing(actor, .45f) && actor.Resident.Temperament is ParkTemperament.Fierce or ParkTemperament.Aloof or ParkTemperament.Feisty)
        {
            var spot = FreePoint(actor.Navigator, actor.Random, actor) ?? new ParkPoint(actor.X, actor.Y);
            Add(actor, new Label("Eating your berry, alone", "Aloof"), new Act(ParkPose.Idle, 400, 0, ParkEmote.Exclaim),
                new GoTo(spot), new Act(ParkPose.Eat, 3000, Prop: ParkProp.Berry), new Gain(ParkNeed.Hunger, 10));
            return new(false, $"{name} snatches the {berry} Berry from your hand and walks off to eat it alone.", "Normal", Taste: taste);
        }
        Step[] steps = taste switch
        {
            > 0 => [new Label($"Loving the {berry} Berry", "Joyful"),
                new Act(ParkPose.Eat, 5000, 0, ParkEmote.Heart, ParkProp.Berry), new Gain(ParkNeed.Hunger, 15, 8),
                new Act(ParkPose.Dance, 2200, Emote: ParkEmote.Sparkle), new Gain(ParkNeed.Fun, 6)],
            < 0 => [new Label($"Not a fan of the {berry} Berry", "Embarrassed"),
                new Act(ParkPose.Eat, 1800, 0, Prop: ParkProp.Berry), new Act(ParkPose.Cringe, 1200, 0, ParkEmote.Sweat),
                new Gain(ParkNeed.Hunger, 5, 1), new Act(ParkPose.Idle, 1500, 0, ParkEmote.Ellipsis)],
            _ => [new Label($"Eating the {berry} Berry", "Delighted"),
                new Act(ParkPose.Eat, 6000, 0, Prop: ParkProp.Berry), new Gain(ParkNeed.Hunger, 10, 4),
                new Act(ParkPose.Hop, 1400, Emote: ParkEmote.Heart)]
        };
        Add(actor, steps);
        // Now you know: the favorite or the disliked flavor shows in its journal from here on.
        if (taste != 0 || NatureFacts.IsNeutral(nature))
            actor.Care = actor.Care with { TasteKnown = actor.Care.TasteKnown | (taste > 0 ? 1 : taste < 0 ? 2 : 3) };
        if (taste < 0) actor.GrudgeUntil = Now + 3 * 60_000;
        return new(true, null, taste > 0 ? "Joyous" : taste < 0 ? "Sigh" : "Happy", Taste: taste);
    }
}
