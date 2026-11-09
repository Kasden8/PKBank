namespace PKForge.Domain;

/// <summary>
/// Meetings: two residents who notice something about each other. A shiny and one of its own kind
/// in the usual colours walk round each other, puzzled. Two of the same kind copy each other until
/// neither knows who started. A regional form meets the one from here. A little one looks up at
/// what it may become one day (Pikachu meets Raichu and decides, once again, that it is happy as it
/// is; Magikarp splashes proudly at Gyarados). Skitty and Wailord get on wonderfully. Two Ditto try
/// to copy each other; two Psyduck share a headache; the park's shinies compare sparkles. The same
/// two meet like this at most once in a few hours.
/// </summary>
public sealed partial class ParkLife
{
    private enum Meeting { ShinyMirror, Twins, Cousins, Elder, SkittyWailord, Dittos, Psyducks, Shinies, Daring }

    private readonly Dictionary<(string, string), long> _metAt = new();
    private static readonly TimeSpan MeetAgain = TimeSpan.FromHours(3);

    private static (string, string) PairKey(ParkActor a, ParkActor b) =>
        string.CompareOrdinal(a.Resident.Id, b.Resident.Id) < 0 ? (a.Resident.Id, b.Resident.Id) : (b.Resident.Id, a.Resident.Id);

    // What these two would make of each other, if anything special.
    private static Meeting? MeetingOf(ParkLifeResident a, ParkLifeResident b)
    {
        if (a.Species == b.Species)
            return a.Species switch
            {
                132 => Meeting.Dittos,
                54 => Meeting.Psyducks,
                _ when a.Shiny != b.Shiny => Meeting.ShinyMirror,
                _ when a.Form != b.Form => Meeting.Cousins,
                _ => Meeting.Twins
            };
        if (a.Species is 300 && b.Species is 321 || a.Species is 321 && b.Species is 300) return Meeting.SkittyWailord;
        // Only the oblivious one starts this one.
        if (Oblivious.Contains(a.Species) && Imposing.Contains(b.Species)) return Meeting.Daring;
        if (ParkSpecies.BecomesOneDay(a.Species, b.Species) || ParkSpecies.BecomesOneDay(b.Species, a.Species)) return Meeting.Elder;
        if (a.Shiny && b.Shiny) return Meeting.Shinies;
        return null;
    }

    private bool CanMeet(ParkActor actor, ParkActor other, Meeting meeting) =>
        other != actor && (Free(other) || meeting == Meeting.SkittyWailord && other.Resident.WaterBound && (other.Submerged || !other.Busy)) &&
        !(other.Resident.WaterBound && meeting != Meeting.SkittyWailord) &&
        Now - _metAt.GetValueOrDefault(PairKey(actor, other), long.MinValue / 2) > MeetAgain.TotalMilliseconds &&
        (Distance(actor.X, actor.Y, other.X, other.Y) < 600 || meeting == Meeting.SkittyWailord) &&
        (meeting == Meeting.SkittyWailord || CanFly(other.Resident) || other.Navigator.Connected(new(other.X, other.Y), new(actor.X, actor.Y)));

    private (ParkActor Other, Meeting Meeting)? Counterpart(ParkActor actor)
    {
        if (actor.Resident.WaterBound && actor.Resident.Species != 321) return null;
        return _actors.Values.Select(o => (Other: o, Meeting: MeetingOf(actor.Resident, o.Resident)))
            .Where(p => p.Meeting is { } m && CanMeet(actor, p.Other, m))
            .OrderBy(p => Distance(actor.X, actor.Y, p.Other.X, p.Other.Y))
            .Select(p => ((ParkActor, Meeting)?)(p.Other, p.Meeting!.Value)).FirstOrDefault();
    }

    private void MeetWeights(ParkActor actor, Dictionary<Activity, float> weights)
    {
        if (Phase == ParkDayPhase.Night) return;
        // Checked now and then rather than every time, it is a search through everyone.
        if (actor.Random.Next(3) > 0 || Counterpart(actor) is null) return;
        weights[Activity.Meet] = 14;
    }

    private bool PlanMeet(ParkActor actor, long now)
    {
        if (Counterpart(actor) is not var (other, meeting)) return false;
        var planned = meeting switch
        {
            Meeting.SkittyWailord => PlanSkittyWailord(actor, other, now),
            Meeting.Daring => PlanDaring(actor, other, now),
            _ => PlanMeeting(actor, other, meeting, now)
        };
        if (planned) _metAt[PairKey(actor, other)] = now;
        return planned;
    }

    private bool PlanMeeting(ParkActor lead, ParkActor other, Meeting meeting, long now)
    {
        ParkActor[] cast = [lead, other];
        ParkPoint Place(ParkPoint middle, int i) => new(middle.X + (i == 0 ? -15 : 15), middle.Y);
        if (Stage(lead, cast, Place) is not { } stage) return false;
        var places = new[] { Place(stage, 0), Place(stage, 1) };
        // The little one and the big one, for an elder meeting: whoever comes first in the line.
        var (small, big) = ParkSpecies.BecomesOneDay(lead.Resident.Species, other.Resident.Species) ? (lead, other) : (other, lead);
        var (plain, shiny) = lead.Resident.Shiny ? (other, lead) : (lead, other);
        // Facing each other across the middle (they may swap sides on the way).
        var swapped = false;
        var script = new Script(cast, a => (a == lead) ^ swapped ? 2 : 6);
        string me = lead.Resident.Name, them = other.Resident.Name;
        // What each one is doing, seen from its own side, naming the other.
        Func<ParkActor, string> label;
        string Other(ParkActor m) => (m == lead ? other : lead).Resident.Name;
        string? journal;
        var random = lead.Random;
        switch (meeting)
        {
            case Meeting.ShinyMirror:
            {
                label = m => $"Puzzled by {Other(m)}";
                script.Beat(1200, a => (ParkPose.Idle, a == plain ? ParkEmote.Exclaim : ParkEmote.None));
                script.Beat(1600, a => (ParkPose.Idle, ParkEmote.Question));
                // They walk round each other: each goes to where the other stood.
                script.Then(lead, new GoTo(places[1], .6f), new Face(other.Resident.Id));
                script.Then(other, new GoTo(places[0], .6f), new Face(lead.Resident.Id));
                swapped = true;
                script.Beat(900, a => (ParkPose.LookUp, ParkEmote.Question));
                var admired = random.Next(2) == 0;
                if (admired)
                {
                    script.Then(shiny, new Spark(ParkEffectKind.Shine));
                    script.Beat(2200, a => a == shiny ? (ParkPose.Pose, ParkEmote.Sparkle) : (ParkPose.Idle, ParkEmote.Sparkle));
                    journal = $"{plain.Resident.Name} could not stop staring at {shiny.Resident.Name}, one of its own kind in all the wrong colours. " +
                        $"{shiny.Resident.Name} pretended not to notice, and posed a little.";
                }
                else
                {
                    script.Beat(1500, _ => (ParkPose.Idle, ParkEmote.Ellipsis));
                    script.Beat(1400, _ => (ParkPose.Nod, ParkEmote.Heart));
                    journal = $"{plain.Resident.Name} met {shiny.Resident.Name}: the same kind, different colours. " +
                        "They walked round each other twice, very puzzled, then decided it did not matter.";
                }
                break;
            }
            case Meeting.Twins:
            {
                // A mirror game: one moves, the other copies a beat later.
                label = m => $"Copying {Other(m)}";
                var moves = new[] { ParkPose.Hop, ParkPose.Spin, ParkPose.Nod, ParkPose.LookUp, ParkPose.Pose }.OrderBy(_ => random.Next()).Take(4).ToArray();
                script.Beat(1000, _ => (ParkPose.Idle, ParkEmote.Exclaim));
                foreach (var move in moves)
                {
                    script.Beat(900, a => a == lead ? (move, ParkEmote.None) : null);
                    script.Beat(900, a => a == other ? (move, ParkEmote.None) : null);
                }
                script.Beat(1200, _ => (ParkPose.Idle, ParkEmote.Question));
                script.Beat(1400, _ => (ParkPose.Hop, ParkEmote.Note));
                journal = $"{me} and {them}, two of a kind, spent a while copying each other. Nobody could tell which one started.";
                break;
            }
            case Meeting.Cousins:
                label = m => $"Meeting {Other(m)}, from far away";
                script.Beat(1500, _ => (ParkPose.Idle, ParkEmote.Question));
                script.Beat(1800, _ => (ParkPose.LookUp, ParkEmote.None));
                script.Beat(2500, a => a == lead ? (ParkPose.Nod, ParkEmote.Note) : (ParkPose.Idle, ParkEmote.Ellipsis));
                script.Beat(1400, _ => (ParkPose.Nod, ParkEmote.Heart));
                journal = $"{me} met {them}: the same kind, but from somewhere far away, and it showed. They compared notes for a long time.";
                break;
            case Meeting.Elder:
            {
                var smallName = small.Resident.Name; var bigName = big.Resident.Name;
                label = m => m == small ? $"Looking up to {bigName}" : $"Meeting little {smallName}";
                if (small.Resident.Species == 25 && big.Resident.Species == 26)
                {
                    // Pikachu has turned down the Thunder Stone before.
                    script.Beat(2000, a => a == small ? (ParkPose.LookUp, ParkEmote.Ellipsis) : (ParkPose.Pose, ParkEmote.None));
                    script.Beat(1800, a => a == small ? (ParkPose.Idle, ParkEmote.None) : (ParkPose.Idle, ParkEmote.Question));
                    script.Then(small, new Act(ParkPose.Idle, 400, (small == lead ? 2 : 6) + 4 & 7));
                    script.Beat(1500, a => a == small ? (ParkPose.Hop, ParkEmote.Note) : (ParkPose.Idle, ParkEmote.Sweat));
                    journal = $"{smallName} looked at {bigName} for a long time, then decided, once again, that it is happy as it is.";
                }
                else if (small.Resident.Species == 129 && big.Resident.Species == 130)
                {
                    script.Then(small, new Spark(ParkEffectKind.Splash));
                    script.Beat(2200, a => a == small ? (ParkPose.Hop, ParkEmote.Exclaim) : (ParkPose.Idle, ParkEmote.Ellipsis));
                    script.Beat(1800, a => a == small ? (ParkPose.Hop, ParkEmote.Sparkle) : (ParkPose.Nod, ParkEmote.Heart));
                    journal = $"{smallName} splashed as hard as it could to show {bigName} what it can do. {bigName} remembers what that was like.";
                }
                else
                {
                    script.Beat(2000, a => a == small ? (ParkPose.LookUp, ParkEmote.Sparkle) : (ParkPose.Idle, ParkEmote.None));
                    script.Beat(1800, a => a == small ? (ParkPose.Pose, ParkEmote.None) : (ParkPose.Nod, ParkEmote.Note));
                    script.Beat(1400, a => a == small ? (ParkPose.Hop, ParkEmote.Heart) : (ParkPose.Nod, ParkEmote.Heart));
                    journal = random.Next(2) == 0
                        ? $"{smallName} looked up at {bigName} for a long time. One day, maybe."
                        : $"{bigName} showed little {smallName} how it is done. {smallName} tried to copy it and nearly fell over.";
                }
                break;
            }
            case Meeting.Dittos:
                label = _ => "Trying to copy another Ditto";
                script.Beat(1000, _ => (ParkPose.Idle, ParkEmote.Exclaim));
                script.Then(lead, new Spark(ParkEffectKind.Psychic));
                script.Then(other, new Spark(ParkEffectKind.Psychic));
                script.Beat(2500, _ => (ParkPose.Charge, ParkEmote.None));
                script.Beat(2500, _ => (ParkPose.Idle, ParkEmote.Question));
                script.Beat(2000, _ => (ParkPose.Idle, ParkEmote.Ellipsis));
                journal = $"{me} and {them}, both Ditto, tried to copy each other. For a whole minute nothing happened, very hard.";
                break;
            case Meeting.Psyducks:
                label = _ => "Sharing a headache";
                script.Beat(2500, _ => (ParkPose.Pain, ParkEmote.Question));
                script.Then(lead, new Spark(ParkEffectKind.Psychic), new Startle(60));
                script.Then(other, new Spark(ParkEffectKind.Psychic));
                script.Beat(2000, _ => (ParkPose.Pain, ParkEmote.Exclaim));
                script.Beat(1500, _ => (ParkPose.Idle, ParkEmote.Question));
                journal = $"{me} and {them} met and shared a headache. It was twice as bad, and the whole park felt it.";
                break;
            default:
                label = _ => "Comparing sparkles";
                script.Then(lead, new Spark(ParkEffectKind.Shine));
                script.Beat(1500, _ => (ParkPose.Idle, ParkEmote.Exclaim));
                script.Then(other, new Spark(ParkEffectKind.Shine));
                script.Beat(1800, _ => (ParkPose.Pose, ParkEmote.Sparkle));
                script.Beat(1400, _ => (ParkPose.Nod, ParkEmote.Heart));
                journal = $"{me} and {them}, the park's shinies, met and compared sparkles. It was a draw.";
                break;
        }
        foreach (var a in cast) script.Then(a, new Gain(ParkNeed.Social, 12), new Gain(ParkNeed.Fun, 6));
        script.Then(lead, new Journal(journal, other.Resident.Id, ParkSocialEventKind.Bond));
        var (spotlight, priority) = meeting switch
        {
            Meeting.ShinyMirror => ($"{shiny.Resident.Name} and {plain.Resident.Name} can't work out why they look different!", 3),
            Meeting.Shinies => ($"{me} and {them}, two shinies, are comparing sparkles!", 3),
            Meeting.Elder when small.Resident.Species == 25 && big.Resident.Species == 26 => ($"{small.Resident.Name} is looking at {big.Resident.Name}…", 2),
            Meeting.Elder => ($"{small.Resident.Name} is looking up to {big.Resident.Name}", 1),
            Meeting.Cousins => ($"{me} met {them}, a cousin from far away", 2),
            Meeting.Dittos => ("Two Ditto are trying to copy each other!", 2),
            Meeting.Psyducks => ("Two Psyduck are sharing a headache!", 2),
            _ => ($"{me} and {them} are copying each other", 1)
        };
        Cast(lead, cast, 2, now, (member, i, troupe) =>
        [
            new Label(member == lead ? $"Going over to {them}" : $"{me} is coming over", "Curious"), new GoTo(places[i]), new Muster(troupe),
            .. member == lead ? new Step[] { new Spotlight(spotlight, priority, 20_000) } : [],
            new Face(cast[1 - i].Resident.Id), new Label(label(member), "Curious"), .. script.For(member)
        ]);
        return true;
    }

    // Skitty on the shore, Wailord in the water just off it: the park's oddest friendship.
    private bool PlanSkittyWailord(ParkActor actor, ParkActor other, long now)
    {
        var (skitty, wailord) = actor.Resident.Species == 300 ? (actor, other) : (other, actor);
        foreach (var shore in _map.Spots.Where(s => s.Kind == ParkSpotKind.Shore && skitty.Navigator.CanStand(s.X, s.Y) &&
                     skitty.Navigator.Connected(new(skitty.X, skitty.Y), new(s.X, s.Y))).OrderBy(_ => actor.Random.Next()))
        {
            // The nearest deep enough water in front of the shore.
            var cell = wailord.Navigator.CellNear(shore.X, shore.Y + 24);
            if (cell < 0) continue;
            var water = wailord.Navigator.Center(cell);
            if (Distance(water.X, water.Y, shore.X, shore.Y) > 60 || !wailord.Navigator.Connected(new(wailord.X, wailord.Y), water)) continue;
            ParkActor[] cast = [skitty, wailord];
            var script = new Script(cast, a => a == skitty ? Toward(new(shore.X, shore.Y), water) : Toward(water, new(shore.X, shore.Y)));
            script.Beat(1500, a => (ParkPose.Idle, a == skitty ? ParkEmote.Exclaim : ParkEmote.None));
            script.Then(wailord, new Spark(ParkEffectKind.Splash));
            script.Beat(2000, a => a == skitty ? (ParkPose.Hop, ParkEmote.Heart) : (ParkPose.Float, ParkEmote.Heart));
            script.Beat(4000, a => a == skitty ? (ParkPose.Sit, ParkEmote.Note) : (ParkPose.Float, ParkEmote.Note));
            foreach (var a in cast) script.Then(a, new Gain(ParkNeed.Social, 16));
            script.Then(actor, new Journal($"{skitty.Resident.Name} and {wailord.Resident.Name} met at the water's edge and got on wonderfully. Nobody can explain it.",
                other.Resident.Id, ParkSocialEventKind.Bond));
            var places = new[] { new ParkPoint(shore.X, shore.Y), water };
            Interrupt(wailord);
            Cast(actor, cast, 2, now, (member, i, troupe) =>
            [
                new Label(member == skitty ? "Going down to the water" : "Swimming toward the shore", "Curious"),
                new GoTo(places[i], member == wailord ? .8f : 1), new Muster(troupe),
                .. member == actor ? new Step[] { new Spotlight($"{skitty.Resident.Name} and {wailord.Resident.Name} are making friends!", 3, 25_000, "An Unlikely Friendship", Secret: true) } : [],
                new Label(member == skitty ? $"Making friends with {wailord.Resident.Name}" : $"Making friends with {skitty.Resident.Name}", "Happy"),
                .. script.For(member)
            ]);
            return true;
        }
        return false;
    }
}
