namespace PKForge.Domain;

/// <summary>
/// Flyers: the true Flying types (birds, bats, Flying dragons; not the insects that flutter like
/// butterflies, nor Doduo and Dodrio) fly whenever they go further than a few steps, over
/// everything, trees, cliffs and water, a shadow on the ground under them, and land where they
/// can stand: a spring off the ground, a gust of wind and a hop when they touch down. Each lives
/// on the height that suits it (the crater's rim for the fiery ones, the lighthouse point for the
/// seabirds, the shrine's plateau for the gentle and the mystic, the snowy summit for the rest),
/// flies back there often and roosts there at night. In the air they show their own flight when
/// their sprite has one, else their resting pose (wings spread, gliding), never a walk. Nobody can
/// reach them up there; a call or a fright brings them down first.
/// </summary>
public sealed partial class ParkLife
{
    // Flying types that never leave the ground.
    private static readonly HashSet<int> Flightless = [84, 85];

    private static bool Winged(ParkLifeResident resident) =>
        resident.Types.Contains(ParkType.Flying) && !resident.Types.Contains(ParkType.Bug) && !Flightless.Contains(resident.Species);

    private static bool CanFly(ParkLifeResident resident) => !resident.WaterBound && Winged(resident);

    // Further than this, a flyer flies instead of walking.
    private const float Hop = 40;

    private void FlightWeights(ParkActor actor, Dictionary<Activity, float> weights)
    {
        if (!CanFly(actor.Resident) || Wet) return;
        weights[Activity.Fly] = 24;
        weights[Activity.Stroll] *= .5f;
        if (Phase == ParkDayPhase.Night) weights[Activity.Fly] *= .1f;
    }

    private bool PlanFlight(ParkActor actor)
    {
        var random = actor.Random;
        var navigator = actor.Navigator;
        // Home to its height most often (straight away when it is elsewhere), else somewhere far
        // to visit, or another of the park's heights.
        var home = HomePlace(actor.Resident);
        ParkPoint? target = (home.Contains(actor.X, actor.Y) ? random.Next(4, 20) : random.Next(0, 14)) switch
        {
            < 8 => FreePoint(navigator, random, actor, land: true, place: home),
            < 15 => FreePoint(navigator, random, actor, land: true, place: new ParkRegion("park", "", ParkEnvironmentId.Commons, 0, 0, _map.Width, _map.Height)),
            _ => _map.Spots.Where(s => s.Kind is ParkSpotKind.Statue or ParkSpotKind.Nap && _map.RegionAt(s.X, s.Y).High &&
                    navigator.CanStand(s.X, s.Y) && !Crowded(actor, s.X, s.Y, PersonalSpace))
                .Select(s => (ParkPoint?)new ParkPoint(s.X, s.Y)).OrderBy(_ => random.Next()).FirstOrDefault()
        };
        if (target is not { } to || Distance(actor.X, actor.Y, to.X, to.Y) < 80) return false;
        var place = _map.RegionAt(to.X, to.Y);
        actor.Plan.Enqueue(new Label(IsWholeOrHere(actor, place) ? "Flying over the park" : $"Flying to {place.Name}", "Joyful"));
        actor.Plan.Enqueue(new Act(ParkPose.Hop, 500, Emote: random.Next(3) == 0 ? ParkEmote.Note : ParkEmote.None));
        actor.Plan.Enqueue(new Fly(to));
        actor.Plan.Enqueue(new Label("Just landed", "Content"));
        actor.Plan.Enqueue(new Act(ParkPose.Idle, random.Next(1200, 2600), random.Next(8)));
        if (random.Next(2) == 0) actor.Plan.Enqueue(new Act(ParkPose.LookUp, 1400));
        return true;
    }

    // Night: home to its height, a nap spot there if one is free, and to sleep.
    private bool PlanRoost(ParkActor actor)
    {
        var home = HomePlace(actor.Resident);
        var bed = _map.Spots.Where(s => s.Kind == ParkSpotKind.Nap && home.Contains(s.X, s.Y) && actor.Navigator.CanStand(s.X, s.Y) &&
                !Crowded(actor, s.X, s.Y, PersonalSpace)).Select(s => (ParkPoint?)new ParkPoint(s.X, s.Y)).FirstOrDefault()
            ?? FreePoint(actor.Navigator, actor.Random, actor, land: true, place: home);
        if (bed is not { } to) return false;
        actor.Plan.Enqueue(new Label("Flying home to roost", "Sleepy"));
        if (Distance(actor.X, actor.Y, to.X, to.Y) > 60) actor.Plan.Enqueue(new Fly(to));
        else actor.Plan.Enqueue(new GoTo(to));
        actor.Plan.Enqueue(new Label($"Roosting at {home.Name}", "Sleepy"));
        actor.Plan.Enqueue(new Act(ParkPose.Lie, 1500));
        actor.Plan.Enqueue(new Act(ParkPose.Sleep, actor.Random.Next(60_000, 180_000), Emote: ParkEmote.Sleep));
        return true;
    }

    private bool IsWholeOrHere(ParkActor actor, ParkRegion place) => IsWhole(place) || place.Contains(actor.X, actor.Y);

    // The nearest place a Pokémon can stand under a point (where a flight must end).
    private static ParkPoint Landing(ParkActor actor, ParkPoint near)
    {
        if (actor.Navigator.CanStand(near.X, near.Y)) return near;
        var cell = actor.Navigator.CellNear(near.X, near.Y);
        return cell < 0 ? near : actor.Navigator.Center(cell);
    }

    private const float Cruise = 36;          // flying height, art pixels, for a short flight; higher for long ones

    // A hop off the ground before the flight, a hop on touching down after it (milliseconds).
    private const long Spring = 320, Touchdown = 480;

    // away: off over the edge of the map, not down to the ground.
    private void TakeOff(ParkActor actor, ParkPoint target, long now, bool away = false)
    {
        var to = away ? target : Landing(actor, target);
        var from = new ParkPoint(actor.X, actor.Y);
        var length = Distance(from.X, from.Y, to.X, to.Y);
        // The way bends a little to one side, as a bird's does.
        var side = (float)(actor.Random.NextDouble() - .5) * .5f;
        var bend = new ParkPoint((from.X + to.X) / 2 - (to.Y - from.Y) * side, (from.Y + to.Y) / 2 + (to.X - from.X) * side);
        var duration = (long)Math.Max(1800, length / (actor.Speed * 3.2f) * 1000);
        actor.Flight = new Flight(from, bend, to, now, duration, away);
        actor.InFlight = true;
        actor.SetPose(ParkPose.Hop, now);
        actor.Prop = ParkProp.None;
        if (actor.Navigator.CanStand(from.X, from.Y)) _effects.Add(new(ParkEffectKind.Gust, from.X, from.Y, now));
    }

    /// <returns>True once it has landed (or flown off the map).</returns>
    private bool Glide(ParkActor actor, long now)
    {
        if (actor.Flight is not { } flight) { actor.InFlight = false; actor.Altitude = 0; return true; }
        var since = now - flight.Start;
        // On the ground already, after touching down: a last hop, then the next thing.
        if (!actor.InFlight)
        {
            if (since < Spring + flight.Duration + Touchdown) return false;
            actor.Flight = null;
            actor.SetPose(ParkPose.Idle, now);
            return true;
        }
        // The spring off the ground.
        if (since < Spring) return false;
        var t = Math.Clamp((since - Spring) / (float)flight.Duration, 0, 1);
        // Gentle off and on: slow at take-off and touch-down, quickest in the middle.
        var s = t * t * (3 - 2 * t);
        float u = 1 - s;
        var x = u * u * flight.From.X + 2 * u * s * flight.Bend.X + s * s * flight.To.X;
        var y = u * u * flight.From.Y + 2 * u * s * flight.Bend.Y + s * s * flight.To.Y;
        if (MathF.Abs(x - actor.X) + MathF.Abs(y - actor.Y) > .05f) actor.Facing = ParkMotion.Facing(x - actor.X, y - actor.Y);
        actor.X = x; actor.Y = y;
        var length = Distance(flight.From.X, flight.From.Y, flight.To.X, flight.To.Y);
        var peak = Cruise + Math.Min(18, length * .02f);
        // Off over the edge it stays high; otherwise it climbs, cruises and comes down.
        var climb = Math.Clamp(flight.Away ? t / .2f : Math.Min(t, 1 - t) / .2f, 0, 1);
        // Wings beat: a gentle rise and fall while it cruises.
        var bob = climb >= 1 ? 1.5f * MathF.Sin((now - flight.Start) / 260f) : 0;
        actor.Altitude = peak * climb * climb * (3 - 2 * climb) + bob;
        if (actor.Pose != ParkPose.Fly) actor.SetPose(ParkPose.Fly, now);
        if (t < 1) return false;
        if (flight.Away) return true;
        // Touch-down: on its feet, a gust of wind under its wings, a hop.
        actor.X = flight.To.X; actor.Y = flight.To.Y;
        actor.Altitude = 0; actor.InFlight = false;
        actor.SetPose(ParkPose.Hop, now);
        _effects.Add(new(ParkEffectKind.Gust, actor.X, actor.Y, now));
        return false;
    }
}
