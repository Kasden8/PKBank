namespace PKForge.Domain;

/// <summary>Something worth watching that has just started somewhere in the park: Snorlax asleep
/// across the bridge, Lapras ferrying someone, a Magneton forming… The page tells the player
/// (and takes the camera there in spectator mode). Priority 1 is a nice moment, 2 a good one, 3 a
/// rare one not to miss. A named <paramref name="Event"/> is one of the park's events: the page
/// stops everything to announce it, a secret one even more so.</summary>
public sealed record ParkHighlight(string ActorId, string Text, int Priority, long Start, long Until, string? Event = null, bool Secret = false)
{
    /// <summary>Who is in it: the Pokémon, then whoever it is with (its partner, its scene), at most three.</summary>
    public IReadOnlyList<string> Cast { get; init; } = [ActorId];
    /// <summary>An evolution: the species it is evolving into (0: not one).</summary>
    public int EvolvesInto { get; init; }
    /// <summary>The name of what it evolves into.</summary>
    public string? EvolvesIntoName { get; init; }
    /// <summary>A resident: it stops before the end, as when B is pressed in the games.</summary>
    public bool StopsEvolving { get; init; }
    /// <summary>What the announcement says before it shows how it ends ("What? Eevee is evolving!").</summary>
    public string? Before { get; init; }
}

public sealed partial class ParkLife
{
    /// <summary>Puts the park's eye on this one, for that long.</summary>
    internal sealed record Spotlight(string Text, int Priority, int Milliseconds, string? Event = null, bool Secret = false) : Step;

    private readonly List<ParkHighlight> _highlights = new();
    private readonly Queue<ParkHighlight> _newHighlights = new();

    /// <summary>What is worth watching right now, the most worth it first.</summary>
    public IReadOnlyList<ParkHighlight> Highlights { get { lock (_gate) return HighlightsNow(); } }

    private ParkHighlight[] HighlightsNow() =>
        _highlights.Where(h => h.Until > Now && _actors.ContainsKey(h.ActorId)).OrderByDescending(h => h.Priority).ThenByDescending(h => h.Start).ToArray();

    /// <summary>The highlights that started since the last call.</summary>
    public IReadOnlyList<ParkHighlight> TakeHighlights() { lock (_gate) return TakeHighlightsCore(); }

    private IReadOnlyList<ParkHighlight> TakeHighlightsCore()
    {
        var fresh = _newHighlights.Where(h => h.Until > Now).ToArray();
        _newHighlights.Clear();
        return fresh;
    }

    private void Highlight(ParkActor actor, string text, int priority, int milliseconds, string? name = null, bool secret = false)
    {
        var cast = new[] { actor.Resident.Id }.Concat(actor.Troupe?.Ids ?? []).Append(actor.PartnerId)
            .OfType<string>().Distinct().Where(_actors.ContainsKey).Take(3).ToArray();
        var highlight = new ParkHighlight(actor.Resident.Id, text, priority, Now, Now + milliseconds, name, secret) { Cast = cast };
        _highlights.RemoveAll(h => h.Until <= Now || h.ActorId == actor.Resident.Id);
        _highlights.Add(highlight);
        _newHighlights.Enqueue(highlight);
        while (_newHighlights.Count > 8) _newHighlights.Dequeue();
    }

    private bool RunHighlightStep(ParkActor actor, Step step)
    {
        if (step is not Spotlight spotlight) return false;
        Highlight(actor, spotlight.Text, spotlight.Priority, spotlight.Milliseconds, spotlight.Event, spotlight.Secret);
        return true;
    }
}
