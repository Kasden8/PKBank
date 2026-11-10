namespace PKForge.Domain;

/// <summary>
/// Evolution in the park. Now and then a wild visitor that can evolve does, glowing as it
/// changes into its next form for good. Once in a long while a resident starts to evolve too,
/// and stops at the last moment, the way it does in the games when B is pressed: a resident is a
/// Pokémon from the player's save or Bank, and the park never changes one.
/// </summary>
public sealed partial class ParkLife
{
    private long _nextEvolutionTry = 2 * 60_000;
    private DateOnly? _residentEvolutionDay;

    private const int VisitorEvolutionOdds = 25;   // per visitor that can evolve, per minute
    private const int ResidentEvolutionOdds = 240; // per minute, at most once a day


    private void UpdateEvolution(long now)
    {
        if (now < _nextEvolutionTry) return;
        _nextEvolutionTry = now + 60_000;
        if (_actors.Values.Any(a => a.Activity == Evolving)) return;

        foreach (var visitor in _actors.Values.Where(a => IsVisitor(a.Resident.Id) && Ready(a)).ToArray())
            if (WildEvolution(visitor.Resident) is { } into && _random.Next(VisitorEvolutionOdds) == 0)
            {
                StartEvolving(visitor, into, stops: false);
                return;
            }

        if (_residentEvolutionDay == Today) return;
        var residents = _actors.Values.Where(a => !IsVisitor(a.Resident.Id) && Ready(a) && ParkSpecies.EvolvesInto(a.Resident.Species).Count > 0).ToArray();
        // The dice only roll when someone could evolve, so a park without one plays out as before.
        if (residents.Length == 0 || _random.Next(ResidentEvolutionOdds) != 0) return;
        var resident = residents[_random.Next(residents.Length)];
        var forms = ParkSpecies.EvolvesInto(resident.Resident.Species);
        _residentEvolutionDay = Today;
        StartEvolving(resident, resident.Resident with { Species = forms[_random.Next(forms.Count)], Form = 0 }, stops: true);
    }

    internal const string Evolving = "Evolving";

    // Awake, out in the open and not with anyone: whatever it was about to do can wait.
    private bool Ready(ParkActor actor) => Free(actor) && actor.Activity is not (Evolving or "Scene" or "Meet");

    // What a visitor evolves into, when the park knows that species (its name and types).
    private ParkLifeResident? WildEvolution(ParkLifeResident visitor)
    {
        var wilds = DayWild.Concat(NightWild).Concat(PlaceWild.Values.SelectMany(p => p.Day.Concat(p.Night)));
        foreach (var species in ParkSpecies.EvolvesInto(visitor.Species))
            if (wilds.FirstOrDefault(w => w.Species == species) is { } wild)
                return visitor with { Name = wild.Name, Species = wild.Species, Types = wild.Types, Form = 0 };
        return null;
    }

    private void StartEvolving(ParkActor actor, ParkLifeResident into, bool stops)
    {
        ClearPlan(actor);
        var name = actor.Resident.Name;
        var wild = IsVisitor(actor.Resident.Id) ? "The wild " : "";
        var text = stops
            ? $"Huh? {name} stopped evolving! It looks rather pleased with itself."
            : $"{wild}{name} evolved into {into.Name}! Everyone came to look.";
        _highlights.RemoveAll(h => h.ActorId == actor.Resident.Id);
        var highlight = new ParkHighlight(actor.Resident.Id, text, 3, Now, Now + 60_000,
            stops ? $"{name} stopped evolving" : $"{name} evolved into {into.Name}!")
        {
            Cast = [actor.Resident.Id],
            EvolvesInto = into.Species,
            EvolvesIntoName = into.Name,
            StopsEvolving = stops,
            Before = $"What? {wild}{name} is evolving!",
        };
        _highlights.Add(highlight);
        _newHighlights.Enqueue(highlight);
        // In the park: it glows and charges while the page plays the announcement, then it is
        // its new self (a visitor) or itself again, puzzled (a resident).
        Add(actor, new Label(Evolving, "Excited"), new Spark(ParkEffectKind.Shine), new Act(ParkPose.Charge, 1800, Emote: ParkEmote.Sparkle),
            new Spark(ParkEffectKind.Shine), new Act(ParkPose.Charge, 1600), new Spark(ParkEffectKind.Shine));
        if (stops)
            Add(actor, new Label("Wondering why it stopped evolving", "Confused"), new Act(ParkPose.LookUp, 1800, Emote: ParkEmote.Question),
                new Journal($"{name} started to evolve, glowing all over, and then stopped. Nobody pressed anything."),
                new Act(ParkPose.Hop, 900, Emote: ParkEmote.Note));
        else
        {
            // A visitor is its new self straight away, while the announcement covers the screen:
            // nothing that interrupts it later can undo what was announced.
            actor.Resident = into;
            _moments.Add(new ParkMoment(ParkSocialEventKind.Rest, actor.Resident.Id, "",
                $"{wild}{name} evolved into {into.Name} right in the middle of the park. Everyone came to look."));
            Add(actor, new Label("Showing off its new form", "Proud"), new Act(ParkPose.Pose, 1500, Emote: ParkEmote.Sparkle));
        }
    }
}
