namespace PKForge.Domain;

/// <summary>A campfire in the middle of the park: lit by a Fire type on a dry evening, burning a
/// few minutes, with the others sitting round it.</summary>
public sealed class ParkCampfire
{
    public ParkPoint At { get; internal set; }
    public long LitAt { get; internal set; }
    public long Until { get; internal set; }
    public string LighterId { get; internal set; } = "";
}

/// <summary>Evenings round the campfire, calling a resident over, and residents falling unwell.</summary>
public sealed partial class ParkLife
{
    // The campfire burns on the home park's lawn, south of the statue.
    private ParkPoint FirePlace => _map.AtHome(150, 166);

    internal sealed record Light : Step;
    internal sealed record Settle : Step;

    /// <summary>The campfire, while it burns.</summary>
    public ParkCampfire? Campfire { get; private set; }

    /// <summary>Unwell: it went too long hungry and exhausted. It drags itself around until it eats
    /// and rests, and the healers come to see it.</summary>
    public static bool IsUnwell(ParkCareState care) => care.Hunger < 12 && care.Energy < 18;

    /// <summary>The player calls a resident to a spot (a long press on the grass). Those who trust you
    /// come running; the aloof pretend not to hear.</summary>
    public ParkReply Call(string id, ParkPoint at) { lock (_gate) return CallCore(id, at); }

    private ParkReply CallCore(string id, ParkPoint at)
    {
        if (!_actors.TryGetValue(id, out var actor) || actor.Indoors || actor.InGame) return ParkReply.None;
        var name = actor.Resident.Name;
        if (actor.IsAsleep) return new(false, $"{name} is fast asleep and does not hear you.", "Sigh");
        var cell = actor.Navigator.CellNear(at.X, at.Y);
        if (cell < 0) return ParkReply.None;
        var spot = actor.Navigator.CanStand(at.X, at.Y) && actor.Navigator.Connected(new(actor.X, actor.Y), at) ? at : actor.Navigator.Center(cell);
        Interrupt(actor);
        if (!Willing(actor, .2f))
        {
            Add(actor, new Label("Pretending not to hear you", "Aloof"), new Act(ParkPose.Idle, 2000, (actor.Facing + 4) % 8, ParkEmote.Ellipsis));
            return new(false, $"{name} glances your way, then goes back to what it was doing.", "Normal");
        }
        var keen = actor.Care.Bond >= 60;
        Add(actor, new Label(keen ? "Running to you" : "Coming over", "Happy"), new Act(ParkPose.Idle, 300, Emote: keen ? ParkEmote.Heart : ParkEmote.Exclaim),
            new GoTo(spot, keen ? 1.9f : 1.3f), new Act(ParkPose.Hop, 1200, 0, ParkEmote.Note), new Gain(ParkNeed.Social, 4, 1),
            new Act(ParkPose.Idle, 4000, 0));
        return new(true, null, "Happy");
    }

    /// <summary>All of today's goals are done: everyone who is awake and free throws a little party.</summary>
    public void Rejoice() { lock (_gate) RejoiceCore(); }

    private void RejoiceCore()
    {
        foreach (var actor in _actors.Values.Where(a => !a.IsAsleep && !a.Indoors && !a.InGame && !IsVisitor(a.Resident.Id)))
        {
            Interrupt(actor);
            Add(actor, new Label("Celebrating with you", "Joyous"), new Act(ParkPose.Hop, 1200, 0, ParkEmote.Exclaim),
                new Act(ParkPose.Dance, actor.Random.Next(3000, 5000), Emote: ParkEmote.Sparkle), new Gain(ParkNeed.Fun, 15), new Gain(ParkNeed.Social, 10));
        }
    }

    private bool CanLightFire(ParkActor actor) => Campfire is null && !Wet && Phase is ParkDayPhase.Evening or ParkDayPhase.Night &&
        actor.Resident.Types.Contains(ParkType.Fire) && !actor.Resident.Types.Contains(ParkType.Water) &&
        actor.Navigator.CanStand(FirePlace.X + 22, FirePlace.Y) && actor.Navigator.Connected(new(actor.X, actor.Y), FirePlace);

    // A Fire type walks to the fire place and lights it; the others will come and sit round.
    private bool PlanCampfire(ParkActor actor)
    {
        if (!CanLightFire(actor)) return false;
        Add(actor, new Label("Lighting a campfire", "Joyful"), new GoTo(new(FirePlace.X + 22, FirePlace.Y)), new Act(ParkPose.Charge, 1800, 6, ParkEmote.Exclaim),
            new Light(), new Settle(), new Label("Tending the campfire", "Peaceful"), new Act(ParkPose.Sit, actor.Random.Next(60_000, 120_000), 6, ParkEmote.Note));
        return true;
    }

    // Sitting round the fire: a free place on the ring, facing the flames.
    private bool PlanFireside(ParkActor actor)
    {
        if (Campfire is not { } fire || actor.Resident.WaterBound) return false;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            // Anywhere on the ring but straight in front, where it would hide the flames.
            var angle = (float)(actor.Random.NextDouble() * Math.PI * 2);
            if (MathF.Sin(angle) > .55f && MathF.Abs(MathF.Cos(angle)) < .8f) continue;
            var seat = new ParkPoint(fire.At.X + MathF.Cos(angle) * 26, fire.At.Y + MathF.Sin(angle) * 13);
            if (!actor.Navigator.CanStand(seat.X, seat.Y) || Crowded(actor, seat.X, seat.Y, 14) || !actor.Navigator.Connected(new(actor.X, actor.Y), seat)) continue;
            Add(actor, new Label("Sitting by the campfire", "Peaceful"), new GoTo(seat), new Settle(), new Act(ParkPose.Sit, actor.Random.Next(20_000, 50_000),
                ParkMotion.Facing(fire.At.X - seat.X, fire.At.Y - seat.Y), ParkEmote.Note), new Gain(ParkNeed.Social, 10), new Gain(ParkNeed.Fun, 8));
            return true;
        }
        return false;
    }

    private bool RunEveningStep(ParkActor actor, Step step, long now)
    {
        if (step is Settle) { actor.Settled = true; return true; }
        if (step is not Light) return false;
        if (Campfire is null && !Wet)
        {
            Campfire = new ParkCampfire { At = FirePlace, LitAt = now, Until = now + 4 * 60_000, LighterId = actor.Resident.Id };
            _moments.Add(new ParkMoment(ParkSocialEventKind.Rest, actor.Resident.Id, "", $"{actor.Resident.Name} lit a campfire, and one by one the others came to sit round it."));
        }
        return true;
    }

    private void UpdateEvening(long now)
    {
        // Rain puts the fire out, and it dies down on its own after a few minutes or at daybreak.
        if (Campfire is not { } fire || !(Wet || now > fire.Until || Phase is ParkDayPhase.Morning or ParkDayPhase.Day)) return;
        Campfire = null;
        foreach (var actor in _actors.Values.Where(a => a.Settled))
        {
            Interrupt(actor);
            Add(actor, new Label(Wet ? "Shaking off the rain" : "Watching the embers die", "Sad"),
                new Act(ParkPose.Idle, 1500, Emote: Wet ? ParkEmote.Sweat : ParkEmote.Ellipsis));
        }
    }

    // What the evening and its health change in its choice.
    private void EveningWeights(ParkActor actor, Dictionary<Activity, float> weights)
    {
        if (CanLightFire(actor)) weights[Activity.Campfire] = 40;
        if (Campfire is not null && !actor.Resident.WaterBound && !IsVisitor(actor.Resident.Id))
            weights[Activity.Fireside] = Phase == ParkDayPhase.Night ? 220 : 60;
        if (IsUnwell(actor.Care))
        {
            foreach (var key in weights.Keys.ToArray()) weights[key] *= .3f;
            weights[Activity.Nap] += 60;
            weights[Activity.Snack] += 60;
        }
    }
}
