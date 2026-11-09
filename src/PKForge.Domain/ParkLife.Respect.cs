namespace PKForge.Domain;

/// <summary>
/// Respect. The legendaries, the mythicals, the pseudo-legendaries and the few others with a
/// presence (Gyarados, Charizard, Steelix…) are given room: the others step aside and bow as they
/// pass, the timid run, and nobody walks up to say hello. Nobody, that is, but the ones who never
/// noticed there was anything to be afraid of: Psyduck, Slowpoke, Wooper, Magikarp, the babies…
/// one of them sometimes walks straight up to Mewtwo and stares, while everyone else holds their
/// breath.
/// </summary>
public sealed partial class ParkLife
{
    // Legendaries and mythicals, the pseudo-legendaries and a few with a presence of their own.
    private static readonly HashSet<int> Imposing =
    [
        // Presence: Charizard, Gyarados, Steelix, Tyranitar's line top, Aggron, Rhyperior, Kingambit…
        6, 130, 208, 248, 306, 464, 983,
        // Pseudo-legendaries.
        149, 373, 376, 445, 635, 706, 784, 887, 998,
        // Legendaries and mythicals.
        144, 145, 146, 150, 151, 243, 244, 245, 249, 250, 251, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386,
        480, 481, 482, 483, 484, 485, 486, 487, 488, 489, 490, 491, 492, 493, 494, 638, 639, 640, 641, 642, 643, 644, 645,
        646, 647, 648, 649, 716, 717, 718, 719, 720, 721, 785, 786, 787, 788, 789, 790, 791, 792, 800, 801, 802, 807, 808, 809,
        888, 889, 890, 891, 892, 893, 894, 895, 896, 897, 898, 905, 1001, 1002, 1003, 1004, 1007, 1008, 1014, 1015, 1016, 1017, 1024, 1025,
    ];

    // Those who never notice there is anything to be afraid of: the dozy, the simple, the babies.
    private static readonly HashSet<int> Oblivious =
    [
        54, 79, 80, 199, 194, 195, 980, 129, 143, 446, 202, 360, 399, 327, 206, 771, 775, 83, 108, 132,
        172, 173, 174, 175, 236, 238, 239, 240, 298, 406, 433, 438, 439, 440, 447, 848,
    ];

    private const float GivenRoom = 34;
    private readonly Dictionary<(string, string), long> _bowedUntil = new();

    public static bool IsImposing(ParkLifeResident resident) => Imposing.Contains(resident.Species);

    // Whether this one gives that one room: an ordinary Pokémon before an imposing one that is not
    // its family, its love or its friend.
    private bool InAwe(ParkActor actor, ParkActor of) =>
        Imposing.Contains(of.Resident.Species) && !Imposing.Contains(actor.Resident.Species) && !Oblivious.Contains(actor.Resident.Species) &&
        actor.Resident.Species != of.Resident.Species && Feeling(actor, of) is not (ParkPairKind.Love or ParkPairKind.Family or ParkPairKind.Friends);

    // Too close to someone imposing: the timid run, the others step aside and bow.
    private void GiveRoom(ParkActor actor, long now)
    {
        if (now % 250 >= TickMilliseconds || actor.Busy || actor.IsAsleep || actor.Indoors || now < actor.CalmUntil || actor.Resident.WaterBound) return;
        // Off after the ball (or anything else it means to pick up): it forgets to be shy.
        if (actor.Plan.Any(step => step is Pickup)) return;
        var great = _actors.Values.FirstOrDefault(other => other != actor && !other.Indoors && !other.Hidden && !other.Submerged &&
            Distance(actor.X, actor.Y, other.X, other.Y) < GivenRoom && InAwe(actor, other));
        if (great is null) return;
        actor.CalmUntil = now + 20_000;
        // Once it has bowed to this one, it keeps its distance quietly for a while.
        var key = (actor.Resident.Id, great.Resident.Id);
        if (now < _bowedUntil.GetValueOrDefault(key)) return;
        _bowedUntil[key] = now + 120_000;
        Interrupt(actor);
        var name = great.Resident.Name;
        var facing = ParkMotion.Facing(great.X - actor.X, great.Y - actor.Y);
        // Back the way it came, a little way off.
        var dx = actor.X - great.X; var dy = actor.Y - great.Y;
        var length = MathF.Max(1, MathF.Sqrt(dx * dx + dy * dy));
        var back = new ParkPoint(actor.X + dx / length * 46, actor.Y + dy / length * 46);
        var timid = actor.Resident.Temperament == ParkTemperament.Timid || Feeling(actor, great) == ParkPairKind.Fears;
        if (timid)
        {
            Add(actor, new Label($"Running away from {name}", "Scared"), new Act(ParkPose.Cringe, 600, facing, ParkEmote.Exclaim));
            var away = Enumerable.Range(0, 6).Select(_ => FreePoint(actor.Navigator, actor.Random, actor)).OfType<ParkPoint>()
                .OrderByDescending(p => Distance(p.X, p.Y, great.X, great.Y)).FirstOrDefault();
            if (away != default) Add(actor, new GoTo(away, 2.1f));
            Add(actor, new Act(ParkPose.Idle, 2000, Emote: ParkEmote.Sweat));
            return;
        }
        Add(actor, new Label($"Making way for {name}", "Nervous"), new Act(ParkPose.Idle, 500, facing, ParkEmote.Exclaim));
        if (actor.Navigator.CanStand(back.X, back.Y) && actor.Navigator.Connected(new(actor.X, actor.Y), back)) Add(actor, new GoTo(back, 1.3f));
        Add(actor, new Act(ParkPose.Nod, 1200, facing, ParkEmote.Sweat), new Label($"Keeping a respectful distance from {name}", "Nervous"),
            new Act(ParkPose.Idle, 1500, facing));
    }

    // One of the oblivious walks straight up to someone imposing, looks up and stares. The others
    // nearby hold their breath; the great one does nothing at all, or lets it sit beside it.
    private bool PlanDaring(ParkActor lead, ParkActor great, long now)
    {
        if (Beside(lead, great) is not { } spot) return false;
        ParkActor[] cast = [lead, great];
        string me = lead.Resident.Name, them = great.Resident.Name;
        var random = lead.Random;
        var script = new Script(cast, a => a == lead ? Toward(spot, new(great.X, great.Y)) : Toward(new(great.X, great.Y), spot));
        script.Beat(1300, a => a == lead ? (ParkPose.LookUp, ParkEmote.Question) : (ParkPose.Idle, ParkEmote.None));
        script.Beat(1500, a => a == lead ? (ParkPose.Idle, ParkEmote.None) : (ParkPose.Idle, ParkEmote.Ellipsis));
        script.Beat(900, a => a == lead ? (ParkPose.Hop, ParkEmote.Note) : null);
        var friendly = random.Next(2) == 0;
        string journal;
        if (friendly)
        {
            script.Beat(1400, a => a == great ? (ParkPose.Nod, ParkEmote.None) : (ParkPose.Hop, ParkEmote.Heart));
            script.Beat(5000, a => a == lead ? (ParkPose.Sit, ParkEmote.Note) : (ParkPose.Idle, ParkEmote.None));
            journal = $"{me} walked straight up to {them} and sat down beside it as if they were old friends. Everyone else is still talking about it.";
        }
        else
        {
            script.Beat(2600, a => a == lead ? (ParkPose.Idle, ParkEmote.Question) : (ParkPose.Idle, ParkEmote.Ellipsis));
            script.Beat(1200, a => a == lead ? (ParkPose.Hop, ParkEmote.Note) : null);
            journal = $"{me} walked straight up to {them} and stared at it for a long time. Everyone else held their breath. {them} did nothing at all.";
        }
        script.Then(lead, new Gain(ParkNeed.Social, 10), new Gain(ParkNeed.Fun, 8), new Journal(journal, great.Resident.Id, ParkSocialEventKind.Bond));
        Cast(lead, cast, 2, now, (member, _, troupe) => member == lead
            ? [new Label($"Wandering over to {them}", "Curious"), new GoTo(spot, .8f), new Muster(troupe),
               new Spotlight($"{me} walked right up to {them}!", 2, 20_000), new Label($"Staring at {them}", "Curious"), .. script.For(member)]
            : [new Label($"{me} is coming over", "Calm"), new Muster(troupe), new Face(lead.Resident.Id), new Label($"Being stared at by {me}", "Calm"), .. script.For(member)]);
        // Everyone close enough to see it stops and holds its breath.
        foreach (var onlooker in _actors.Values.Where(o => o != lead && o != great && Free(o) && InAwe(o, great) &&
                     Distance(o.X, o.Y, great.X, great.Y) < 150).OrderBy(o => Distance(o.X, o.Y, great.X, great.Y)).Take(4).ToArray())
        {
            Interrupt(onlooker);
            onlooker.CalmUntil = now + 30_000;
            Add(onlooker, new Act(ParkPose.Idle, 2500 + onlooker.Random.Next(1500), ParkMotion.Facing(great.X - onlooker.X, great.Y - onlooker.Y), ParkEmote.Exclaim),
                new Label($"Holding its breath while {me} stares at {them}", "Nervous"),
                new Act(ParkPose.Idle, 6000, ParkMotion.Facing(great.X - onlooker.X, great.Y - onlooker.Y), ParkEmote.Sweat));
        }
        return true;
    }
}
