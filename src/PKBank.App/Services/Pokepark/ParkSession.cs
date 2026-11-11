using PKForge.Domain;

namespace PKBank.App.Services.Pokepark;

/// <summary>
/// The park's life: the residents, what they do while the player watches, and what they did
/// while the player was away. The session owns the simulation (<see cref="ParkLife"/>) and
/// keeps the journal, the care and the daily goals in step with it.
/// </summary>
public sealed class ParkSession(PokeparkService service)
{
    private readonly Lock _gate = new();
    private ParkLife _life = new(ParkMap.Park, []);
    private IReadOnlyList<ParkPokemon> _residents = [];
    private ParkGoalsState _goals = ParkGoals.For(DateOnly.FromDateTime(DateTime.Now), 0, false);
    private DateTimeOffset _lastSimulation = DateTimeOffset.Now;
    private ParkSocialState _social = ParkSocialState.Empty(DateTimeOffset.Now);
    private long _lastSocialTick;
    private readonly Queue<string> _notices = new();

    /// <summary>The simulation, for the renderer and the panels.</summary>
    public ParkLife Life => _life;

    /// <summary>The residents, as the roster records them.</summary>
    public IReadOnlyList<ParkPokemon> Residents
    {
        get { lock (_gate) return _residents; }
    }

    /// <summary>Short notices for the top of the view (a goal done, a visitor arrived).</summary>
    public string? TakeNotice()
    {
        lock (_gate) return _notices.Count > 0 ? _notices.Dequeue() : null;
    }

    public void Notify(string text)
    {
        lock (_gate) _notices.Enqueue(text);
    }

    /// <summary>Puts the roster into the park and restores the care the residents had.</summary>
    public void Load(IReadOnlyList<ParkPokemon> residents)
    {
        lock (_gate)
        {
            _residents = residents;
            _life = new ParkLife(ParkMap.Park, LifeResidents(residents));
            RestoreCare();
            // The residents know how they get on with each other: the relationships the park
            // remembers are the ones the simulation plays on.
            _social = service.Social ?? ParkSocialState.Empty(DateTimeOffset.Now);
            _life.Relation = RelationBetween;
            var fighter = residents.Any(r => HabitatCatalog.TypesFor(r.Species, r.Form).Contains(ParkType.Fighting));
            _goals = ParkGoals.Load(service.Goals, DateOnly.FromDateTime(DateTime.Now), residents.Count, fighter);
            service.SaveGoals(ParkGoals.Save(_goals));
        }
    }

    /// <summary>How two residents get on (friendship and rivalry), from the park's memory.</summary>
    private (int Friendship, int Rivalry) RelationBetween(string first, string second)
    {
        lock (_gate)
        {
            var key = ParkRelationship.KeyFor(first, second);
            var relationship = _social.Relationships.FirstOrDefault(r => r.Key == key);
            return relationship is null ? (0, 0) : (relationship.Friendship, relationship.Rivalry);
        }
    }

    /// <summary>The relationships between the residents, for the bonds panel.</summary>
    public IReadOnlyList<ParkRelationship> Relationships
    {
        get { lock (_gate) return [.. _social.Relationships]; }
    }

    /// <summary>How a resident feels about another one.</summary>
    public ParkRelationship? BondOf(string first, string second)
    {
        lock (_gate)
        {
            var key = ParkRelationship.KeyFor(first, second);
            return _social.Relationships.FirstOrDefault(r => r.Key == key);
        }
    }

    /// <summary>The residents as the simulation knows them.</summary>
    public static IEnumerable<ParkLifeResident> LifeResidents(IReadOnlyList<ParkPokemon> residents) => residents.Select(mon =>
        new ParkLifeResident(mon.Id, mon.Name, mon.Species, HabitatCatalog.TypesFor(mon.Species, mon.Form),
            ParkLife.PersonalityFor(mon.Id), null, HabitatCatalog.SpeedFor(mon.Species, mon.Form), mon.Shiny, mon.Form));

    /// <summary>Runs the park up to now: the day's light, the weather, the residents' steps.</summary>
    public void Tick(long now)
    {
        var local = DateTime.Now;
        _life.Phase = ParkCare.PhaseAt(local);
        _life.Weather = ParkCare.WeatherAt(local);
        _life.Today = DateOnly.FromDateTime(local);
        _life.Advance(now);
        foreach (var moment in _life.TakeMoments())
        {
            service.AppendJournal(KindOf(moment.Kind), TextOf(moment), [moment.FirstId, moment.SecondId]);
            Witness(moment);
        }
        SocialTick(now);
    }

    /// <summary>The relationships move on their own: every half minute the residents meet,
    /// quarrel or make up, and the park remembers how they get on.</summary>
    private void SocialTick(long now)
    {
        if (now - _lastSocialTick < 30_000) return;
        _lastSocialTick = now;
        var residents = Residents;
        if (residents.Count < 2) return;
        var social = residents.Select(r => new ParkSocialPokemon(r.Id, r.Name, r.Species,
            HabitatCatalog.TypesFor(r.Species, r.Form), ParkLife.PersonalityFor(r.Id))).ToArray();
        lock (_gate)
        {
            var (state, events) = ParkSocialSimulation.Simulate(_social, social, DateTimeOffset.Now);
            _social = state;
            foreach (var socialEvent in events)
                service.AppendJournal(KindOf(socialEvent.Kind), socialEvent.Text, socialEvent.PokemonIds);
            service.SaveSocial(_social);
        }
    }

    /// <summary>A moment between two residents changes how they get on.</summary>
    private void Witness(ParkMoment moment)
    {
        if (moment.SecondId.Length == 0) return;
        lock (_gate)
        {
            var (state, _) = ParkSocialSimulation.Witness(_social, moment, moment.FirstId, moment.SecondId, DateTimeOffset.Now);
            _social = state;
            service.SaveSocial(_social);
        }
    }

    /// <summary>The player spends a moment with a resident.</summary>
    public ParkReply Interact(string id, ParkCommand command)
    {
        var reply = _life.Command(id, command);
        if (reply.Accepted)
        {
            Record(command switch
            {
                ParkCommand.Talk => ParkDeed.Talked,
                ParkCommand.Snack => ParkDeed.LovedBerry,
                _ => ParkDeed.Pat,
            }, id);
            if (reply.Line is { Length: > 0 } line) service.AppendJournal(ParkJournalEventKind.Affection, line, [id]);
        }
        return reply;
    }

    /// <summary>Pets a resident (the smallest of the player's actions, and the first goal).</summary>
    public ParkReply Pet(string id)
    {
        var reply = _life.Pet(id);
        if (reply.Accepted) Record(ParkDeed.Pat, id);
        return reply;
    }

    /// <summary>Counts a deed towards today's goals and rewards a full day.</summary>
    public void Record(ParkDeed deed, string id)
    {
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (_goals.Day != today)
            {
                var fighter = _residents.Any(r => HabitatCatalog.TypesFor(r.Species, r.Form).Contains(ParkType.Fighting));
                _goals = ParkGoals.For(today, _residents.Count, fighter);
            }
            _goals = ParkGoals.Record(_goals, deed, id, out var completed);
            foreach (var goal in completed)
                Notify($"Goal done: {goal.Text}");
            if (_goals.AllDone && !_goals.Rewarded)
            {
                var reward = ParkGoals.RewardFor(today);
                _goals = _goals with { Rewarded = true };
                var treasures = service.Treasures.ToList();
                treasures.Add(reward);
                service.SaveTreasures(treasures);
                Notify($"All the goals done — you found {reward.Name}!");
                service.AppendJournal(ParkJournalEventKind.Discovery, $"You found {reward.Name}!", []);
            }
            service.SaveGoals(ParkGoals.Save(_goals));
        }
    }

    /// <summary>The goals of today, for the panel.</summary>
    public ParkGoalsState Goals
    {
        get { lock (_gate) return _goals; }
    }

    /// <summary>Saves the residents' care, so they keep their needs between visits.</summary>
    public void SaveCare() => service.SaveCare(_life.Snapshot(DateTimeOffset.Now));

    private void RestoreCare()
    {
        var saved = service.CareStates;
        if (saved.Count == 0) return;
        _life.Restore(saved, DateTimeOffset.Now);
    }

    /// <summary>
    /// What happened while the app was closed: a short account of the park's life, written
    /// into the journal as if the player had been there.
    /// </summary>
    public void RunOfflineLife()
    {
        var residents = Residents;
        if (residents.Count == 0) return;
        var now = DateTimeOffset.Now;
        var away = now - _lastSimulation;
        if (away < ParkAbsence.Minimum) return;
        var before = service.CareStates.ToDictionary(c => c.Id, c => c);
        var offline = residents.Select(r => new ParkOfflineResident(r.Id, r.Name, r.Species,
            ParkEnvironmentId.Commons)).ToArray();
        var simulator = new ParkOfflineSimulator();
        var result = simulator.Simulate(_lastSimulation, now, offline, Random.Shared);
        foreach (var entry in result.Events)
            service.AppendJournal(entry.Kind, entry.Text, entry.ResidentIds, entry.Habitat);
        _lastSimulation = now;
        // The stories of the absence, in the player's own words.
        var stories = ParkAbsence.Tell(LifeResidents(residents).ToArray(), before,
            _life.Snapshot(now).ToDictionary(c => c.Id, c => c), now.DateTime.Add(-away), now.DateTime, Random.Shared, service.Wilds);
        foreach (var story in stories.Take(3))
            service.AppendJournal(ParkJournalEventKind.Activity, story.Text, []);
    }

    /// <summary>Notes the moment the park was left, so the absence can be told next time.</summary>
    public void MarkDeparture() => _lastSimulation = DateTimeOffset.Now;

    private static ParkJournalEventKind KindOf(ParkSocialEventKind kind) => kind switch
    {
        ParkSocialEventKind.Conflict => ParkJournalEventKind.Conflict,
        ParkSocialEventKind.Deescalation => ParkJournalEventKind.Reconciliation,
        ParkSocialEventKind.Bond or ParkSocialEventKind.Crush => ParkJournalEventKind.Friendship,
        ParkSocialEventKind.Help => ParkJournalEventKind.Friendship,
        _ => ParkJournalEventKind.Activity,
    };

    private static string TextOf(ParkMoment moment) => moment.Text is { Length: > 0 } text
        ? text
        : $"{moment.FirstId} and {moment.SecondId} spent time together.";
}
