namespace PKForge.Domain;

/// <summary>
/// The park's rhythms: the morning call (as in a Mystery Dungeon guild: everyone lines up on the
/// home plaza, three cheers, and someone always sleeps through it), Hoothoot hooting on the hour,
/// and Politoed leading the rain chorus with the Poliwag lined up in front of it.
/// </summary>
public sealed partial class ParkLife
{
    private DateOnly _morningCallDay;
    private long _nextHoot = 60 * 60_000;

    private static readonly HashSet<int> ChorusLeaders = [186, 62];   // Politoed, Poliwrath
    private static readonly HashSet<int> ChorusSingers = [60, 61, 186, 62, 194, 195, 270, 271, 418, 535, 536, 537];

    private void UpdateRhythms(long now)
    {
        if (now % 1000 >= TickMilliseconds) return;
        if (Phase == ParkDayPhase.Morning && _morningCallDay != Today) PlanMorningCall(now);
        if (now >= _nextHoot)
        {
            _nextHoot = now + 60 * 60_000;
            Hoot();
        }
        if (Wet) SudowoodoRuns();
        // At dusk, while they are still up.
        if (Phase is ParkDayPhase.Evening or ParkDayPhase.Night && _lightShowDay != Today) PlanLightShow(now);
    }

    // ── Sudowoodo in the rain ────────────────────────────────────────────────

    // A "tree" that hates water: the first drops and it runs for cover.
    private void SudowoodoRuns()
    {
        foreach (var tree in _actors.Values.Where(a => a.Resident.Has(ParkQuirk.PretendTree) && a.Activity == "Pretending to be a tree").ToArray())
        {
            var cover = _map.Spots.Where(s => s.Kind is ParkSpotKind.Hide or ParkSpotKind.Door && tree.Navigator.CanStand(s.X, s.Y) &&
                    tree.Navigator.Connected(new(tree.X, tree.Y), new(s.X, s.Y)))
                .OrderBy(s => Distance(s.X, s.Y, tree.X, tree.Y)).FirstOrDefault();
            Interrupt(tree);
            var name = tree.Resident.Name;
            Add(tree, new Label("A tree that hates the rain", "Scared"), new Act(ParkPose.Hurt, 700, Emote: ParkEmote.Exclaim),
                new Spotlight($"It started raining and {name}, the tree, is running for cover!", 2, 15_000));
            if (cover is not null) Add(tree, new GoTo(new(cover.X, cover.Y), 2f));
            Add(tree, new Act(ParkPose.Cringe, 3000, Emote: ParkEmote.Sweat),
                new Journal($"It started raining and {name}, the tree, ran for cover. Some tree."));
        }
    }

    // ── Volbeat's light show ─────────────────────────────────────────────────

    private DateOnly _lightShowDay;

    // At dusk Volbeat draws a heart of light in the dark for Illumise, who watches and directs.
    private void PlanLightShow(long now)
    {
        var volbeat = _actors.Values.FirstOrDefault(a => a.Resident.Species == 313 && Free(a));
        var illumise = volbeat is null ? null : _actors.Values.FirstOrDefault(a => a.Resident.Species == 314 && Free(a) &&
            volbeat.Navigator.Connected(new(volbeat.X, volbeat.Y), new(a.X, a.Y)));
        if (volbeat is null || illumise is null) return;
        // A heart of points that Volbeat can walk round: in front of Illumise if there is room,
        // else somewhere open nearby.
        ParkPoint[] HeartAround(ParkPoint middle) => Enumerable.Range(0, 12).Select(k =>
        {
            var t = k / 12f * MathF.PI * 2;
            var x = 16 * MathF.Pow(MathF.Sin(t), 3);
            var y = -(13 * MathF.Cos(t) - 5 * MathF.Cos(2 * t) - 2 * MathF.Cos(3 * t) - MathF.Cos(4 * t));
            return new ParkPoint(middle.X + x * 1.2f, middle.Y + y * 1.2f);
        }).ToArray();
        bool Walkable(ParkPoint[] points) => points.All(p => volbeat.Navigator.CanStand(p.X, p.Y) && volbeat.Navigator.Connected(new(volbeat.X, volbeat.Y), p));
        var candidates = new List<ParkPoint> { new(illumise.X, illumise.Y - 26), new(illumise.X, illumise.Y + 26) };
        for (var k = 0; k < 8; k++)
            if (FreePoint(volbeat.Navigator, volbeat.Random, volbeat, land: true) is { } open) candidates.Add(open);
        var heart = candidates.Select(HeartAround).FirstOrDefault(Walkable);
        if (heart is null) return;
        _lightShowDay = Today;
        Interrupt(volbeat); Interrupt(illumise);
        volbeat.PartnerId = illumise.Resident.Id; illumise.PartnerId = volbeat.Resident.Id;
        string v = volbeat.Resident.Name, i = illumise.Resident.Name;
        Add(illumise, new Label($"Watching {v}'s light show", "In love"), new Face(volbeat.Resident.Id), new Act(ParkPose.Sit, 30_000, Emote: ParkEmote.Heart), new Release());
        Add(volbeat, new Label($"Drawing a heart of light for {i}", "In love"), new GoTo(heart[0]),
            new Spotlight($"{v} is drawing a heart of light in the dark for {i}!", 2, 30_000, "Lights in the Dark"));
        foreach (var point in heart.Skip(1).Append(heart[0]))
            Add(volbeat, new GoTo(point, .7f), new Spark(ParkEffectKind.Sparkle));
        Add(volbeat, new Spark(ParkEffectKind.Hearts), new Act(ParkPose.Nod, 1400, Emote: ParkEmote.Heart), new Gain(ParkNeed.Social, 20),
            new Journal($"In the dark, {v} drew a heart of light for {i}. {i} pretended not to be impressed, for about a second.", illumise.Resident.Id, ParkSocialEventKind.Bond),
            new Release());
    }

    // ── The morning call ─────────────────────────────────────────────────────

    private void PlanMorningCall(long now)
    {
        var plaza = _map.AtHome(168, 150);
        var home = _map.RegionAt(plaza.X, plaza.Y);
        var crew = _actors.Values.Where(a => !IsVisitor(a.Resident.Id) && Free(a) && home.Contains(a.X, a.Y) &&
            a.Navigator.Connected(new(a.X, a.Y), plaza)).Take(9).ToList();
        if (crew.Count < 3) return;
        _morningCallDay = Today;
        // The loudest leads: Loudred and Exploud by right, else the boldest.
        var lead = crew.FirstOrDefault(a => a.Resident.Species is 293 or 294 or 295 or 441) ??
            crew.OrderByDescending(a => a.Resident.Personality is ParkPersonality.Bold or ParkPersonality.Proud).First();
        crew.Remove(lead);
        ParkActor[] cast = [lead, .. crew];
        // The lead faces the others, who stand in two rows facing it.
        ParkPoint Place(ParkPoint middle, int i) => i == 0 ? new(middle.X, middle.Y - 18)
            : new(middle.X + ((i - 1) % 4 - 1.5f) * 18, middle.Y + 6 + (i - 1) / 4 * 16);
        if (Stage(lead, cast, Place) is not { } stage)
        {
            _morningCallDay = default;
            return;
        }
        // Someone always sleeps through it.
        var sleeper = _actors.Values.FirstOrDefault(a => a.IsAsleep && !a.Indoors && home.Contains(a.X, a.Y) && !IsVisitor(a.Resident.Id));
        var script = new Script(cast, a => a == lead ? 0 : 4);
        script.Beat(1500, a => a == lead ? (ParkPose.Pose, ParkEmote.Exclaim) : (ParkPose.Idle, ParkEmote.None));
        for (var cheer = 0; cheer < 3; cheer++)
        {
            script.Beat(900, _ => (ParkPose.Hop, ParkEmote.Note));
            script.Beat(400, _ => null);
        }
        script.Beat(1400, a => a == lead ? (ParkPose.Nod, ParkEmote.Sparkle) : (ParkPose.Nod, ParkEmote.None));
        foreach (var a in cast) script.Then(a, new Gain(ParkNeed.Social, 10), new Gain(ParkNeed.Energy, 6));
        script.Then(lead, new Journal(sleeper is not null
            ? $"Morning call: {lead.Resident.Name} led the cheers, {Names(crew)} cheered three times. {sleeper.Resident.Name} slept through the whole thing."
            : $"Morning call: {lead.Resident.Name} led the cheers and {Names(crew)} cheered three times, loud enough to wake the whole park."));
        Cast(lead, cast, 3, now, (member, i, troupe) =>
        [
            new Label("Off to the morning call", "Determined"), new GoTo(Place(stage, i), 1.2f), new Muster(troupe),
            .. member == lead ? new Step[] { new Spotlight("Morning call on the plaza!", 2, 20_000) } : [],
            new Label(member == lead ? "Leading the morning call" : "At the morning call", "Joyful"), .. script.For(member)
        ]);
    }

    // ── Hoothoot ─────────────────────────────────────────────────────────────

    private void Hoot()
    {
        // In the air or not (it lands first), unless it is in the middle of something with others.
        foreach (var owl in _actors.Values.Where(a => a.Resident.Species is 163 or 164 && !a.Indoors && !a.InGame && a.Troupe is null && a.PartnerId is null && !a.IsAsleep))
        {
            Interrupt(owl);
            Add(owl, new Label("Hooting the hour", "Proud"), new Act(ParkPose.Sing, 1600, Emote: ParkEmote.Note, Prop: ParkProp.Notes),
                new Act(ParkPose.Idle, 800), new Act(ParkPose.Hop, 600));
            foreach (var other in Near(owl, 90).Where(o => !o.Busy && !o.IsAsleep)) other.Say(ParkEmote.Question, Now);
            if (owl.Random.Next(3) == 0) Add(owl, new Journal($"{owl.Resident.Name} hooted on the hour, right on time, as it does every hour."));
        }
    }

    // ── The rain chorus ──────────────────────────────────────────────────────

    private bool PlanChorus(ParkActor leader, long now)
    {
        if (!Wet || Spot(leader, ParkSpotKind.Shore) is not { } shore) return false;
        var singers = Recruit(leader, o => ChorusSingers.Contains(o.Resident.Species), 4, 400);
        if (singers.Length == 0) return false;
        ParkActor[] cast = [leader, .. singers];
        ParkPoint Place(ParkPoint middle, int i) => i == 0 ? middle : new(middle.X + (i - (cast.Length + 1) / 2f) * 16, middle.Y + 16);
        // By the water when there is room, else wherever they are.
        var byWater = new ParkPoint(shore.X, shore.Y - 18);
        var stage = cast.Select((a, i) => (a, at: Place(byWater, i))).All(p => p.a.Navigator.CanStand(p.at.X, p.at.Y) &&
            (CanFly(p.a.Resident) || p.a.Navigator.Connected(new(p.a.X, p.a.Y), p.at))) ? byWater : Stage(leader, cast, Place) ?? default;
        if (stage == default) return false;
        var script = new Script(cast, a => a == leader ? 0 : 4);
        for (var verse = 0; verse < 3; verse++)
        {
            script.Beat(2200, a => a == leader ? (ParkPose.Sing, ParkEmote.Note) : null);
            script.Beat(1800, a => a == leader ? null : (ParkPose.Sing, ParkEmote.Note));
        }
        script.Beat(1600, _ => (ParkPose.Hop, ParkEmote.Sparkle));
        foreach (var a in cast) script.Then(a, new Gain(ParkNeed.Fun, 12));
        script.Then(leader, new Journal($"The rain came, and {leader.Resident.Name} led the rain chorus by the water, with {Names(singers)} singing back."));
        Cast(leader, cast, 2, now, (member, i, troupe) =>
        [
            new Label("Off to sing in the rain", "Joyful"), new GoTo(Place(stage, i)), new Muster(troupe),
            .. member == leader ? new Step[] { new Spotlight($"{leader.Resident.Name} is leading the rain chorus!", 2, 25_000) } : [],
            new Label(member == leader ? "Leading the rain chorus" : "Singing in the rain chorus", "Joyful"), .. script.For(member)
        ]);
        return true;
    }
}
