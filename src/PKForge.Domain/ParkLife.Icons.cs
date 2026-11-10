namespace PKForge.Domain;

/// <summary>
/// The most famous things Pokémon do, each touching the others or the park: Lapras ferries the
/// ones who cannot swim across the water, Lotad carries a little one on its leaf; Psyduck wades in,
/// forgets it cannot swim and a swimmer brings it back; Slowpoke fishes with its tail; Croagunk
/// stops anyone making eyes at someone with a Poison Jab and drags it off; Wobbuffet walks into the
/// middle of every gathering and salutes; Snorlax and Munchlax strip the berry bushes bare; Diglett
/// pops up right under someone's nap; the dogs bark at newcomers; Mimikyu droops when Pikachu gets
/// patted; Jigglypuff draws on the faces of those who fell asleep during its song; Gengar slips into
/// someone's shadow.
/// </summary>
public sealed partial class ParkLife
{
    /// <summary>Climb onto (or slip into the shadow of) another resident and go where it goes.</summary>
    internal sealed record Mount(string MountId, float Dx, float Dy, float Lift, bool Hidden = false) : Step;
    /// <summary>The one carrying puts its rider down where it can stand.</summary>
    internal sealed record Unload(string RiderId, ParkPoint At, string? Line = null) : Step;
    /// <summary>Gets off whoever it was riding (or hiding in), here.</summary>
    internal sealed record GetOff(ParkPoint At) : Step;
    /// <summary>Carrying that rider from now on.</summary>
    internal sealed record Carry(string RiderId) : Step;
    /// <summary>A Poison Jab: the one gushing over someone is knocked flat and dragged off.</summary>
    internal sealed record Jab(string VictimId, ParkPoint Away) : Step;
    /// <summary>Eaten bare: nobody finds a berry on this bush for a while.</summary>
    internal sealed record Strip(ParkSpot Bush) : Step;
    /// <summary>On arrival at a bush: if it has been stripped, there is nothing to eat.</summary>
    internal sealed record CheckBush(ParkSpot Bush) : Step;
    /// <summary>Marker on the faces of those asleep within reach.</summary>
    internal sealed record Doodle(float Radius) : Step;

    private static readonly HashSet<int> Ferries = [131, 270, 226];
    private static readonly HashSet<int> Dogs = [58, 59, 209, 210, 228, 229, 261, 262, 309, 310, 506, 507, 508, 744, 745, 835, 836, 926, 927];

    private readonly Dictionary<ParkSpot, long> _stripped = new();
    private const long StrippedFor = 10 * 60_000;

    private static bool HasIcon(ParkLifeResident resident) => resident.Species is 54 or 79 or 199 or 50 or 51 or 94 or 399 or 400 || ChorusLeaders.Contains(resident.Species) || Ferries.Contains(resident.Species);

    /// <returns>True when its famous habit fits right now and is planned.</returns>
    private bool PlanIcon(ParkActor actor)
    {
        var random = actor.Random;
        return actor.Resident.Species switch
        {
            131 or 226 => Phase != ParkDayPhase.Night && PlanFerry(actor, little: false),
            270 => Phase != ParkDayPhase.Night && PlanFerry(actor, little: true),
            54 => Phase != ParkDayPhase.Night && random.Next(2) == 0 && PlanPsyduckWade(actor),
            79 or 199 => PlanFishing(actor),
            50 or 51 => random.Next(2) == 0 && PlanPopUp(actor),
            399 or 400 => Phase != ParkDayPhase.Night && PlanDamWork(actor),
            186 or 62 => PlanChorus(actor, Now),
            94 => Phase is ParkDayPhase.Evening or ParkDayPhase.Night && random.Next(2) == 0 && PlanShadow(actor),
            _ => false
        };
    }

    private bool RunIconStep(ParkActor actor, Step step, long now)
    {
        switch (step)
        {
            case Mount mount:
                if (!_actors.ContainsKey(mount.MountId)) return true;
                actor.RidingOn = mount.MountId;
                actor.RideOffset = (mount.Dx, mount.Dy);
                actor.Altitude = mount.Lift;
                actor.InShadow = mount.Hidden;
                return true;
            case GetOff off:
                Dismount(actor);
                if (actor.Navigator.CanStand(off.At.X, off.At.Y)) { actor.X = off.At.X; actor.Y = off.At.Y; }
                return true;
            case Carry carry:
                actor.Carrying = carry.RiderId;
                return true;
            case Unload unload:
                actor.Carrying = null;
                if (_actors.TryGetValue(unload.RiderId, out var rider) && rider.RidingOn == actor.Resident.Id)
                {
                    Interrupt(rider);
                    var at = rider.Navigator.CanStand(unload.At.X, unload.At.Y) ? unload.At : SafeGround(rider);
                    rider.X = at.X; rider.Y = at.Y;
                    Add(rider, new Label($"Thanking {actor.Resident.Name}", "Happy"), new Face(actor.Resident.Id),
                        new Act(ParkPose.Hop, 1200, Emote: ParkEmote.Heart), new Gain(ParkNeed.Fun, 10));
                    if (unload.Line is { } line) _moments.Add(new ParkMoment(ParkSocialEventKind.Help, actor.Resident.Id, rider.Resident.Id, line));
                }
                return true;
            case Jab jab:
                if (_actors.TryGetValue(jab.VictimId, out var victim) && !victim.Indoors)
                {
                    Burst(ParkEffectKind.Poison, victim);
                    Highlight(actor, $"{actor.Resident.Name} Poison-jabbed {victim.Resident.Name}!", 2, 15_000);
                    Interrupt(victim);
                    Add(victim, new Label($"Poison-jabbed by {actor.Resident.Name}", "Embarrassed"), new Act(ParkPose.Hurt, 700, Emote: ParkEmote.Exclaim),
                        new Act(ParkPose.Tumble, 1400, Emote: ParkEmote.Sweat), new Label($"Being dragged off by {actor.Resident.Name}", "Embarrassed"),
                        new GoTo(jab.Away, .45f), new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Ellipsis));
                }
                return true;
            case Strip strip:
                _stripped[strip.Bush] = now + StrippedFor;
                return true;
            case CheckBush check:
                if (_stripped.GetValueOrDefault(check.Bush) <= now) return true;
                actor.Plan.Clear();
                actor.FoundBareAt = now;
                Add(actor, new Label("Found the berry bush stripped bare", "Grumpy"), new Act(ParkPose.LookUp, 1300, check.Bush.Facing, ParkEmote.Question),
                    new Act(ParkPose.Idle, 1500, check.Bush.Facing, ParkEmote.Ellipsis));
                if (_actors.Values.FirstOrDefault(g => g.Resident.Has(ParkQuirk.Glutton) && g != actor) is { } glutton && actor.Random.Next(3) == 0)
                    Add(actor, new Journal($"{actor.Resident.Name} came for berries and found the bush bare. It knows exactly who did it, and it is looking at {glutton.Resident.Name}.",
                        glutton.Resident.Id, ParkSocialEventKind.Conflict));
                return true;
            case Doodle doodle:
                var marked = Near(actor, doodle.Radius).Where(o => o.IsAsleep && !o.Indoors).ToArray();
                foreach (var sleeper in marked) sleeper.DoodledUntil = now + 15 * 60_000;
                if (marked.Length > 0) Highlight(actor, $"{actor.Resident.Name} is drawing on sleeping faces!", 2, 20_000);
                if (marked.Length > 0)
                    _moments.Add(new ParkMoment(ParkSocialEventKind.Conflict, actor.Resident.Id, marked[0].Resident.Id,
                        $"{actor.Resident.Name} drew all over {Names(marked)}'s {(marked.Length == 1 ? "face" : "faces")} while they slept through its song. They have no idea yet."));
                return true;
        }
        return false;
    }

    // The nearest place it can stand, from where it is.
    private static ParkPoint SafeGround(ParkActor actor)
    {
        if (actor.Navigator.CanStand(actor.X, actor.Y)) return new(actor.X, actor.Y);
        var cell = actor.Navigator.CellNear(actor.X, actor.Y);
        return cell < 0 ? new(actor.X, actor.Y) : actor.Navigator.Center(cell);
    }

    /// <summary>A rider goes where its mount goes; it gets off safely if the mount stops carrying it.</summary>
    private void Ride(ParkActor rider)
    {
        if (rider.RidingOn is not { } mountId) return;
        if (!_actors.TryGetValue(mountId, out var mount) || mount.Carrying != rider.Resident.Id && !rider.InShadow)
        {
            // The mount stopped carrying it (called away, gone): off, and on with its own day.
            Interrupt(rider);
            return;
        }
        rider.X = mount.X + rider.RideOffset.Dx;
        rider.Y = mount.Y + rider.RideOffset.Dy;
    }

    private void Dismount(ParkActor rider)
    {
        rider.RidingOn = null;
        rider.InShadow = false;
        if (!rider.InFlight) rider.Altitude = 0;
        var at = SafeGround(rider);
        rider.X = at.X; rider.Y = at.Y;
    }

    // ── Ferries ──────────────────────────────────────────────────────────────

    // A swimmer offers a lift across the water to someone on the shore who cannot swim.
    private bool PlanFerry(ParkActor ferry, bool little)
    {
        if (ferry.Submerged || ferry.Carrying is not null) return false;
        var shores = _map.Spots.Where(s => s.Kind is ParkSpotKind.Shore or ParkSpotKind.Gate).ToArray();
        // Water in front of a shore spot, that the ferry can reach (worked out once per spot).
        var landings = new Dictionary<ParkSpot, ParkPoint?>();
        ParkPoint? Landing(ParkSpot shore) => landings.TryGetValue(shore, out var known) ? known : landings[shore] = FindLanding(shore);
        ParkPoint? FindLanding(ParkSpot shore)
        {
            var cell = ferry.Navigator.CellNear(shore.X, shore.Y + 18);
            if (cell < 0) return null;
            var water = ferry.Navigator.Center(cell);
            return Distance(water.X, water.Y, shore.X, shore.Y) < 40 && ferry.Navigator.Connected(new(ferry.X, ferry.Y), water) ? water : null;
        }
        var random = ferry.Random;
        foreach (var from in shores.OrderBy(s => Distance(s.X, s.Y, ferry.X, ferry.Y)).Take(4))
        {
            if (Landing(from) is not { } pickup) continue;
            var passenger = _actors.Values.Where(o => o != ferry && Free(o) && !o.Resident.Swims && !CanFly(o.Resident) && (!little || Little(o.Resident)) &&
                    Distance(o.X, o.Y, from.X, from.Y) < 260 && o.Navigator.CanStand(from.X, from.Y) && o.Navigator.Connected(new(o.X, o.Y), new(from.X, from.Y)))
                .OrderBy(o => Distance(o.X, o.Y, from.X, from.Y)).FirstOrDefault();
            if (passenger is null) continue;
            var to = shores.Where(s => s != from && Distance(s.X, s.Y, from.X, from.Y) > 120 && passenger.Navigator.CanStand(s.X, s.Y))
                .Select(s => (Shore: s, Water: Landing(s))).Where(p => p.Water is { } w && ferry.Navigator.Connected(pickup, w))
                .OrderBy(_ => random.Next()).FirstOrDefault();
            if (to.Water is not { } dropoff) continue;
            string them = passenger.Resident.Name, me = ferry.Resident.Name;
            var line = little
                ? $"{them} rode across the water on {me}'s leaf, very carefully, and did not get its feet wet."
                : $"{me} carried {them} across the water on its back, singing all the way.";
            ParkActor[] cast = [ferry, passenger];
            Cast(ferry, cast, 2, Now, (member, _, troupe) => member == ferry
                ?
                [
                    new Label($"Swimming over to {them}", "Friendly"), new GoTo(pickup, .9f), new Muster(troupe), new Carry(passenger.Resident.Id),
                    new Spotlight(little ? $"{them} is riding across the water on {me}'s leaf!" : $"{me} is ferrying {them} across the water!", 3, 45_000),
                    new Act(ParkPose.Float, 1200, Emote: ParkEmote.Note), new Label($"Carrying {them} across the water", "Happy"),
                    new GoTo(dropoff, little ? .5f : .7f), new Unload(passenger.Resident.Id, new(to.Shore.X, to.Shore.Y), line),
                    new Tally($"ferry:{ferry.Resident.Id}:{passenger.Resident.Id}", passenger.Resident.Id),
                    new Act(ParkPose.Float, 1500, Emote: ParkEmote.Heart)
                ]
                :
                [
                    new Label("Heading down to the water", "Curious"), new GoTo(new(from.X, from.Y)), new Muster(troupe),
                    new Act(ParkPose.Idle, 900, Emote: ParkEmote.Exclaim),
                    new Mount(ferry.Resident.Id, 0, 1, little ? 5 : 9), new Label($"Riding across the water on {me}", "Joyful"),
                    new Act(ParkPose.Sit, 180_000, Emote: ParkEmote.Note)
                ]);
            return true;
        }
        return false;
    }

    // ── Psyduck ──────────────────────────────────────────────────────────────

    private bool PlanPsyduckWade(ParkActor psyduck)
    {
        if (Spot(psyduck, ParkSpotKind.Shore) is not { } shore || WaterBy(psyduck, shore) is not { } water) return false;
        var name = psyduck.Resident.Name;
        var land = new ParkPoint(shore.X, shore.Y);
        psyduck.RescueShore = land;
        Add(psyduck, new Label("Wading into the water", "Curious"), new GoTo(land), new GoTo(water, .6f),
            new Act(ParkPose.Float, 1000), new Label("Forgot it cannot swim", "Scared"), new Spotlight($"{name} forgot it can't swim! Talk to it to pull it out", 3, 25_000, "Overboard!"),
            new Act(ParkPose.Hurt, 800, Emote: ParkEmote.Exclaim), new Spark(ParkEffectKind.Splash), new Act(ParkPose.Cringe, 900, Emote: ParkEmote.Sweat),
            new Spark(ParkEffectKind.Splash));
        // A swimmer that swims to it (a flyer would just fly, which is no rescue).
        var rescuer = _actors.Values.Where(o => o != psyduck && o.Resident.Swims && !CanFly(o.Resident) && o.Resident.Species != 54 && Free(o) &&
                Distance(o.X, o.Y, water.X, water.Y) < 320 && o.Navigator.Connected(new(o.X, o.Y), water))
            .OrderBy(o => Distance(o.X, o.Y, water.X, water.Y)).FirstOrDefault();
        if (rescuer is null)
        {
            Add(psyduck, new Act(ParkPose.Hurt, 900), new Spark(ParkEffectKind.Splash), new GoTo(land, 1.6f), new Act(ParkPose.Idle, 2000, Emote: ParkEmote.Sweat),
                new Journal($"{name} waded into the water and forgot it cannot swim. It scrambled out on its own. Nobody saw, and it is telling no one."));
            return true;
        }
        // The rescuer sees it, swims over, and brings it back to the shore.
        Interrupt(rescuer);
        psyduck.PartnerId = rescuer.Resident.Id; rescuer.PartnerId = psyduck.Resident.Id;
        Add(psyduck, new Act(ParkPose.Hurt, 20_000, Emote: ParkEmote.Sweat), new Release());
        var beside = new ParkPoint(water.X + 12, water.Y);
        if (!rescuer.Navigator.CanStand(beside.X, beside.Y)) beside = water;
        // It notices only once Psyduck is in the water and panicking: as long as that walk takes, and the first splashes.
        var walk = (Distance(psyduck.X, psyduck.Y, land.X, land.Y) + Distance(land.X, land.Y, water.X, water.Y) / .6f) / psyduck.Speed * 1000;
        Add(rescuer, new Act(ParkPose.Idle, (int)walk + 2500), new Act(ParkPose.Idle, 900, Emote: ParkEmote.Exclaim),
            new Label($"Swimming to rescue {name}", "Determined"), new GoTo(beside, 1.5f),
            new Face(psyduck.Resident.Id),
            new Engage(psyduck.Resident.Id, [new Label($"Rescued by {rescuer.Resident.Name}", "Embarrassed"), new GoTo(land, .7f),
                new Act(ParkPose.Idle, 1800, Emote: ParkEmote.Sweat), new Act(ParkPose.Nod, 1200, Emote: ParkEmote.Heart)]),
            new Label($"Bringing {name} back to the shore", "Caring"), new GoTo(land, .7f), new Act(ParkPose.Nod, 1200, Emote: ParkEmote.Heart),
            new Journal($"{name} waded into the water, forgot it cannot swim, and panicked. {rescuer.Resident.Name} swam over and brought it back to the shore.",
                psyduck.Resident.Id, ParkSocialEventKind.Help),
            new Release());
        return true;
    }

    // A little way into the water from a shore spot: the way it faces first, then the other ways.
    private ParkPoint? WaterBy(ParkActor actor, ParkSpot shore)
    {
        (float X, float Y)[] ways = [(0, 1), (1, 0), (0, -1), (-1, 0)];
        var facing = shore.Facing switch { 2 => 1, 4 => 2, 6 => 3, _ => 0 };
        foreach (var (dx, dy) in ways.Skip(facing).Concat(ways.Take(facing)))
            for (var d = 12; d <= 48; d += 6)
            {
                var point = new ParkPoint(shore.X + dx * d, shore.Y + dy * d);
                // Not right at the edge of the map.
                if (point.X < 16 || point.Y < 16 || point.X > _map.Width - 16 || point.Y > _map.Height - 16) continue;
                if (_map.TerrainAt(point.X, point.Y) == ParkTerrain.Water && actor.Navigator.CanStand(point.X, point.Y) &&
                    actor.Navigator.Connected(new(shore.X, shore.Y), point))
                    return point;
            }
        return null;
    }

    // ── Slowpoke ─────────────────────────────────────────────────────────────

    private bool PlanFishing(ParkActor slowpoke)
    {
        var spot = Spot(slowpoke, ParkSpotKind.Shore) ?? _map.Spots.FirstOrDefault(s => s.Kind == ParkSpotKind.Gate && _map.RegionAt(s.X, s.Y).Id == "coast" &&
            slowpoke.Navigator.CanStand(s.X, s.Y) && slowpoke.Navigator.Connected(new(slowpoke.X, slowpoke.Y), new(s.X, s.Y)));
        if (spot is null) return false;
        var name = slowpoke.Resident.Name;
        var random = slowpoke.Random;
        Add(slowpoke, new Label("Off to go fishing", "Calm"), new GoTo(new(spot.X, spot.Y)), new Label("Fishing with its tail", "Peaceful"),
            new Act(ParkPose.Sit, random.Next(20_000, 40_000), spot.Facing, ParkEmote.Ellipsis));
        if (random.Next(2) == 0)
        {
            // A bite. Slowpoke takes a while to notice.
            var shellder = _actors.Values.FirstOrDefault(o => o.Resident.Species == 90 && !o.Busy && Distance(o.X, o.Y, spot.X, spot.Y) < 300);
            Add(slowpoke, new Spark(ParkEffectKind.Splash), new Label("Something is biting its tail", "Peaceful"),
                new Act(ParkPose.Sit, random.Next(6000, 10_000), spot.Facing), new Act(ParkPose.Idle, 1500, spot.Facing, ParkEmote.Question),
                new Act(ParkPose.Hop, 1200, spot.Facing, ParkEmote.Exclaim),
                new Journal(shellder is not null
                    ? $"{shellder.Resident.Name} clamped onto {name}'s tail while it was fishing. For a moment, it looked a lot like a Slowbro."
                    : $"{name} sat fishing with its tail for a long time. Something bit. {name} noticed about ten seconds later."));
        }
        return true;
    }

    // ── Diglett ──────────────────────────────────────────────────────────────

    private bool PlanPopUp(ParkActor diglett)
    {
        var sleeper = _actors.Values.Where(o => o != diglett && o.IsAsleep && !o.Indoors && !o.Busy && o.Altitude == 0 && !IsVisitor(o.Resident.Id) &&
                diglett.Navigator.Connected(new(diglett.X, diglett.Y), new(o.X, o.Y)))
            .OrderBy(o => Distance(o.X, o.Y, diglett.X, diglett.Y)).FirstOrDefault();
        if (sleeper is null || Beside(diglett, sleeper) is not { } spot) return false;
        string name = diglett.Resident.Name, them = sleeper.Resident.Name;
        sleeper.PartnerId = diglett.Resident.Id; diglett.PartnerId = sleeper.Resident.Id;
        Add(diglett, new Label("Tunneling along underground", "Mischievous"), new GoTo(spot, .8f), new Face(sleeper.Resident.Id),
            new Spark(ParkEffectKind.Dirt), new Spotlight($"{name} popped up under {them}'s nap!", 1, 12_000), new Act(ParkPose.Hop, 900, Emote: ParkEmote.Note),
            new Engage(sleeper.Resident.Id, [new Act(ParkPose.Wake, 900, Emote: ParkEmote.Exclaim), new Label($"Woken by {name} popping up", "Grumpy"),
                new Face(diglett.Resident.Id), new Act(ParkPose.Idle, 1800, Emote: ParkEmote.Anger)]),
            new Act(ParkPose.Idle, 1200, Emote: ParkEmote.Sweat),
            new Journal($"{name} popped up right in the middle of {them}'s nap. {them} has not forgiven it.", sleeper.Resident.Id, ParkSocialEventKind.Conflict),
            new Release());
        return true;
    }

    // ── Gengar ───────────────────────────────────────────────────────────────

    private bool PlanShadow(ParkActor gengar)
    {
        var host = _actors.Values.Where(o => o != gengar && !o.IsAsleep && !o.Busy && !o.Indoors && !o.Resident.WaterBound &&
                !o.Resident.Types.Contains(ParkType.Ghost) && gengar.Navigator.Connected(new(gengar.X, gengar.Y), new(o.X, o.Y)))
            .OrderBy(_ => gengar.Random.Next()).FirstOrDefault();
        if (host is null || Beside(gengar, host) is not { } spot) return false;
        string name = gengar.Resident.Name, them = host.Resident.Name;
        Add(gengar, new Label($"Slipping into {them}'s shadow", "Mischievous"), new GoTo(spot, .8f), new Spark(ParkEffectKind.Ghost),
            new Mount(host.Resident.Id, 0, 1, 0, Hidden: true), new Spotlight($"{name} is hiding in {them}'s shadow", 1, 30_000), new Label($"Hiding in {them}'s shadow", "Mischievous"), new Act(ParkPose.Idle, gengar.Random.Next(20_000, 40_000)),
            new GetOff(new(host.X + 14, host.Y)), new Spark(ParkEffectKind.Scare), new Act(ParkPose.Hop, 1500, Emote: ParkEmote.Note),
            new Journal($"{name} spent a while hiding in {them}'s shadow. {them} kept shivering and could not work out why.", host.Resident.Id, ParkSocialEventKind.Play));
        return true;
    }

    // ── Reactions to others ──────────────────────────────────────────────────

    // Croagunk steps in when someone gushes over someone.
    private void CroagunkWatches(ParkActor lover, ParkActor beloved)
    {
        var croagunk = _actors.Values.Where(c => c.Resident.Species is 453 or 454 && Free(c) && c != lover && c != beloved &&
                Distance(c.X, c.Y, lover.X, lover.Y) < 500 && c.Navigator.Connected(new(c.X, c.Y), new(lover.X, lover.Y)))
            .OrderBy(c => Distance(c.X, c.Y, lover.X, lover.Y)).FirstOrDefault();
        if (croagunk is null || croagunk.Random.Next(3) == 0) return;
        var away = FreePoint(croagunk.Navigator, croagunk.Random, lover, land: true) ?? new ParkPoint(lover.X, lover.Y);
        Interrupt(croagunk);
        Add(croagunk, new Label($"Watching {lover.Resident.Name} make eyes at {beloved.Resident.Name}", "Grumpy"), new Act(ParkPose.Idle, 2500, Emote: ParkEmote.Ellipsis),
            new GoTo(new(lover.X - 14, lover.Y), 1.3f), new Face(lover.Resident.Id), new Act(ParkPose.Attack, 700, Emote: ParkEmote.Exclaim),
            new Jab(lover.Resident.Id, away), new Label($"Dragging {lover.Resident.Name} off by the leg", "Calm"), new GoTo(new(away.X - 12, away.Y), .45f),
            new Journal($"{lover.Resident.Name} was making eyes at {beloved.Resident.Name}. {croagunk.Resident.Name} stepped in with a Poison Jab and dragged it off by the leg.",
                lover.Resident.Id, ParkSocialEventKind.Conflict));
    }

    // Wobbuffet walks into the middle of any gathering, salutes, and walks out again.
    private void WobbuffetJoins(ParkActor lead)
    {
        var wobbuffet = _actors.Values.Where(w => w.Resident.Species is 202 or 360 && Free(w) && w != lead &&
                Distance(w.X, w.Y, lead.X, lead.Y) < 260 && w.Navigator.Connected(new(w.X, w.Y), new(lead.X, lead.Y)))
            .FirstOrDefault();
        if (wobbuffet is null || wobbuffet.Random.Next(2) == 0) return;
        var near = new ParkPoint(lead.X, lead.Y + 16);
        if (!wobbuffet.Navigator.CanStand(near.X, near.Y)) return;
        Interrupt(wobbuffet);
        Add(wobbuffet, new Act(ParkPose.Idle, 5000), new Label("Getting in on the act", "Proud"), new GoTo(near, 1.1f), new Act(ParkPose.Pose, 1600, 0, ParkEmote.Exclaim),
            new Act(ParkPose.Nod, 1200, 0), new Journal($"{wobbuffet.Resident.Name} walked into the middle of it all, saluted, and walked out again."));
        if (FreePoint(wobbuffet.Navigator, wobbuffet.Random, wobbuffet, land: true) is { } away) Add(wobbuffet, new GoTo(away));
    }

    // A dog hears a newcomer and goes to bark at it until it is sure it is friendly.
    private void DogsBark(ParkActor visitor)
    {
        if (CanFly(visitor.Resident) || visitor.Resident.WaterBound) return;
        var dog = _actors.Values.Where(d => Dogs.Contains(d.Resident.Species) && Free(d) && !IsVisitor(d.Resident.Id) &&
                Distance(d.X, d.Y, visitor.X, visitor.Y) < 320 && d.Navigator.Connected(new(d.X, d.Y), new(visitor.X, visitor.Y)))
            .OrderBy(d => Distance(d.X, d.Y, visitor.X, visitor.Y)).FirstOrDefault();
        if (dog is null || Beside(dog, visitor) is not { } spot) return;
        Interrupt(dog);
        var friendly = dog.Random.Next(3) > 0;
        Add(dog, new Label($"Barking at the newcomer", "Determined"), new GoTo(spot, 1.6f), new Face(visitor.Resident.Id),
            new Act(ParkPose.Hop, 800, Emote: ParkEmote.Exclaim), new Act(ParkPose.Hop, 800, Emote: ParkEmote.Exclaim), new Act(ParkPose.Idle, 1200),
            friendly ? new Act(ParkPose.Nod, 1400, Emote: ParkEmote.Heart) : new Act(ParkPose.Idle, 1800, Emote: ParkEmote.Anger));
        if (dog.Random.Next(2) == 0)
            Add(dog, new Journal(friendly
                ? $"{dog.Resident.Name} barked at the wild {visitor.Resident.Name} until it was sure it was friendly. Then it sniffed it all over."
                : $"{dog.Resident.Name} barked at the wild {visitor.Resident.Name} and would not stop. It is keeping an eye on it.", visitor.Resident.Id, ParkSocialEventKind.Rest));
    }

    // Mimikyu watches Pikachu get patted, and droops.
    private void MimikyuSees(ParkActor pikachu)
    {
        var mimikyu = _actors.Values.FirstOrDefault(m => m.Resident.Species == 778 && Free(m) && Distance(m.X, m.Y, pikachu.X, pikachu.Y) < 300);
        if (mimikyu is null) return;
        Interrupt(mimikyu);
        Add(mimikyu, new Face(pikachu.Resident.Id), new Label($"Watching {pikachu.Resident.Name} get all the attention", "Lonely"),
            new Act(ParkPose.Idle, 1800, Emote: ParkEmote.Anger), new Act(ParkPose.Cringe, 2200, Emote: ParkEmote.Ellipsis));
        if (mimikyu.Random.Next(2) == 0)
            Add(mimikyu, new Journal($"{mimikyu.Resident.Name} watched you pat {pikachu.Resident.Name} and drooped. It would like a pat too, please.", pikachu.Resident.Id));
    }
}
