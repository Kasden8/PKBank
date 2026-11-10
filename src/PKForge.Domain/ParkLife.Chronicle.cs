using System.Text.Json;

namespace PKForge.Domain;

/// <summary>What the park remembers from one day to the next, so its stories go on: how many
/// times Lapras has carried each passenger, how often a troublemaker has picked on the same
/// victim, how many naps Snorlax has had on the bridge, how far Bidoof's dam has got.</summary>
public sealed class ParkChronicle
{
    public Dictionary<string, int> Counts { get; set; } = new();
    /// <summary>Sticks in Bidoof's dam (0 to <see cref="ParkLife.DamFull"/>).</summary>
    public int Dam { get; set; }

    public int Count(string key) => Counts.GetValueOrDefault(key);

    public int Add(string key) => Counts[key] = Count(key) + 1;

    public void Reset(string key) => Counts.Remove(key);

    public string Save() => JsonSerializer.Serialize(this);

    public static ParkChronicle Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<ParkChronicle>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }
}

public sealed partial class ParkLife
{
    /// <summary>The park's memory across days (the page saves and restores it).</summary>
    public ParkChronicle Chronicle { get; set; } = new();

    /// <summary>The chronicle as JSON, to save (taken under the park's lock).</summary>
    public string SaveChronicle() { lock (_gate) return Chronicle.Save(); }

    public const int DamFull = 12;

    /// <summary>Counts a story beat; on some counts the story takes a turn.</summary>
    internal sealed record Tally(string Key, string OtherId = "") : Step;

    /// <summary>One more stick on Bidoof's dam.</summary>
    internal sealed record AddStick : Step;

    /// <summary>Where Bidoof builds its dam: the home lake's edge.</summary>
    public ParkSpot? DamSpot => _map.Spots.FirstOrDefault(s => s.Kind == ParkSpotKind.Shore && _map.RegionAt(s.X, s.Y).Id == "home");

    private bool RunChronicleStep(ParkActor actor, Step step)
    {
        switch (step)
        {
            case Tally tally:
                var count = Chronicle.Add(tally.Key);
                StoryTurn(actor, tally.Key, count, tally.OtherId);
                return true;
            case AddStick:
                if (Chronicle.Dam >= DamFull) return true;
                Chronicle.Dam++;
                var name = actor.Resident.Name;
                var line = Chronicle.Dam switch
                {
                    1 => $"{name} dragged a stick to the edge of the lake and left it there, very deliberately. It seems to be building something.",
                    4 => $"{name}'s pile of sticks by the lake is starting to look like a dam.",
                    8 => $"{name}'s dam is coming along. Nobody asked for a dam.",
                    DamFull => $"{name} finished its dam. Nobody asked for a dam, and everyone has been to admire it anyway.",
                    _ => null
                };
                if (line is not null) _moments.Add(new ParkMoment(ParkSocialEventKind.Rest, actor.Resident.Id, "", line));
                if (Chronicle.Dam == DamFull) Highlight(actor, $"{name} finished its dam!", 3, 60_000, "The Great Dam");
                return true;
        }
        return false;
    }

    // The turns the stories take.
    private void StoryTurn(ParkActor actor, string key, int count, string otherId)
    {
        var other = _actors.GetValueOrDefault(otherId);
        string me = actor.Resident.Name, them = other?.Resident.Name ?? "";
        if (key.StartsWith("ferry:") && count == 3 && other is not null)
        {
            // A regular: they are friends now, and the journal says so.
            _moments.Add(new ParkMoment(ParkSocialEventKind.Bond, actor.Resident.Id, otherId,
                $"{them} has ridden across the water on {me} three times now. It waits at the shore for {me} like an old friend."));
        }
        else if (key.StartsWith("tricks:") && count >= 5 && other is not null)
        {
            // Enough is enough: next thing it does, the victim gets its own back.
            Chronicle.Reset(key);
            other.Revenge = actor.Resident.Id;
        }
        else if (key.StartsWith("bridge:") && count == 5)
        {
            _moments.Add(new ParkMoment(ParkSocialEventKind.Rest, actor.Resident.Id, "",
                $"{me} has fallen asleep across the way five times now. The others have started calling it {me}'s bridge."));
        }
    }

    // The victim's turn: it sneaks up on the troublemaker and trips it.
    private bool PlanRevenge(ParkActor actor)
    {
        if (actor.Revenge is not { } trollId) return false;
        actor.Revenge = null;
        if (!_actors.TryGetValue(trollId, out var troll) || troll.Busy || troll.IsAsleep || troll.Indoors || Beside(actor, troll) is not { } spot) return false;
        string me = actor.Resident.Name, them = troll.Resident.Name;
        Interrupt(troll);
        actor.PartnerId = trollId; troll.PartnerId = actor.Resident.Id;
        Add(troll, new Act(ParkPose.Idle, 20_000), new Release());
        Add(actor, new Label($"Getting its own back on {them}", "Mischievous"), new GoTo(spot, 1.2f), new Face(trollId),
            new Spotlight($"{me} is getting its own back on {them}!", 2, 15_000), new Act(ParkPose.Attack, 700), new Spark(ParkEffectKind.Scare),
            new Engage(trollId, [new Act(ParkPose.Trip, 1100, Emote: ParkEmote.Exclaim), new Label($"Tripped by {me}, for once", "Embarrassed"),
                new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Sweat)]),
            new Act(ParkPose.Hop, 1400, Emote: ParkEmote.Note),
            new Journal($"After the fifth time, {me} finally got its own back on {them}. {them} did not see it coming.", trollId, ParkSocialEventKind.Conflict),
            new Release());
        return true;
    }

    // ── Bidoof's dam ─────────────────────────────────────────────────────────

    private bool PlanDamWork(ParkActor bidoof)
    {
        if (Chronicle.Dam >= DamFull || DamSpot is not { } dam || Wet) return false;
        var random = bidoof.Random;
        if (Spot(bidoof, ParkSpotKind.Nap) is not { } trees || !bidoof.Navigator.Connected(new(bidoof.X, bidoof.Y), new(dam.X, dam.Y))) return false;
        Add(bidoof, new Label("Fetching a stick", "Determined"), new GoTo(new(trees.X, trees.Y)), new Act(ParkPose.Pull, 1600), new Spark(ParkEffectKind.Rustle),
            new Label("Carrying a stick to its dam", "Determined"), new GoTo(new(dam.X - 10, dam.Y), .8f), new Act(ParkPose.Pull, 1400, dam.Facing),
            new AddStick(), new Act(ParkPose.Idle, 1500, dam.Facing, random.Next(2) == 0 ? ParkEmote.Sparkle : ParkEmote.None));
        return true;
    }

    // A storm washes the dam away; Bidoof starts again without a word.
    private void StormDam()
    {
        if (Weather != ParkWeather.Storm || Chronicle.Dam == 0 || Now % 60_000 >= TickMilliseconds) return;
        if (_actors.Values.FirstOrDefault(a => a.Resident.Species is 399 or 400) is not { } bidoof) return;
        Chronicle.Dam = 0;
        _moments.Add(new ParkMoment(ParkSocialEventKind.Rest, bidoof.Resident.Id, "",
            $"The storm washed {bidoof.Resident.Name}'s dam away. {bidoof.Resident.Name} will start again tomorrow, without a word."));
    }
}
