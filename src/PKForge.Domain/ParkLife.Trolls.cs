namespace PKForge.Domain;

/// <summary>
/// The park's troublemakers: they steal berries out of paws, wake sleepers for fun, trip
/// walkers, pick on the timid, run off with the ball, and Zorua frames the innocent with its
/// illusions. Victims lose a little of what they need and hold it against them. Their family,
/// their love and their friends come running to take their side (one tells the troublemaker off,
/// the others stand by the victim); with none of them around, someone with a sense of justice
/// may step in. The same happens to whoever loses a real fight with a sworn rival.
/// </summary>
public sealed partial class ParkLife
{
    /// <summary>Someone was wronged: those who care about it may come. Without an aggressor, the
    /// troublemaker standing next to it.</summary>
    internal sealed record Defend(string VictimId, string? AggressorId = null) : Step;
    internal sealed record Frame(string InnocentId, string VictimId) : Step;

    private static readonly HashSet<int> Thieves = [198, 430, 215, 461, 509, 510, 52, 53, 302];
    private static readonly HashSet<int> Illusionists = [570, 571];
    private static readonly HashSet<int> Bullies = [674, 675, 261, 262, 859, 860, 861];
    private static readonly HashSet<int> Defenders = [447, 448, 475, 638, 639, 640, 647, 359, 625, 983];

    /// <summary>Whether a species makes trouble for the others (also any mischievous temperament).</summary>
    public static bool IsTroll(ParkLifeResident resident) =>
        resident.Has(ParkQuirk.Prankster) || Thieves.Contains(resident.Species) || Illusionists.Contains(resident.Species) ||
        Bullies.Contains(resident.Species) || resident.Temperament == ParkTemperament.Mischievous;

    // One piece of mischief that fits what the others are doing right now.
    private bool PlanMischief(ParkActor troll)
    {
        var name = troll.Resident.Name;
        var random = troll.Random;
        var others = _actors.Values.Where(o => o != troll && !o.Busy && !o.Indoors && !IsVisitor(o.Resident.Id) &&
            troll.Navigator.Connected(new(troll.X, troll.Y), new(o.X, o.Y))).ToList();
        if (others.Count == 0) return false;
        var options = new List<(string Kind, Func<bool> Try)>();
        // A different victim from last time when there is one, picked at random.
        ParkActor? Victim(IEnumerable<ParkActor> candidates)
        {
            var list = candidates.OrderBy(_ => random.Next()).ToList();
            return list.FirstOrDefault(o => o.Resident.Id != troll.LastVictim) ?? list.FirstOrDefault();
        }

        // Zorua and Zoroark wear an innocent's face for the prank, and the victim blames the innocent.
        if (Illusionists.Contains(troll.Resident.Species) && others.Count >= 2)
            options.Add(("illusion", () =>
            {
                var victim = others[random.Next(others.Count)];
                var innocent = others.Where(o => o != victim).OrderBy(_ => random.Next()).First();
                if (Beside(troll, victim) is not { } spot) return false;
                victim.PartnerId = troll.Resident.Id; troll.PartnerId = victim.Resident.Id;
                Add(troll, new Label($"Up to something, looking like {innocent.Resident.Name}", "Mischievous"), new Copy(innocent.Resident.Id),
                    new GoTo(spot, .8f), new Face(victim.Resident.Id), new Spark(ParkEffectKind.Scare),
                    new Engage(victim.Resident.Id, [new Act(ParkPose.Hurt, 600, Emote: ParkEmote.Exclaim), new Face(troll.Resident.Id),
                        new Label($"Furious at {innocent.Resident.Name}", "Grumpy"), new Act(ParkPose.Idle, 2000, Emote: ParkEmote.Anger)]),
                    new Act(ParkPose.Hop, 1500, Emote: ParkEmote.Note), new Frame(innocent.Resident.Id, victim.Resident.Id), new Release());
                return true;
            }));

        // Snatch a berry from someone eating.
        if (Victim(others.Where(o => o.Pose == ParkPose.Eat)) is { } eater && (Thieves.Contains(troll.Resident.Species) || random.Next(2) == 0))
            options.Add(("snatch", () =>
            {
                if (Beside(troll, eater) is not { } spot) return false;
                eater.PartnerId = troll.Resident.Id; troll.PartnerId = eater.Resident.Id;
                Add(troll, new Label($"Eyeing {eater.Resident.Name}'s berry", "Mischievous"), new GoTo(spot, 1.6f), new Face(eater.Resident.Id),
                    new Engage(eater.Resident.Id, [new Act(ParkPose.Cringe, 700, Emote: ParkEmote.Exclaim), new Label($"Robbed by {name}", "Grumpy"),
                        new Gain(ParkNeed.Hunger, -12), new Gain(ParkNeed.Fun, -6), new Act(ParkPose.Idle, 2200, Emote: ParkEmote.Anger), new Defend(eater.Resident.Id)]),
                    new Act(ParkPose.Eat, 2500, Prop: ParkProp.Berry), new Gain(ParkNeed.Hunger, 12), new Act(ParkPose.Hop, 900, Emote: ParkEmote.Note),
                    new Journal($"{name} snatched {eater.Resident.Name}'s berry right out of its paws and ate it in front of it.", eater.Resident.Id, ParkSocialEventKind.Conflict),
                    new Release());
                return true;
            }));

        // Wake a sleeper for fun.
        if (Victim(_actors.Values.Where(o => o != troll && o.IsAsleep && !o.Indoors && !o.Busy && !IsVisitor(o.Resident.Id) &&
                troll.Navigator.Connected(new(troll.X, troll.Y), new(o.X, o.Y)))) is { } sleeper)
            options.Add(("wake", () =>
            {
                if (Beside(troll, sleeper) is not { } spot) return false;
                sleeper.PartnerId = troll.Resident.Id; troll.PartnerId = sleeper.Resident.Id;
                Add(troll, new Label($"Creeping up on sleeping {sleeper.Resident.Name}", "Mischievous"), new GoTo(spot, .7f), new Face(sleeper.Resident.Id),
                    new Act(ParkPose.Attack, 700), new Spark(ParkEffectKind.Scare),
                    new Engage(sleeper.Resident.Id, [new Act(ParkPose.Wake, 900, Emote: ParkEmote.Exclaim), new Label($"Woken up by {name}", "Grumpy"),
                        new Gain(ParkNeed.Energy, -10), new Act(ParkPose.Idle, 2000, Emote: ParkEmote.Anger), new Defend(sleeper.Resident.Id)]),
                    new Act(ParkPose.Hop, 1600, Emote: ParkEmote.Note),
                    new Journal($"{name} woke {sleeper.Resident.Name} up just to see what would happen. {sleeper.Resident.Name} is not amused.", sleeper.Resident.Id, ParkSocialEventKind.Conflict),
                    new Release());
                return true;
            }));

        // Trip someone on the move.
        if (Victim(others.Where(o => o.IsWalking && !o.Resident.WaterBound)) is { } walker)
            options.Add(("trip", () =>
            {
                if (Beside(troll, walker) is not { } spot) return false;
                walker.PartnerId = troll.Resident.Id; troll.PartnerId = walker.Resident.Id;
                Add(troll, new Label($"Waiting for {walker.Resident.Name} to walk by", "Mischievous"), new GoTo(spot, 1.4f), new Face(walker.Resident.Id),
                    new Engage(walker.Resident.Id, [new Act(ParkPose.Trip, 1100, Emote: ParkEmote.Sweat), new Label($"Tripped by {name}", "Grumpy"),
                        new Gain(ParkNeed.Fun, -8), new Act(ParkPose.Idle, 1800, Emote: ParkEmote.Anger), new Defend(walker.Resident.Id)]),
                    new Act(ParkPose.Hop, 1500, Emote: ParkEmote.Note),
                    new Journal($"{name} stuck out a foot and tripped {walker.Resident.Name} on purpose.", walker.Resident.Id, ParkSocialEventKind.Conflict),
                    new Release());
                return true;
            }));

        // Pick on the timid.
        if ((Bullies.Contains(troll.Resident.Species) || troll.Resident.Temperament == ParkTemperament.Fierce) &&
            Victim(others.Where(o => o.Resident.Temperament == ParkTemperament.Timid)) is { } timid)
            options.Add(("bully", () =>
            {
                if (Beside(troll, timid) is not { } spot) return false;
                timid.PartnerId = troll.Resident.Id; troll.PartnerId = timid.Resident.Id;
                Add(troll, new Label($"Picking on {timid.Resident.Name}", "Mischievous"), new GoTo(spot, 1.2f), new Face(timid.Resident.Id),
                    new Engage(timid.Resident.Id, [new Face(troll.Resident.Id), new Act(ParkPose.Cringe, 1500, Emote: ParkEmote.Sweat),
                        new Label($"Picked on by {name}", "Scared"), new Gain(ParkNeed.Social, -10), new Gain(ParkNeed.Fun, -6), new Defend(timid.Resident.Id)]),
                    new Act(ParkPose.Attack, 900, Emote: ParkEmote.Anger), new Act(ParkPose.Pose, 1200),
                    new Journal($"{name} cornered {timid.Resident.Name} and would not let it pass. {timid.Resident.Name} has been hiding since.", timid.Resident.Id, ParkSocialEventKind.Conflict),
                    new Release());
                return true;
            }));

        // Run off with a ball lying around.
        if (Thieves.Contains(troll.Resident.Species) && Ball is { HolderId: null } loose && FreePoint(troll.Navigator, random, troll) is { } stash)
            options.Add(("ball", () =>
            {
                Add(troll, new Label("Stealing the ball", "Mischievous"), new GoTo(loose.To, 2), new Pickup(), new Act(ParkPose.Hop, 500, Emote: ParkEmote.Note),
                    new GoTo(stash, 2), new Drop(), new Journal($"{name} ran off with the ball. It is somewhere in the park now. Good luck."));
                return true;
            }));

        // Pick on Wobbuffet, and get countered.
        if (Victim(others.Where(o => o.Resident.Species is 202 or 360)) is { } wobbuffet)
            options.Add(("counter", () =>
            {
                if (Beside(troll, wobbuffet) is not { } spot) return false;
                wobbuffet.PartnerId = troll.Resident.Id; troll.PartnerId = wobbuffet.Resident.Id;
                Add(troll, new Label($"Sneaking up on {wobbuffet.Resident.Name}", "Mischievous"), new GoTo(spot, 1.2f), new Face(wobbuffet.Resident.Id),
                    new Act(ParkPose.Attack, 700),
                    new Engage(wobbuffet.Resident.Id, [new Face(troll.Resident.Id), new Act(ParkPose.Pose, 1400, Emote: ParkEmote.Exclaim),
                        new Spark(ParkEffectKind.Psychic), new Label("Countering", "Proud"), new Act(ParkPose.Nod, 1200)]),
                    new Act(ParkPose.Idle, 500), new Act(ParkPose.Tumble, 1500, Emote: ParkEmote.Sweat),
                    new Journal($"{name} tried to trip {wobbuffet.Resident.Name}. {wobbuffet.Resident.Name} countered. {name} is still on its back.",
                        wobbuffet.Resident.Id, ParkSocialEventKind.Conflict),
                    new Release());
                return true;
            }));

        // Pull a face right in front of someone, then run off giggling.
        if (Victim(others.Where(o => !o.IsWalking && !o.Resident.WaterBound)) is { } watcher)
            options.Add(("face", () =>
            {
                if (Beside(troll, watcher) is not { } spot) return false;
                watcher.PartnerId = troll.Resident.Id; troll.PartnerId = watcher.Resident.Id;
                Add(troll, new Label($"Sneaking up on {watcher.Resident.Name}", "Mischievous"), new GoTo(spot, .9f), new Face(watcher.Resident.Id),
                    new Engage(watcher.Resident.Id, [new Face(troll.Resident.Id), new Act(ParkPose.Cringe, 900, Emote: ParkEmote.Exclaim),
                        new Label($"Startled by {name}'s face", "Grumpy"), new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Anger)]),
                    new Act(ParkPose.Pose, 900, Emote: ParkEmote.Note), new Act(ParkPose.Hop, 1200, Emote: ParkEmote.Note),
                    new Journal($"{name} popped up right in front of {watcher.Resident.Name} and pulled the silliest face. {watcher.Resident.Name} did not laugh.",
                        watcher.Resident.Id, ParkSocialEventKind.Conflict),
                    new Release());
                return true;
            }));

        // Echo someone's call from somewhere else, and watch it look everywhere.
        if (Victim(others.Where(o => !o.IsWalking && !o.Resident.WaterBound)) is { } listener &&
            FreePoint(troll.Navigator, random, troll, land: true) is { } hideout && Distance(hideout.X, hideout.Y, listener.X, listener.Y) is > 40 and < 140)
            options.Add(("echo", () =>
            {
                listener.PartnerId = troll.Resident.Id; troll.PartnerId = listener.Resident.Id;
                Add(troll, new Label($"Hiding from {listener.Resident.Name}", "Mischievous"), new GoTo(hideout, .9f),
                    new Act(ParkPose.Sing, 1600, Emote: ParkEmote.Note, Prop: ParkProp.Notes),
                    new Engage(listener.Resident.Id, [new Act(ParkPose.LookUp, 1200, Emote: ParkEmote.Question), new Act(ParkPose.Idle, 900, (listener.Facing + 2) % 8),
                        new Act(ParkPose.Idle, 900, (listener.Facing + 6) % 8, ParkEmote.Question), new Label("Looking for whoever called", "Confused")]),
                    new Act(ParkPose.Hop, 1500, Emote: ParkEmote.Note),
                    new Journal($"{name} hid and called out in {listener.Resident.Name}'s own voice. {listener.Resident.Name} looked everywhere for itself.",
                        listener.Resident.Id, ParkSocialEventKind.Play),
                    new Release());
                return true;
            }));

        // Not the same trick twice in a row when it knows another.
        foreach (var option in options.OrderBy(o => o.Kind == troll.LastTrick ? 1 : 0).ThenBy(_ => random.Next()))
            if (option.Try())
            {
                troll.LastTrick = option.Kind;
                troll.LastVictim = troll.PartnerId;
                if (troll.LastVictim is { } victim) Add(troll, new Tally($"tricks:{troll.Resident.Id}:{victim}", victim));
                return true;
            }
        return false;
    }

    private ParkPoint? Beside(ParkActor mover, ParkActor target)
    {
        foreach (var side in new[] { mover.X < target.X ? -1 : 1, mover.X < target.X ? 1 : -1 })
        {
            var point = new ParkPoint(target.X + side * 16, target.Y);
            if (mover.Navigator.CanStand(point.X, point.Y) && mover.Navigator.Connected(new(mover.X, mover.Y), point)) return point;
        }
        return null;
    }

    private bool RunTrollStep(ParkActor actor, Step step)
    {
        switch (step)
        {
            case Frame frame:
                // The victim saw the innocent's face: their rivalry grows, though the innocent did nothing.
                if (_actors.TryGetValue(frame.InnocentId, out var innocent) && _actors.TryGetValue(frame.VictimId, out var victim))
                    _moments.Add(new ParkMoment(ParkSocialEventKind.Conflict, frame.VictimId, frame.InnocentId,
                        $"{victim.Resident.Name} is furious at {innocent.Resident.Name} for a prank {innocent.Resident.Name} never pulled. Somewhere, {actor.Resident.Name} is grinning."));
                actor.Disguise = null;
                return true;
            case Defend defend:
                _defenses.Add((defend.VictimId, defend.AggressorId, Now + 15_000));
                return true;
        }
        return false;
    }

    private readonly List<(string VictimId, string? AggressorId, long Until)> _defenses = new();

    // Pending defenses wait for the troll to finish its mischief, then someone steps in.
    private void UpdateDefenses(long now)
    {
        if (_defenses.Count == 0 || now % 500 >= TickMilliseconds) return;
        for (var i = _defenses.Count - 1; i >= 0; i--)
            if (now > _defenses[i].Until || CallDefender(_defenses[i].VictimId, _defenses[i].AggressorId)) _defenses.RemoveAt(i);
    }

    // Those who care about the victim come running; one tells the aggressor off, the others stand
    // by the victim. With nobody of its own around, someone with a sense of justice may step in.
    /// <returns>True once handled (someone went, or nobody will).</returns>
    private bool CallDefender(string victimId, string? aggressorId)
    {
        if (!_actors.TryGetValue(victimId, out var victim)) return true;
        var troll = aggressorId is not null
            ? _actors.GetValueOrDefault(aggressorId)
            : _actors.Values.FirstOrDefault(a => a.Resident.Id != victimId && IsTroll(a.Resident) && Distance(a.X, a.Y, victim.X, victim.Y) < 40);
        if (troll is null) return true;
        if (troll.Busy) return false;
        bool CanCome(ParkActor a) => a != victim && a != troll && !a.Busy && !a.IsAsleep && !a.Indoors && !a.Hidden && !IsVisitor(a.Resident.Id) &&
            !a.Resident.WaterBound && a.Navigator.Connected(new(a.X, a.Y), new(troll.X, troll.Y));
        // Its own: love first, then family, then friends (by species or by what happened between them).
        int Closeness(ParkActor a) => Feeling(a, victim) switch
        {
            ParkPairKind.Love => 3, ParkPairKind.Family => 2, ParkPairKind.Friends => 1,
            _ => (Relation?.Invoke(a.Resident.Id, victimId).Friendship ?? 0) >= 40 ? 1 : 0
        };
        var allies = _actors.Values.Where(a => CanCome(a) && Closeness(a) > 0 && Distance(a.X, a.Y, victim.X, victim.Y) < 420 &&
                Feeling(a, troll) != ParkPairKind.Fears && !InAwe(a, troll))
            .OrderByDescending(Closeness).ThenBy(a => Distance(a.X, a.Y, troll.X, troll.Y)).Take(3).ToArray();
        var defender = allies.FirstOrDefault() ?? _actors.Values.Where(a => CanCome(a) && !InAwe(a, troll) &&
                (Defenders.Contains(a.Resident.Species) || a.Resident.Temperament is ParkTemperament.Fierce or ParkTemperament.Feisty))
            .OrderByDescending(a => Defenders.Contains(a.Resident.Species)).ThenBy(a => Distance(a.X, a.Y, troll.X, troll.Y)).FirstOrDefault();
        // A stranger does not always bother; its own always come.
        if (defender is null || allies.Length == 0 && defender.Random.Next(3) == 0 || Beside(defender, troll) is not { } spot) return true;
        string hero = defender.Resident.Name, rascal = troll.Resident.Name, poor = victim.Resident.Name;
        Interrupt(defender);
        Interrupt(troll);
        defender.PartnerId = troll.Resident.Id; troll.PartnerId = defender.Resident.Id;
        // The troublemaker notices and waits, a little too late to run.
        Add(troll, new Face(defender.Resident.Id), new Act(ParkPose.Idle, 20_000, Emote: ParkEmote.Sweat), new Release());
        var supporters = allies.Skip(1).ToArray();
        var why = Closeness(defender) switch { 3 => $"{poor}'s sweetheart {hero}", 2 => $"{poor}'s family, {hero},", 1 => $"{poor}'s friend {hero}", _ => hero };
        var journal = allies.Length > 0
            ? $"{why} came running when {rascal} went after {poor}, and gave {rascal} a piece of its mind." +
              (supporters.Length > 0 ? $" {Names(supporters)} stood by {poor} until it felt better." : "")
            : $"{hero} stood up for {poor} and chased {rascal} off. The park cheered, quietly.";
        Add(defender, new Label($"Standing up for {poor}", "Determined"), new Act(ParkPose.Idle, 400, Emote: ParkEmote.Exclaim), new GoTo(spot, 1.6f),
            new Face(troll.Resident.Id), new Spotlight($"{hero} is standing up for {poor}!", 2, 15_000),
            new Engage(troll.Resident.Id, [new Act(ParkPose.Hurt, 700, Emote: ParkEmote.Sweat), new Act(ParkPose.Tumble, 1100),
                new Label($"Told off by {hero}", "Embarrassed"), new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Ellipsis)]),
            new Act(ParkPose.Attack, 900, Emote: ParkEmote.Anger), new Act(ParkPose.Pose, 1400, Emote: ParkEmote.Sparkle),
            new Journal(journal, troll.Resident.Id, ParkSocialEventKind.Conflict), new Release());
        // The others go and stand by the victim, glare at the troublemaker, then comfort it.
        foreach (var friend in supporters)
        {
            if (Beside(friend, victim) is not { } by) continue;
            Interrupt(friend);
            Add(friend, new Label($"Running to {poor}'s side", "Worried"), new GoTo(by, 1.7f),
                new Act(ParkPose.Idle, 2200, ParkMotion.Facing(troll.X - by.X, troll.Y - by.Y), ParkEmote.Anger),
                new Face(victimId), new Label($"Comforting {poor}", "Caring"), new Act(ParkPose.Nod, 1500, Emote: ParkEmote.Heart), new Gain(ParkNeed.Social, 8));
        }
        if (!victim.Busy && !victim.IsAsleep)
        {
            // It stops what it was doing to see its defender (and the moment gets its caption).
            Interrupt(victim);
            Add(victim, new Act(ParkPose.Idle, 2500, ParkMotion.Facing(troll.X - victim.X, troll.Y - victim.Y), ParkEmote.Exclaim),
                new Label($"Glad {hero} came", "Relieved"), new Act(ParkPose.Hop, 900, Emote: ParkEmote.Heart), new Gain(ParkNeed.Social, 10));
        }
        return true;
    }
}
