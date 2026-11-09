namespace PKForge.Domain;

/// <summary>
/// Snorlax. By day it now and then falls asleep right across a narrow way (the middle of the
/// bridge, the shrine's stairs) and nobody on foot can get past: they stop, puzzled, a queue
/// builds up, a bold one pushes with all its might (nothing happens) and gives up, until a singer
/// comes and sings it awake, someone calls it, or it wakes on its own and ambles off. Flyers just
/// fly over. At night it sleeps out under the stars (it does not fit in the cabin), and the little
/// ones come to sleep on its belly and curled up against it; if it gets up, whoever was on top
/// tumbles off.
/// </summary>
public sealed partial class ParkLife
{
    private const int Snorlax = 143;

    /// <summary>Lying across a narrow way, or not any more.</summary>
    internal sealed record LieAcross(bool On) : Step;
    /// <summary>Up on top of the Pokémon it sleeps on (lifted this high), or back down.</summary>
    internal sealed record Perch(string? OnId, float Height) : Step;
    /// <summary>A singer's song reaches the sleeper blocking the way: it wakes and moves off.</summary>
    internal sealed record WakeBlocker(string BlockerId) : Step;
    /// <summary>The little ones on and around Snorlax tonight make a line in the journal, once.</summary>
    internal sealed record BellyTold(string SnorlaxId) : Step;

    // Close enough to a sleeper across the way to be stuck behind it (art pixels).
    private const float StuckDistance = 24;
    private const long WaitLimit = 40_000, PushAfter = 12_000;

    private readonly Dictionary<string, DateOnly> _bellyTold = new();

    private static string PassName(ParkSpot spot) => spot.Y > 600 ? "the middle of the bridge" : "the shrine's stairs";

    private bool PlanRoadblock(ParkActor snorlax)
    {
        if (Phase == ParkDayPhase.Night || Now < snorlax.NextMischief) return false;
        var pass = _map.Spots.Where(s => s.Kind == ParkSpotKind.Pass && snorlax.Navigator.CanStand(s.X, s.Y) &&
                snorlax.Navigator.Connected(new(snorlax.X, snorlax.Y), new(s.X, s.Y)) && !Crowded(snorlax, s.X, s.Y, PersonalSpace))
            .OrderBy(s => Distance(s.X, s.Y, snorlax.X, snorlax.Y)).FirstOrDefault();
        if (pass is null) return false;
        var random = snorlax.Random;
        snorlax.NextMischief = Now + random.Next(20, 45) * 60_000L;
        var where = PassName(pass);
        Add(snorlax, new Label("Looking for a comfy spot", "Sleepy"), new GoTo(new(pass.X, pass.Y)),
            new Label($"Fast asleep in {where}", "Sleepy"), new Act(ParkPose.Lie, 1500, pass.Facing, ParkEmote.Ellipsis), new LieAcross(true), new Tally($"bridge:{snorlax.Resident.Id}"),
            new Spotlight($"{snorlax.Resident.Name} fell asleep across {(pass.Y > 600 ? "the bridge" : "the shrine's stairs")}! Talk to it to play the Poké Flute", 3, 150_000, "The Sleeping Giant"),
            new Act(ParkPose.Sleep, random.Next(150_000, 300_000), pass.Facing, ParkEmote.Sleep), new LieAcross(false),
            new Act(ParkPose.Wake, 1400), new Act(ParkPose.DeepBreath, 1800), new Label("Ambling off, still yawning", "Sleepy"));
        if (FreePoint(snorlax.Navigator, random, snorlax, land: true) is { } away) Add(snorlax, new GoTo(away, .7f));
        return true;
    }

    private bool RunSnorlaxStep(ParkActor actor, Step step)
    {
        switch (step)
        {
            case LieAcross block:
                if (!block.On && actor.Blocking) _highlights.RemoveAll(h => h.ActorId == actor.Resident.Id);
                actor.Blocking = block.On;
                actor.QueueTold = actor.SongSent = false;
                actor.PushedBy = null;
                return true;
            case Perch perch:
                actor.PerchedOn = perch.OnId;
                if (!actor.InFlight) actor.Altitude = perch.Height;
                return true;
            case WakeBlocker wake:
                if (_actors.TryGetValue(wake.BlockerId, out var sleeper) && sleeper.Blocking)
                {
                    Interrupt(sleeper);
                    Add(sleeper, new Label("Woken by a song", "Surprised"), new Act(ParkPose.Wake, 1400, Emote: ParkEmote.Exclaim),
                        new Act(ParkPose.DeepBreath, 1800), new Label("Ambling off, still yawning", "Sleepy"));
                    if (FreePoint(sleeper.Navigator, sleeper.Random, sleeper, land: true) is { } away) Add(sleeper, new GoTo(away, .7f));
                }
                return true;
            case BellyTold told:
                if (_actors.TryGetValue(told.SnorlaxId, out var bed) && _bellyTold.GetValueOrDefault(told.SnorlaxId) != Today)
                {
                    // Everyone lying on or against it, the one settling down now included.
                    var sleepers = _actors.Values.Where(a => a != bed && Distance(a.X, a.Y, bed.X, bed.Y) < 22 &&
                        (a.IsAsleep || a.Current is BellyTold || a.PerchedOn == told.SnorlaxId)).ToArray();
                    if (sleepers.Length >= 2)
                    {
                        _bellyTold[told.SnorlaxId] = Today;
                        Highlight(bed, $"{Names(sleepers)} {(sleepers.Length == 1 ? "is" : "are")} asleep on {bed.Resident.Name}!", 2, 120_000);
                        _moments.Add(new ParkMoment(ParkSocialEventKind.Bond, sleepers[0].Resident.Id, told.SnorlaxId,
                            $"{Names(sleepers)} fell asleep on and around {bed.Resident.Name}. {bed.Resident.Name} did not notice."));
                    }
                }
                return true;
        }
        return false;
    }

    /// <summary>A walker stuck behind a sleeper lying across the way: it waits, puzzled; a bold one
    /// pushes; after a while it gives up and goes to do something else.</summary>
    /// <returns>True while it is held up (it does not move this tick).</returns>
    private bool HeldUp(ParkActor actor, GoTo go, long now)
    {
        if (!_anyBlocking && actor.WaitingSince == 0) return false;
        // Swimming under the bridge, it is not in the way.
        if (InWater(actor)) { actor.WaitingSince = 0; return false; }
        if (actor.Route is not { } route || actor.RouteLeg >= route.Count || actor.Blocking) return false;
        var next = route[actor.RouteLeg];
        var blocker = _actors.Values.FirstOrDefault(b => b != actor && b.Blocking && b.IsAsleep &&
            Distance(actor.X, actor.Y, b.X, b.Y) < StuckDistance && Distance(next.X, next.Y, b.X, b.Y) < Distance(actor.X, actor.Y, b.X, b.Y) - .05f);
        if (blocker is null)
        {
            actor.WaitingSince = 0;
            return false;
        }
        var them = blocker.Resident.Name;
        actor.SetPose(ParkPose.Idle, now);
        actor.Facing = ParkMotion.Facing(blocker.X - actor.X, blocker.Y - actor.Y);
        if (actor.WaitingSince == 0)
        {
            actor.WaitingSince = now;
            actor.Say(ParkEmote.Question, now);
            if (actor.Activity != $"Waiting for {them} to move") { actor.Caption = $"Waiting for {them} to move"; actor.CaptionSince = Now; }
            actor.Activity = $"Waiting for {them} to move"; actor.ActivityMood = "Confused";
            return true;
        }
        var waited = now - actor.WaitingSince;
        if (waited % 7000 < TickMilliseconds) actor.Say(ParkEmote.Ellipsis, now);
        // A queue: worth a line in the journal, once.
        if (!blocker.QueueTold && _actors.Values.Count(a => a.WaitingSince > 0 && Distance(a.X, a.Y, blocker.X, blocker.Y) < StuckDistance + 20) >= 2)
        {
            blocker.QueueTold = true;
            _moments.Add(new ParkMoment(ParkSocialEventKind.Rest, blocker.Resident.Id, "",
                $"{them} fell asleep right across {(blocker.Y > 600 ? "the bridge" : "the shrine's stairs")}. A queue formed behind it."));
        }
        // The bold try to shift it. It does not shift.
        if (waited > PushAfter && blocker.PushedBy is null &&
            (actor.Resident.Personality is ParkPersonality.Bold or ParkPersonality.Energetic || actor.Resident.Temperament is ParkTemperament.Feisty or ParkTemperament.Fierce))
        {
            blocker.PushedBy = actor.Resident.Id;
            actor.WaitingSince = 0;
            ClearPlan(actor);
            Add(actor, new Label($"Pushing {them} with all its might", "Determined"), new Face(blocker.Resident.Id),
                new Act(ParkPose.Pull, 2500, Emote: ParkEmote.Sweat), new Spark(ParkEffectKind.Dirt), new Act(ParkPose.Pull, 1500, Emote: ParkEmote.Sweat),
                new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Ellipsis), new Label($"Gave up on moving {them}", "Grumpy"),
                new Journal($"{actor.Resident.Name} pushed {them} with all its might. {them} did not move, or wake up.", blocker.Resident.Id, ParkSocialEventKind.Rest));
            return true;
        }
        if (waited > WaitLimit)
        {
            actor.WaitingSince = 0;
            ClearPlan(actor);
            Add(actor, new Label($"Gave up waiting for {them}", "Grumpy"), new Act(ParkPose.Idle, 1200, Emote: ParkEmote.Anger));
            return true;
        }
        return true;
    }

    // A singer nearby comes and sings the sleeper awake once a queue has built up behind it.
    // Whether anyone lies across a way right now (checked once a tick, so walkers skip the search).
    private bool _anyBlocking;

    private void UpdateRoadblocks(long now)
    {
        _anyBlocking = false;
        foreach (var a in _actors.Values) if (a.Blocking) { _anyBlocking = true; break; }
        if (now % 1000 >= TickMilliseconds) return;
        foreach (var blocker in _actors.Values.Where(b => b.Blocking && b.IsAsleep && !b.SongSent))
        {
            if (_actors.Values.Count(a => a.WaitingSince > 0 && Distance(a.X, a.Y, blocker.X, blocker.Y) < StuckDistance + 20) < 2) continue;
            var singer = _actors.Values.Where(s => s != blocker && (s.Resident.Has(ParkQuirk.Singer) || s.Resident.Species is 131 or 648) && Free(s) &&
                    Distance(s.X, s.Y, blocker.X, blocker.Y) < 400)
                .OrderBy(s => Distance(s.X, s.Y, blocker.X, blocker.Y)).FirstOrDefault();
            if (singer is null || Beside(singer, blocker) is not { } spot) continue;
            blocker.SongSent = true;
            Interrupt(singer);
            Add(singer, new Label($"Going to wake {blocker.Resident.Name} with a song", "Determined"), new GoTo(spot, 1.2f), new Face(blocker.Resident.Id),
                new Label($"Singing {blocker.Resident.Name} awake", "Joyful"), new Act(ParkPose.Sing, 3500, Emote: ParkEmote.Note, Prop: ParkProp.Notes),
                new WakeBlocker(blocker.Resident.Id), new Act(ParkPose.Hop, 1200, Emote: ParkEmote.Sparkle),
                new Journal($"{singer.Resident.Name} sang until {blocker.Resident.Name} woke up and rolled out of the way. Everyone stuck behind it cheered.",
                    blocker.Resident.Id, ParkSocialEventKind.Help));
        }
        // Whoever slept on top of a Snorlax that got up tumbles off.
        foreach (var rider in _actors.Values.Where(a => a.PerchedOn is not null).ToArray())
        {
            if (_actors.TryGetValue(rider.PerchedOn!, out var bed) && bed.IsAsleep && Distance(bed.X, bed.Y, rider.X, rider.Y) < 6) continue;
            Interrupt(rider);
            Add(rider, new Label("Tumbled off", "Surprised"), new Act(ParkPose.Tumble, 1200, Emote: ParkEmote.Exclaim), new Act(ParkPose.Idle, 1000, Emote: ParkEmote.Sweat));
        }
    }

    // Babies and first stages: the ones small enough to sleep on Snorlax.
    private static bool Little(ParkLifeResident resident) =>
        !resident.WaterBound && resident.Species != Snorlax && ParkSpecies.EvolvesFrom(resident.Species) == 0 && ParkSpecies.Evolves(resident.Species);

    /// <summary>Night: a little one goes to sleep on Snorlax's belly, or curled up against it.</summary>
    private bool PlanBellyBed(ParkActor actor)
    {
        if (!Little(actor.Resident)) return false;
        var snorlaxes = _actors.Values.Where(b => b.Resident.Species == Snorlax && !b.Indoors && !b.Blocking &&
            Distance(b.X, b.Y, actor.X, actor.Y) < 500 && actor.Navigator.Connected(new(actor.X, actor.Y), new(b.X, b.Y))).ToArray();
        var bed = snorlaxes.Where(b => b.IsAsleep).OrderBy(b => Distance(b.X, b.Y, actor.X, actor.Y)).FirstOrDefault();
        if (bed is null)
        {
            // Snorlax is still on its way to bed: the little one waits up a little for it (or not).
            if (snorlaxes.Length == 0 || actor.Random.Next(3) == 0) return false;
            Add(actor, new Label($"Waiting for {snorlaxes[0].Resident.Name} to settle down", "Sleepy"),
                new Act(ParkPose.Sit, actor.Random.Next(6000, 12_000), Emote: ParkEmote.Sleep));
            return true;
        }
        // On the belly first, then round it.
        // Snorlax asleep stands 16 art pixels tall over its feet, its belly 4 to 12 above them: the
        // one on top lies mid-belly, drawn just in front of it.
        var places = new (float X, float Y, float Lift)[] { (5, 2, 4), (-17, 3, 0), (17, 3, 0), (-9, 11, 0), (9, 11, 0) };
        var name = bed.Resident.Name;
        foreach (var (dx, dy, lift) in places)
        {
            var at = new ParkPoint(bed.X + dx, bed.Y + dy);
            if (!actor.Navigator.CanStand(at.X, at.Y) || !actor.Navigator.Connected(new(actor.X, actor.Y), at) ||
                _actors.Values.Any(o => o != actor && o != bed && (Distance(o.X, o.Y, at.X, at.Y) < 7 || o.Destination is { } d && Distance(d.X, d.Y, at.X, at.Y) < 7)))
                continue;
            var onTop = lift > 0;
            Add(actor, new Label(onTop ? $"Climbing onto {name}'s belly" : $"Snuggling up to {name}", "Sleepy"), new GoTo(at, .9f),
                new Face(bed.Resident.Id));
            if (onTop) Add(actor, new Perch(bed.Resident.Id, lift));
            Add(actor, new Act(ParkPose.Lie, 1200), new Label(onTop ? $"Fast asleep on {name}'s belly" : $"Fast asleep against {name}", "Sleepy"),
                new BellyTold(bed.Resident.Id), new Act(ParkPose.Sleep, actor.Random.Next(90_000, 200_000), Emote: ParkEmote.Sleep), new Perch(null, 0),
                new Act(ParkPose.Wake, 1200));
            return true;
        }
        return false;
    }
}
