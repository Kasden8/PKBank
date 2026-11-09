namespace PKForge.Domain;

/// <summary>
/// Scenes: several residents acting something out together, each walking (or flying) to its
/// place, waiting there for the others, then moving in time. Three Magnemite click into a
/// Magneton for a moment and three Diglett bob in a row like a Dugtrio (neither really evolves);
/// the Eevee family has a picnic; the brawlers spar in a ring with a crowd around them, and the
/// Hitmon trio trains; the cool ones strike dramatic poses on the heights (and sometimes trip,
/// or stare each other down); the loners find the quietest corner and let one friend sit with
/// them.
/// </summary>
public sealed partial class ParkLife
{
    /// <summary>A group acting a scene. It holds while everyone in it is still there; if one is
    /// called away or the others take too long to come, the rest give up and go about their day.</summary>
    internal sealed class Troupe(string[] ids, int least, long deadline)
    {
        public string[] Ids { get; } = ids;
        public int Least { get; } = least;
        public long Deadline { get; set; } = deadline;
        public HashSet<string> Ready { get; } = new();
        public bool Off { get; set; }
    }

    /// <summary>Wait in place until the whole troupe is in its places.</summary>
    internal sealed record Muster(Troupe Troupe) : Step;
    /// <summary>The scene is over for this one.</summary>
    internal sealed record Disband : Step;

    private static readonly HashSet<int> Eeveelutions = [133, 134, 135, 136, 196, 197, 470, 471, 700];
    private static readonly HashSet<int> HitmonTrio = [106, 107, 237];

    // Fighters who love a sparring match (Primeape's line, Machop's, the Hitmons, the Kalos and
    // Alola heavyweights…).
    private static readonly HashSet<int> Brawlers =
    [
        56, 57, 979, 62, 66, 67, 68, 106, 107, 236, 237, 248, 286, 296, 297, 448, 453, 454, 532, 533, 534, 538, 539,
        619, 620, 674, 675, 701, 727, 739, 740, 759, 760, 766, 852, 853, 891, 892, 983
    ];

    // The cool ones, who like to stand somewhere high with the wind in their face.
    private static readonly HashSet<int> AuraFarmers =
    [
        6, 38, 130, 149, 150, 197, 229, 243, 244, 245, 248, 249, 250, 254, 257, 282, 359, 373, 380, 381, 384, 445, 448, 461,
        475, 491, 571, 635, 658, 724, 745, 812, 815, 818, 887, 888, 889, 908, 911, 914, 1006
    ];

    private enum SceneKind { Magneton, Dugtrio, EeveePicnic, Sparring, HitmonTraining, AuraFarming, EeveeShowcase, EeveePile }

    // A scene is an event, not a habit: once one has played, the same kind waits a while
    // (minutes of park time, at random within the range) before it can play again.
    private static (int Min, int Max) Pause(SceneKind kind) => kind switch
    {
        SceneKind.Magneton or SceneKind.Dugtrio => (25, 50),
        SceneKind.EeveePicnic => (25, 45),
        SceneKind.EeveeShowcase => (25, 45),
        SceneKind.EeveePile => (90, 150),
        SceneKind.Sparring => (20, 40),
        SceneKind.HitmonTraining => (25, 45),
        _ => (12, 25)
    };

    private readonly Dictionary<SceneKind, long> _sceneNext = new();

    private IEnumerable<SceneKind> ScenesFor(ParkLifeResident resident)
    {
        var species = resident.Species;
        if (species == 81) yield return SceneKind.Magneton;
        if (species == 50) yield return SceneKind.Dugtrio;
        if (Eeveelutions.Contains(species))
        {
            // By night the family only curls up together; by day it picnics and shows off.
            if (Phase == ParkDayPhase.Night) { yield return SceneKind.EeveePile; yield break; }
            yield return SceneKind.EeveePicnic;
            yield return SceneKind.EeveeShowcase;
        }
        if (HitmonTrio.Contains(species)) yield return SceneKind.HitmonTraining;
        if (Brawlers.Contains(species)) yield return SceneKind.Sparring;
        if (AuraFarmers.Contains(species)) yield return SceneKind.AuraFarming;
    }

    private void SceneWeights(ParkActor actor, Dictionary<Activity, float> weights)
    {
        if (Phase == ParkDayPhase.Night && !Eeveelutions.Contains(actor.Resident.Species) ||
            actor.Resident.WaterBound || !ScenesFor(actor.Resident).Any(k => Now >= _sceneNext.GetValueOrDefault(k))) return;
        weights[Activity.Scene] = Wet ? 3 : 12;
        if (actor.LastActivity == Activity.Scene) weights[Activity.Scene] *= .2f;
        // At bedtime an Eevee with family still awake nearby goes to curl up with it rather than alone.
        if (Phase == ParkDayPhase.Night && _actors.Values.Any(o => o != actor && Eeveelutions.Contains(o.Resident.Species) && Free(o) &&
                Distance(actor.X, actor.Y, o.X, o.Y) < 600))
            weights[Activity.Scene] = Math.Max(weights[Activity.Scene], weights.GetValueOrDefault(Activity.Bedtime) * 1.5f);
    }

    private bool PlanScene(ParkActor actor, long now)
    {
        foreach (var kind in ScenesFor(actor.Resident).Where(k => now >= _sceneNext.GetValueOrDefault(k)).OrderBy(_ => actor.Random.Next()))
        {
            var planned = kind switch
            {
                SceneKind.Magneton or SceneKind.Dugtrio => PlanFormation(actor, kind, now),
                SceneKind.EeveePicnic => !Wet && PlanPicnic(actor, now),
                SceneKind.EeveeShowcase => !Wet && PlanShowcase(actor, now),
                SceneKind.EeveePile => PlanPile(actor, now),
                SceneKind.HitmonTraining => PlanTraining(actor, now),
                SceneKind.Sparring => !Wet && PlanSparring(actor, now),
                _ => !Wet && PlanAuraFarming(actor, now)
            };
            if (!planned) continue;
            var (min, max) = Pause(kind);
            _sceneNext[kind] = now + actor.Random.Next(min, max) * 60_000L;
            return true;
        }
        return false;
    }

    // Waiting for the troupe: false while it waits. Called straight from Run, because a step that
    // is still going must not fall through to the other handlers (the last one ends any step).
    private bool RunMuster(ParkActor actor, Muster muster, long now)
    {
        var troupe = muster.Troupe;
        if (troupe.Off || actor.Troupe != troupe) { GiveUp(actor); return true; }
        troupe.Ready.Add(actor.Resident.Id);
        if (actor.Pose != ParkPose.Idle) actor.SetPose(ParkPose.Idle, now);
        var still = troupe.Ids.Where(id => _actors.TryGetValue(id, out var member) && member.Troupe == troupe).ToArray();
        if (still.Length < troupe.Least || now > troupe.Deadline && !still.All(troupe.Ready.Contains))
        {
            troupe.Off = true;
            GiveUp(actor);
            return true;
        }
        return still.All(troupe.Ready.Contains);
    }

    private bool RunSceneStep(ParkActor actor, Step step, long now)
    {
        switch (step)
        {
            case Disband:
                actor.Troupe = null;
                return true;
        }
        return false;
    }

    // Called away, or the others never came: back to its own day.
    private void GiveUp(ParkActor actor)
    {
        ClearPlan(actor);
        actor.Plan.Enqueue(new Label("Wandering off", "Content"));
        actor.Plan.Enqueue(new Act(ParkPose.Idle, 800, Emote: ParkEmote.Ellipsis));
    }

    // Free to join a scene: awake, outside, on the ground, not already doing something with someone.
    private bool Free(ParkActor other) => !other.Busy && !other.IsAsleep && !other.Indoors && !other.Hidden && !other.Resident.WaterBound &&
        other.Flight is null && other.Current is not Fly;

    private const float SceneReach = 360;

    // Others close enough who fit, nearest first.
    private ParkActor[] Recruit(ParkActor lead, Func<ParkActor, bool> fits, int most, float reach = SceneReach) => _actors.Values
        .Where(other => other != lead && fits(other) && Free(other) && Distance(lead.X, lead.Y, other.X, other.Y) < reach &&
            (CanFly(other.Resident) || other.Navigator.Connected(new(other.X, other.Y), new(lead.X, lead.Y))))
        .OrderBy(other => Distance(lead.X, lead.Y, other.X, other.Y)).Take(most).ToArray();

    // A middle near the lead where every member has a place it can stand on and reach.
    private ParkPoint? Stage(ParkActor lead, IReadOnlyList<ParkActor> cast, Func<ParkPoint, int, ParkPoint> place)
    {
        bool Fits(ParkPoint middle) => cast.Select((member, i) => (member, at: place(middle, i))).All(p =>
            p.member.Navigator.CanStand(p.at.X, p.at.Y) && !Crowded(null, p.at.X, p.at.Y, 6) &&
            (CanFly(p.member.Resident) || p.member.Navigator.Connected(new(p.member.X, p.member.Y), p.at)) &&
            _actors.Values.Where(o => !cast.Contains(o)).All(o => Distance(o.X, o.Y, p.at.X, p.at.Y) > 10));
        var here = new ParkPoint(lead.X, lead.Y);
        if (Fits(here)) return here;
        for (var attempt = 0; attempt < 10; attempt++)
            if (FreePoint(lead.Navigator, lead.Random, lead, land: true) is { } middle && Fits(middle)) return middle;
        return null;
    }

    // Each member: off to its place, wait for the others, then its part, then free again.
    private void Cast(ParkActor lead, IReadOnlyList<ParkActor> cast, int least, long now, Func<ParkActor, int, Troupe, Step[]> part)
    {
        var troupe = new Troupe([.. cast.Select(a => a.Resident.Id)], least, now);
        var walk = 0f;
        for (var i = 0; i < cast.Count; i++)
        {
            var member = cast[i];
            if (member != lead) Interrupt(member);
            member.Troupe = troupe;
            var steps = part(member, i, troupe);
            // Long enough for each to get to its own place (the first walk in its part), and some.
            if (steps.OfType<GoTo>().FirstOrDefault() is { } first)
                walk = Math.Max(walk, Distance(member.X, member.Y, first.Target.X, first.Target.Y) / (member.Speed * first.SpeedFactor));
            foreach (var step in steps) member.Plan.Enqueue(step);
            member.Plan.Enqueue(new Disband());
        }
        troupe.Deadline = now + 30_000 + (long)(walk * 2200);
        WobbuffetJoins(lead);
    }

    private static int Toward(ParkPoint from, ParkPoint to) => ParkMotion.Facing(to.X - from.X, to.Y - from.Y);

    private static string Names(IEnumerable<ParkActor> actors)
    {
        var names = actors.Select(a => a.Resident.Name).ToArray();
        return names.Length switch { 0 => "", 1 => names[0], _ => string.Join(", ", names[..^1]) + " and " + names[^1] };
    }

    // ── Magneton and Dugtrio ─────────────────────────────────────────────────

    private bool PlanFormation(ParkActor lead, SceneKind kind, long now)
    {
        var species = lead.Resident.Species;
        var others = Recruit(lead, o => o.Resident.Species == species, 2);
        if (others.Length < 2) return false;
        ParkActor[] cast = [lead, .. others];
        var magneton = kind == SceneKind.Magneton;
        // Magnemite in a triangle, magnets in; Diglett side by side in a row.
        ParkPoint Place(ParkPoint middle, int i) => magneton
            ? i switch { 0 => new(middle.X, middle.Y - 8), 1 => new(middle.X - 9, middle.Y + 5), _ => new(middle.X + 9, middle.Y + 5) }
            : new(middle.X + (i - 1) * 12, middle.Y);
        if (Stage(lead, cast, Place) is not { } stage) return false;
        Cast(lead, cast, 3, now, (member, i, troupe) =>
        {
            var spot = Place(stage, i);
            var away = new ParkPoint(spot.X + (spot.X - stage.X) * 2.5f, spot.Y + (spot.Y - stage.Y) * 2.5f + (magneton ? 0 : 10));
            var steps = new List<Step>
            {
                new Label(magneton ? "Drifting toward the other Magnemite" : "Tunneling toward the other Diglett", "Curious"),
                new GoTo(spot), new Muster(troupe), new Act(ParkPose.Idle, 500, 0)
            };
            if (member == lead)
                steps.Add(new Spotlight(magneton ? "Three Magnemite are forming a Magneton!" : "Three Diglett are pretending to be a Dugtrio!", 3, 15_000, magneton ? "Magneton Assembles" : "Dugtrio?"));
            if (magneton)
            {
                steps.AddRange([
                    new Label("Clicked into a Magneton (for now)", "Excited"), new Act(ParkPose.Charge, 2200, 0),
                    .. member == lead ? new Step[] { new Spark(ParkEffectKind.Zap) } : [],
                    new Act(ParkPose.Idle, 4500, 0, i == 0 ? ParkEmote.Sparkle : ParkEmote.None),
                    .. member == lead ? new Step[] { new Spark(ParkEffectKind.Zap) } : [],
                    new Act(ParkPose.Hurt, 700, 0, ParkEmote.Exclaim), new Label("Came apart again", "Surprised")]);
            }
            else
            {
                steps.AddRange([
                    new Label("Pretending to be a Dugtrio", "Joyful"), new Spark(ParkEffectKind.Dirt),
                    new Act(ParkPose.Hop, 700, 0), new Act(ParkPose.Idle, 300, 0), new Act(ParkPose.Hop, 700, 0), new Act(ParkPose.Idle, 300, 0),
                    new Act(ParkPose.Hop, 700, 0), new Act(ParkPose.Idle, 2400, 0, i == 1 ? ParkEmote.Note : ParkEmote.None), new Spark(ParkEffectKind.Dirt)]);
            }
            if (member.Navigator.CanStand(away.X, away.Y)) steps.Add(new GoTo(away, 1.2f));
            steps.Add(new Gain(ParkNeed.Social, 14));
            steps.Add(new Gain(ParkNeed.Fun, 10));
            if (member == lead)
                steps.Add(new Journal(magneton
                    ? $"{Names(cast)} drifted together and clicked into a Magneton for a moment. Then they came apart again, a little surprised."
                    : $"{Names(cast)} popped up side by side and bobbed in time, like a Dugtrio. They left three neat little furrows behind.",
                    others[0].Resident.Id, ParkSocialEventKind.Play));
            return [.. steps];
        });
        return true;
    }

    // ── The Eevee family's picnic ────────────────────────────────────────────

    private bool PlanPicnic(ParkActor lead, long now)
    {
        var others = Recruit(lead, o => Eeveelutions.Contains(o.Resident.Species), 4, 700);
        if (others.Length == 0) return false;
        ParkActor[] cast = [lead, .. others];
        var count = cast.Length;
        ParkPoint Place(ParkPoint middle, int i)
        {
            // Two face each other side by side; more sit in a ring, the first nearest the camera.
            var angle = MathF.PI * 2 * i / count + (count == 2 ? 0 : MathF.PI / 2);
            return new(middle.X + MathF.Cos(angle) * (14 + 5 * count), middle.Y + MathF.Sin(angle) * (8 + 3 * count));
        }
        if (Stage(lead, cast, Place) is not { } stage) return false;
        // Eevee, if it came, cannot sit still: it runs from one to the next before it sits.
        var eevee = cast.FirstOrDefault(a => a.Resident.Species == 133);
        var visits = eevee is null ? 0 : count - 1;
        Cast(lead, cast, 2, now, (member, i, troupe) =>
        {
            var spot = Place(stage, i);
            var face = Toward(spot, stage);
            var steps = new List<Step>
            {
                new Label("Off to the family picnic", "Happy"), new GoTo(spot), new Muster(troupe),
                new Label("Having a picnic with the family", "Happy")
            };
            if (member == lead) steps.Add(new Spotlight("The Eevee family is having a picnic!", 2, 30_000));
            if (member == eevee)
            {
                for (var k = 1; k < count; k++)
                {
                    var friend = Place(stage, (i + k) % count);
                    var beside = new ParkPoint((friend.X + stage.X) / 2, (friend.Y + stage.Y) / 2);
                    steps.Add(new GoTo(beside, 1.4f));
                    steps.Add(new Act(ParkPose.Hop, 900, Toward(beside, friend), ParkEmote.Heart));
                }
                steps.Add(new GoTo(spot, 1.4f));
            }
            else steps.Add(new Act(ParkPose.Sit, 2400 * visits + 600, face, member.Random.Next(3) == 0 ? ParkEmote.Heart : ParkEmote.None));
            steps.AddRange([
                new Act(ParkPose.Eat, 5000, face, Prop: ParkProp.Berry), new Act(ParkPose.Sit, 3000, face, member.Random.Next(2) == 0 ? ParkEmote.Note : ParkEmote.None),
                new Act(ParkPose.Nod, 1400, face, ParkEmote.Heart), new Gain(ParkNeed.Social, 18), new Gain(ParkNeed.Hunger, 15)]);
            if (member == lead)
                steps.Add(new Journal(eevee is not null
                    ? $"{Names(cast)} had a picnic together. {eevee.Resident.Name} ran from one to the next the whole time, then ate the most."
                    : $"{Names(cast)} {(count == 2 ? "sat side by side" : "sat in a circle")} and shared berries. Eevee's family always finds its way back together.",
                    others[0].Resident.Id, ParkSocialEventKind.Bond));
            return [.. steps];
        });
        return true;
    }

    // ── The Eevee family shows off ───────────────────────────────────────────

    // What each one shows: Vaporeon water, Jolteon sparks, Flareon fire, Espeon its mind, Umbreon
    // its rings, Leafeon leaves, Glaceon frost, Sylveon its ribbons.
    private static ParkEffectKind ElementOf(int species) => species switch
    {
        134 => ParkEffectKind.Splash, 135 => ParkEffectKind.Zap, 136 => ParkEffectKind.Burn, 196 => ParkEffectKind.Psychic,
        197 => ParkEffectKind.Shine, 470 => ParkEffectKind.Pollen, 471 => ParkEffectKind.Frost, 700 => ParkEffectKind.Hearts,
        _ => ParkEffectKind.Sparkle
    };

    // The evolved ones step forward in turn and show their element, Eevee in the middle trying to
    // decide what it will be one day. Without Eevee, they show off to each other.
    private bool PlanShowcase(ParkActor lead, long now)
    {
        var others = Recruit(lead, o => Eeveelutions.Contains(o.Resident.Species), 4, 700);
        ParkActor[] cast = [lead, .. others];
        var eevee = cast.FirstOrDefault(a => a.Resident.Species == 133);
        var shows = cast.Where(a => a.Resident.Species != 133).ToArray();
        if (shows.Length < (eevee is null ? 2 : 1)) return false;
        // Eevee in the middle, the others in a half circle in front of it.
        var ring = shows.Length;
        var order = eevee is null ? cast : [eevee, .. shows];
        ParkPoint Place(ParkPoint middle, int i)
        {
            if (eevee is not null && i == 0) return new(middle.X, middle.Y + 14);
            var k = eevee is null ? i : i - 1;
            var angle = MathF.PI * (ring == 1 ? .5f : .15f + .7f * k / (ring - 1));
            return new(middle.X - MathF.Cos(angle) * 40, middle.Y - MathF.Sin(angle) * 18 - 6);
        }
        if (Stage(lead, order, Place) is not { } stage) return false;
        var places = order.Select((a, i) => (a, Place(stage, i))).ToDictionary(p => p.a, p => p.Item2);
        var middle = eevee is null ? stage : places[eevee];
        var script = new Script(order, a => a == eevee ? 4 : Toward(places[a], middle));
        script.Beat(1200, a => (ParkPose.Idle, a == eevee ? ParkEmote.Exclaim : ParkEmote.None));
        foreach (var show in shows)
        {
            script.Then(show, new Spark(ElementOf(show.Resident.Species)));
            script.Beat(1500, a => a == show ? (ParkPose.Charge, ParkEmote.None) : null);
            script.Beat(1300, a => a == show ? (ParkPose.Pose, ParkEmote.Sparkle) : a == eevee ? (ParkPose.Hop, ParkEmote.Sparkle) : null);
        }
        if (eevee is not null)
        {
            script.Beat(1600, a => a == eevee ? (ParkPose.LookUp, ParkEmote.Question) : (ParkPose.Idle, ParkEmote.None));
            script.Beat(1400, a => a == eevee ? (ParkPose.Hop, ParkEmote.Note) : (ParkPose.Nod, ParkEmote.Heart));
        }
        else script.Beat(1400, _ => (ParkPose.Nod, ParkEmote.Heart));
        foreach (var a in order) script.Then(a, new Gain(ParkNeed.Fun, 12), new Gain(ParkNeed.Social, 10));
        script.Then(lead, new Journal(eevee is not null
            ? $"{Names(shows)} each showed {eevee.Resident.Name} what they can do. {eevee.Resident.Name} loved every one of them, and still cannot decide what it wants to be."
            : $"{Names(shows)} took turns showing off their elements. Nobody won; everybody clapped.",
            others[0].Resident.Id, ParkSocialEventKind.Play));
        Cast(lead, order, 2, now, (member, _, troupe) =>
        [
            new Label(member == eevee ? "Off to watch the family show off" : "Off to show off for the family", "Excited"),
            new GoTo(places[member]), new Muster(troupe),
            .. member == lead ? new Step[] { new Spotlight(eevee is not null ? $"The Eevee family is showing {eevee.Resident.Name} what it could become!" : "The Eevee family is showing off!", 2, 25_000) } : [],
            new Label(member == eevee ? "Watching the family show off" : "Showing off for the family", "Excited"),
            .. script.For(member)
        ]);
        return true;
    }

    // By night the family curls up in one heap, Eevee in the middle.
    private bool PlanPile(ParkActor lead, long now)
    {
        var others = Recruit(lead, o => Eeveelutions.Contains(o.Resident.Species), 4, 600);
        if (others.Length == 0) return false;
        ParkActor[] cast = [lead, .. others];
        var eevee = cast.FirstOrDefault(a => a.Resident.Species == 133);
        var order = eevee is null ? cast : [eevee, .. cast.Where(a => a != eevee)];
        ParkPoint Place(ParkPoint middle, int i)
        {
            if (i == 0) return middle;
            var angle = MathF.PI * 2 * (i - 1) / (order.Length - 1);
            return new(middle.X + MathF.Cos(angle) * 15, middle.Y + MathF.Sin(angle) * 8);
        }
        if (Stage(lead, order, Place) is not { } stage) return false;
        Cast(lead, order, 2, now, (member, i, troupe) =>
        [
            new Label("Off to sleep with the family", "Sleepy"), new GoTo(Place(stage, i)), new Muster(troupe),
            .. member == lead ? new Step[] { new Spotlight("The Eevee family is curling up together for the night", 1, 30_000) } : [],
            new Label("Curled up with the family", "Sleepy"), new Act(ParkPose.Idle, 900, Toward(Place(stage, i), stage), ParkEmote.Heart),
            new Act(ParkPose.Sleep, 60_000 + member.Random.Next(30_000), Toward(Place(stage, i), stage), ParkEmote.Sleep),
            new Gain(ParkNeed.Energy, 25), new Gain(ParkNeed.Social, 12),
            .. member == lead ? new Step[] { new Journal($"{Names(order)} slept in one warm heap last night{(eevee is not null ? $", {eevee.Resident.Name} right in the middle" : "")}.",
                others[0].Resident.Id, ParkSocialEventKind.Bond) } : []
        ]);
        return true;
    }

    // ── Sparring ─────────────────────────────────────────────────────────────

    // Everyone's part, beat by beat, so that a blow and the flinch it causes happen together.
    private sealed class Script(IReadOnlyList<ParkActor> cast, Func<ParkActor, int> facing)
    {
        private readonly Dictionary<ParkActor, List<Step>> _steps = cast.ToDictionary(a => a, _ => new List<Step>());

        public void Beat(int milliseconds, Func<ParkActor, (ParkPose Pose, ParkEmote Emote)?> part)
        {
            foreach (var (actor, steps) in _steps)
                steps.Add(part(actor) is { } act ? new Act(act.Pose, milliseconds, facing(actor), act.Emote) : new Act(ParkPose.Idle, milliseconds, facing(actor)));
        }

        public void Then(ParkActor actor, params Step[] steps) => _steps[actor].AddRange(steps);

        public IEnumerable<Step> For(ParkActor actor) => _steps[actor];
    }

    private bool PlanSparring(ParkActor lead, long now)
    {
        if (Recruit(lead, o => Brawlers.Contains(o.Resident.Species) && Feeling(lead, o) != ParkPairKind.Rivals, 1) is not [var rival]) return false;
        // A crowd from whoever is close: they stand at the corners of the ring.
        var crowd = Recruit(lead, o => o != rival && !Brawlers.Contains(o.Resident.Species) && o.Resident.Temperament != ParkTemperament.Aloof, 4, 240);
        ParkActor[] cast = [lead, rival, .. crowd];
        ParkPoint Place(ParkPoint middle, int i) => i switch
        {
            0 => new(middle.X - 17, middle.Y), 1 => new(middle.X + 17, middle.Y),
            2 => new(middle.X - 46, middle.Y - 18), 3 => new(middle.X + 46, middle.Y - 18),
            4 => new(middle.X - 46, middle.Y + 16), _ => new(middle.X + 46, middle.Y + 16)
        };
        if (Stage(lead, cast, Place) is not { } stage)
        {
            // Too crowded for a ring here: the two of them alone, then.
            cast = [lead, rival];
            crowd = [];
            if (Stage(lead, cast, Place) is not { } open) return false;
            stage = open;
        }
        var places = cast.Select((a, i) => (a, Place(stage, i))).ToDictionary(p => p.a, p => p.Item2);
        int Facing(ParkActor a) => a == lead ? 2 : a == rival ? 6 : Toward(places[a], stage);
        var script = new Script(cast, Facing);
        bool InRing(ParkActor a) => a == lead || a == rival;

        // A heel plays to the crowd first, and the crowd answers: some cheer, some boo.
        var heel = cast.Take(2).FirstOrDefault(a => a.Resident.Species == 727 || a.Resident.Temperament == ParkTemperament.Fierce);
        if (heel is not null && crowd.Length > 0)
        {
            script.Beat(1800, a => a == heel ? (ParkPose.Pose, ParkEmote.Sparkle) : null);
            script.Beat(1200, a => InRing(a) ? null : (ParkPose.Hop, a.Random.Next(2) == 0 ? ParkEmote.Anger : ParkEmote.Note));
        }
        script.Beat(1300, a => InRing(a) ? (ParkPose.Nod, ParkEmote.None) : null);
        script.Beat(1400, a => InRing(a) ? (ParkPose.Charge, ParkEmote.Exclaim) : null);
        // Three exchanges: a blow, a flinch, the crowd reacting.
        var random = lead.Random;
        for (var round = 0; round < 3; round++)
        {
            var striker = random.Next(2) == 0 ? lead : rival;
            var struck = striker == lead ? rival : lead;
            script.Then(striker, new Spark(ParkEffectKind.Gust));
            script.Beat(900, a => a == striker ? (ParkPose.Attack, ParkEmote.None) : a == struck ? (ParkPose.Hurt, ParkEmote.None)
                : (ParkPose.Hop, round == 2 ? ParkEmote.Exclaim : ParkEmote.None));
            script.Beat(500, _ => null);
        }
        // Hawlucha finishes with a leap; anyone else with one last blow.
        var winner = random.Next(2) == 0 ? lead : rival;
        var loser = winner == lead ? rival : lead;
        if (winner.Resident.Species == 701) script.Beat(1200, a => a == winner ? (ParkPose.Hop, ParkEmote.Exclaim) : null);
        script.Then(winner, new Spark(ParkEffectKind.Gust));
        script.Beat(900, a => a == winner ? (ParkPose.Attack, ParkEmote.None) : a == loser ? (ParkPose.Tumble, ParkEmote.Sweat) : null);
        script.Beat(1700, a => a == winner ? (ParkPose.Pose, ParkEmote.Sparkle) : a == loser ? (ParkPose.Idle, ParkEmote.Sweat)
            : (ParkPose.Hop, ParkEmote.Note));
        // Then a bow, as friends.
        script.Beat(1400, a => InRing(a) ? (ParkPose.Nod, ParkEmote.Heart) : null);
        foreach (var a in cast) script.Then(a, new Gain(ParkNeed.Fun, InRing(a) ? 16 : 10), new Gain(ParkNeed.Social, 10));
        script.Then(lead, new Share(ParkSocialEventKind.Play, rival.Resident.Id));
        var place = _map.RegionAt(stage.X, stage.Y);
        var where = IsWhole(place) ? "out in the meadow" : $"in {place.Name}";
        script.Then(lead, new Journal(crowd.Length > 0
            ? $"{lead.Resident.Name} and {rival.Resident.Name} put on a sparring match {where}, with {Names(crowd)} watching. {winner.Resident.Name} won; {loser.Resident.Name} asked for a rematch straight away."
            : $"{lead.Resident.Name} and {rival.Resident.Name} sparred {where}. {winner.Resident.Name} won, and they bowed to each other like old friends.",
            rival.Resident.Id, ParkSocialEventKind.Play));
        Cast(lead, cast, 2, now, (member, _, troupe) =>
        [
            new Label(InRing(member) ? $"Off to spar with {(member == lead ? rival : lead).Resident.Name}" : "Going to watch the sparring", "Excited"),
            new GoTo(places[member], 1.2f), new Muster(troupe),
            .. member == lead ? new Step[] { new Spotlight($"{lead.Resident.Name} and {rival.Resident.Name} are sparring{(crowd.Length > 0 ? " in front of a crowd" : "")}!", 2, 25_000) } : [],
            new Label(InRing(member) ? $"Sparring with {(member == lead ? rival : lead).Resident.Name}" : "Cheering at the sparring", "Excited"),
            .. script.For(member)
        ]);
        return true;
    }

    // Hitmonlee, Hitmonchan and Hitmontop train side by side: kicks, punches, spins, a bow.
    private bool PlanTraining(ParkActor lead, long now)
    {
        var others = Recruit(lead, o => HitmonTrio.Contains(o.Resident.Species), 2);
        if (others.Length == 0) return false;
        ParkActor[] cast = [lead, .. others];
        ParkPoint Place(ParkPoint middle, int i) => i switch
        {
            0 => new(middle.X, middle.Y - 12), 1 => new(middle.X - 20, middle.Y + 8), _ => new(middle.X + 20, middle.Y + 8)
        };
        if (Stage(lead, cast, Place) is not { } stage) return false;
        var places = cast.Select((a, i) => (a, Place(stage, i))).ToDictionary(p => p.a, p => p.Item2);
        var script = new Script(cast, a => 0);
        ParkPose Move(ParkActor a) => a.Resident.Species switch { 237 => ParkPose.Spin, _ => ParkPose.Attack };
        for (var set = 0; set < 3; set++)
        {
            script.Beat(1000, a => (Move(a), set == 2 ? ParkEmote.Exclaim : ParkEmote.None));
            script.Beat(500, _ => null);
        }
        script.Beat(1600, _ => (ParkPose.Charge, ParkEmote.None));
        script.Beat(1000, a => (Move(a), ParkEmote.None));
        foreach (var a in cast) script.Then(a, new Spark(ParkEffectKind.Gust), new Face(cast[(Array.IndexOf(cast, a) + 1) % cast.Length].Resident.Id));
        foreach (var a in cast) script.Then(a, new Act(ParkPose.Nod, 1400, Emote: ParkEmote.Heart), new Gain(ParkNeed.Fun, 12), new Gain(ParkNeed.Social, 12));
        script.Then(lead, new Journal($"{Names(cast)} trained together: kicks, punches, spins. At the end they bowed to each other.",
            others[0].Resident.Id, ParkSocialEventKind.Play));
        Cast(lead, cast, 2, now, (member, _, troupe) =>
        [
            new Label("Off to train with the others", "Determined"), new GoTo(places[member]), new Muster(troupe),
            .. member == lead ? new Step[] { new Spotlight("The Hitmons are training together!", 2, 20_000) } : [],
            new Label("Training with the Hitmons", "Determined"), .. script.For(member)
        ]);
        return true;
    }

    // ── Aura farming ─────────────────────────────────────────────────────────

    // Somewhere high with a view: the statues and nap spots up on the heights.
    private ParkSpot? Lookout(ParkActor actor, float width = 0)
    {
        var flies = CanFly(actor.Resident);
        var lookouts = _map.Spots.Where(s => s.Kind is ParkSpotKind.Statue or ParkSpotKind.Nap && _map.RegionAt(s.X, s.Y).High &&
            actor.Navigator.CanStand(s.X, s.Y) && (flies || actor.Navigator.Connected(new(actor.X, actor.Y), new(s.X, s.Y))) &&
            !Crowded(actor, s.X, s.Y, PersonalSpace + width) &&
            (width == 0 || actor.Navigator.CanStand(s.X - width, s.Y) && actor.Navigator.CanStand(s.X + width, s.Y)))
            .OrderBy(s => Distance(actor.X, actor.Y, s.X, s.Y)).Take(3).ToArray();
        return lookouts.Length == 0 ? null : lookouts[actor.Random.Next(lookouts.Length)];
    }

    private bool PlanAuraFarming(ParkActor lead, long now)
    {
        // Two of them who notice each other: a stare-down on the same height.
        if (lead.Random.Next(3) == 0 && Recruit(lead, o => AuraFarmers.Contains(o.Resident.Species), 1, 1600) is [var other] &&
            Lookout(lead, 18) is { } arena && PlanStareDown(lead, other, arena, now))
            return true;
        if (Lookout(lead) is not { } spot) return false;
        var name = lead.Resident.Name;
        var where = _map.RegionAt(spot.X, spot.Y).Name;
        var at = new ParkPoint(spot.X, spot.Y);
        Add(lead, new Label($"Heading up to {where}", "Determined"), new GoTo(at), new Act(ParkPose.Idle, 1000, spot.Facing),
            new Label($"Striking a dramatic pose at {where}", "Proud"), new Spotlight($"{name} is striking a dramatic pose at {where}", 1, 15_000), new Act(ParkPose.Pose, 2000, spot.Facing, ParkEmote.Sparkle),
            new Spark(ParkEffectKind.Gust), new Act(ParkPose.Idle, lead.Random.Next(7000, 12_000), spot.Facing),
            new Spark(ParkEffectKind.Gust), new Act(ParkPose.LookUp, 1800, spot.Facing));
        // Self-aware: now and then the pose ends in a stumble.
        var trips = lead.Random.Next(3) == 0;
        if (trips)
            Add(lead, new Label("Tripped right out of its pose", "Embarrassed"), new Act(ParkPose.Trip, 1100, spot.Facing, ParkEmote.Sweat),
                new Act(ParkPose.Idle, 900, (spot.Facing + 4) % 8, ParkEmote.Ellipsis),
                new Journal($"{name} struck a dramatic pose at {where} for a long time. Then it tripped. It hopes nobody saw."));
        // An admirer who happens to be near copies the pose, badly.
        else if (Recruit(lead, o => !AuraFarmers.Contains(o.Resident.Species) && o.Resident.Temperament is ParkTemperament.Playful or ParkTemperament.Feisty or ParkTemperament.Friendly, 1, 180) is [var fan])
        {
            Interrupt(fan);
            fan.PartnerId = lead.Resident.Id;
            Add(fan, new Label($"Watching {name} strike a pose", "Inspired"), new Act(ParkPose.Idle, 3500, Toward(new(fan.X, fan.Y), at), ParkEmote.Sparkle),
                new Act(ParkPose.Pose, 1800, spot.Facing, ParkEmote.Sparkle), new Act(ParkPose.Trip, 1000, spot.Facing, ParkEmote.Sweat),
                new Act(ParkPose.Pose, 1800, spot.Facing), new Gain(ParkNeed.Fun, 10), new Release(),
                new Journal($"{name} posed at {where} with the wind in its face. {fan.Resident.Name} has been copying the pose ever since.", lead.Resident.Id));
        }
        else if (lead.Random.Next(2) == 0)
            Add(lead, new Journal($"{name} stood at {where} looking dramatic for twenty minutes. Nobody was watching."));
        Add(lead, new Gain(ParkNeed.Fun, 12));
        return true;
    }

    private bool PlanStareDown(ParkActor lead, ParkActor other, ParkSpot arena, long now)
    {
        ParkActor[] cast = [lead, other];
        var places = new[] { new ParkPoint(arena.X - 18, arena.Y), new ParkPoint(arena.X + 18, arena.Y) };
        if (!cast.Select((a, i) => a.Navigator.CanStand(places[i].X, places[i].Y) &&
                (CanFly(a.Resident) || a.Navigator.Connected(new(a.X, a.Y), places[i]))).All(b => b)) return false;
        var where = _map.RegionAt(arena.X, arena.Y).Name;
        // A passer-by breaks the spell, if anyone is about.
        var passer = _actors.Values.FirstOrDefault(o => !cast.Contains(o) && !o.IsAsleep && !o.Indoors && !o.Hidden &&
            Distance(o.X, o.Y, arena.X, arena.Y) < 200);
        var script = new Script(cast, a => a == lead ? 2 : 6);
        script.Beat(1800, _ => (ParkPose.Pose, ParkEmote.None));
        script.Then(lead, new Spark(ParkEffectKind.Gust));
        script.Beat(5000, _ => (ParkPose.Idle, ParkEmote.Ellipsis));
        script.Then(other, new Spark(ParkEffectKind.Gust));
        script.Beat(3000, _ => null);
        if (passer is not null)
        {
            foreach (var a in cast) script.Then(a, new Face(passer.Resident.Id), new Act(ParkPose.Idle, 1800, Emote: ParkEmote.Question));
            script.Then(lead, new Journal($"{lead.Resident.Name} and {other.Resident.Name} stared each other down at {where}, the wind in their faces. " +
                $"Nobody moved. Then {passer.Resident.Name} walked past and they both forgot what they were doing.", other.Resident.Id, ParkSocialEventKind.Rest));
        }
        else
        {
            script.Beat(1400, _ => (ParkPose.Nod, ParkEmote.None));
            script.Then(lead, new Journal($"{lead.Resident.Name} and {other.Resident.Name} stared each other down at {where}. Neither blinked. They called it a draw.",
                other.Resident.Id, ParkSocialEventKind.Rest));
        }
        foreach (var a in cast) script.Then(a, new Gain(ParkNeed.Fun, 10));
        Cast(lead, cast, 2, now, (member, i, troupe) =>
        [
            new Label($"Heading up to {where}", "Determined"), new GoTo(places[i]), new Muster(troupe),
            .. member == lead ? new Step[] { new Spotlight($"{lead.Resident.Name} and {other.Resident.Name} are staring each other down at {where}", 2, 15_000) } : [],
            new Label($"Staring down {(member == lead ? other : lead).Resident.Name}", "Proud"), .. script.For(member)
        ]);
        return true;
    }

    // ── Loners ───────────────────────────────────────────────────────────────

    // The quietest spot it can reach: as far as possible from everyone else.
    private ParkSpot? QuietCorner(ParkActor actor) => _map.Spots
        .Where(s => s.Kind is ParkSpotKind.Shore or ParkSpotKind.Nap or ParkSpotKind.Gate or ParkSpotKind.Statue &&
            actor.Navigator.CanStand(s.X, s.Y) && actor.Navigator.Connected(new(actor.X, actor.Y), new(s.X, s.Y)) &&
            Distance(actor.X, actor.Y, s.X, s.Y) < 500 && !Crowded(actor, s.X, s.Y, PersonalSpace))
        .OrderByDescending(s => _actors.Values.Where(o => o != actor && !o.Indoors).Select(o => Distance(o.X, o.Y, s.X, s.Y)).DefaultIfEmpty(999).Min() +
            actor.Random.Next(40))
        .FirstOrDefault();

    private bool PlanSolitude(ParkActor actor)
    {
        if (QuietCorner(actor) is not { } corner) return false;
        var name = actor.Resident.Name;
        var sit = actor.Random.Next(14_000, 24_000);
        Add(actor, new Label("Looking for somewhere quiet", "Calm"), new GoTo(new(corner.X, corner.Y)),
            new Label("Alone with its thoughts", "Peaceful"), new Act(ParkPose.Sit, sit, corner.Facing, ParkEmote.Ellipsis));
        // One friend may come and sit beside it. Nobody says a word.
        var friend = Recruit(actor, o => (Relation?.Invoke(actor.Resident.Id, o.Resident.Id).Friendship ?? 0) > 40 ||
            Feeling(actor, o) is ParkPairKind.Friends or ParkPairKind.Family or ParkPairKind.Love, 1, 400).FirstOrDefault();
        var beside = new ParkPoint(corner.X + 16, corner.Y);
        if (friend is not null && actor.Random.Next(3) == 0 && friend.Navigator.CanStand(beside.X, beside.Y) &&
            friend.Navigator.Connected(new(friend.X, friend.Y), beside))
        {
            Interrupt(friend);
            friend.PartnerId = actor.Resident.Id;
            Add(friend, new Label($"Going to sit with {name}", "Calm"), new GoTo(beside, .8f), new Face(actor.Resident.Id),
                new Label($"Sitting quietly with {name}", "Peaceful"), new Act(ParkPose.Sit, sit / 2, corner.Facing),
                new Gain(ParkNeed.Social, 16), new Release(),
                new Journal($"{name} keeps to itself, but it let {friend.Resident.Name} sit beside it. Neither said a word.", actor.Resident.Id, ParkSocialEventKind.Bond));
        }
        return true;
    }
}
