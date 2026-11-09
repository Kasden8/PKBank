namespace PKForge.Domain;

/// <summary>
/// The daycare, run by Pokémon: Blissey keeps the post by the daycare's door, Audino makes the
/// rounds among the guests. Guests are residents the player left in their care; they play
/// together in front of the house, are fed when they get hungry, and two that get on may leave an
/// egg, which Blissey finds and holds until the player comes for it.
/// </summary>
public sealed partial class ParkLife
{
    public const string KeeperPrefix = VisitorPrefix + "keeper:";
    public static bool IsKeeper(string id) => id.StartsWith(KeeperPrefix, StringComparison.Ordinal);

    // Blissey's post: just beside the daycare's door.
    private ParkPoint KeeperPost
    {
        get
        {
            if (_map.Spots.FirstOrDefault(s => s.Kind == ParkSpotKind.Daycare) is not { } door) return new(-1, -1);
            var land = ParkNavigator.For(_map, ParkTraversal.Land);
            foreach (var (dx, dy) in new[] { (19f, 7f), (24f, 10f), (-19f, 7f), (0f, 14f), (28f, 14f) })
                if (land.CanStand(door.X + dx, door.Y + dy)) return new(door.X + dx, door.Y + dy);
            return new(door.X, door.Y);
        }
    }
    private const string BlisseyId = KeeperPrefix + "blissey", AudinoId = KeeperPrefix + "audino";

    internal sealed record FindEgg : Step;

    /// <summary>The daycare is open on this map: the keepers are about.</summary>
    public bool DaycareOpen { get; set; }

    /// <summary>Residents staying at the daycare.</summary>
    public IReadOnlyCollection<string> DaycareGuests { get; set; } = [];

    /// <summary>Whether two guests could have an egg together (egg groups, genders, Ditto), from the game data.</summary>
    public Func<string, string, bool>? CanBreed { get; set; }

    /// <summary>The egg Blissey is keeping for the player, and its two parents.</summary>
    public (string First, string Second)? Egg { get; private set; }

    private long _nextEggCheck;
    private (string First, string Second)? _eggFound;

    public bool AtDaycare(string id) => DaycareGuests.Contains(id);

    /// <summary>An egg kept from an earlier visit: Blissey is holding it again.</summary>
    public void KeepEgg((string First, string Second) egg) => Egg = egg;

    /// <summary>The player takes the egg from Blissey.</summary>
    public void TakeEgg() { lock (_gate) TakeEggCore(); }

    private void TakeEggCore()
    {
        Egg = null;
        if (_actors.TryGetValue(BlisseyId, out var blissey)) blissey.Prop = ParkProp.None;
    }

    private void UpdateDaycare(long now)
    {
        if (!DaycareOpen)
        {
            foreach (var id in new[] { BlisseyId, AudinoId }) if (_actors.ContainsKey(id) && !_leaving.Contains(id)) _leaving.Add(id);
            return;
        }
        Hire(BlisseyId, "Blissey", 242, ParkPersonality.Gentle);
        Hire(AudinoId, "Audino", 531, ParkPersonality.Helpful);

        // Guests never go hungry: the keepers feed them.
        foreach (var id in DaycareGuests)
            if (_actors.TryGetValue(id, out var guest) && guest.Care.Hunger < 35) guest.Care = guest.Care.Add(ParkNeed.Hunger, 30);

        // Two guests who get on may have an egg: about once an hour for the same species, half that otherwise.
        if (now < _nextEggCheck || Egg is not null || _eggFound is not null || CanBreed is null) return;
        _nextEggCheck = now + 60_000;
        var guests = DaycareGuests.Where(_actors.ContainsKey).Select(id => _actors[id]).ToArray();
        for (var i = 0; i < guests.Length; i++)
        for (var j = i + 1; j < guests.Length; j++)
        {
            ParkActor a = guests[i], b = guests[j];
            if (ParkLore.Feeling(a.Resident.Species, b.Resident.Species, 0) is ParkPairKind.Rivals or ParkPairKind.Fears) continue;
            if (_random.Next(a.Resident.Species == b.Resident.Species ? 60 : 120) != 0 || !CanBreed(a.Resident.Id, b.Resident.Id)) continue;
            _eggFound = (a.Resident.Id, b.Resident.Id);
            return;
        }
    }

    // A keeper arrives from the cabin the first time the daycare opens.
    private void Hire(string id, string name, int species, ParkPersonality personality)
    {
        if (_actors.ContainsKey(id)) return;
        var resident = new ParkLifeResident(id, name, species, [ParkType.Normal], personality);
        var navigator = ParkNavigator.For(_map, resident.Traversal);
        if (!navigator.CanStand(KeeperPost.X, KeeperPost.Y)) return;
        _actors[id] = new ParkActor(resident, navigator, new Random(_random.Next()), KeeperPost.X, KeeperPost.Y) { Facing = 0, LeaveAt = long.MaxValue };
    }

    // What the keepers do: Blissey keeps the post and goes for the egg when there is one; Audino
    // checks on each guest in turn. At night they sleep where they stand.
    private bool PlanKeeper(ParkActor keeper)
    {
        if (!IsKeeper(keeper.Resident.Id)) return false;
        if (Phase == ParkDayPhase.Night)
        {
            Add(keeper, new Label("Asleep at the daycare post", "Sleepy"), new Act(ParkPose.Sleep, 60_000));
            return true;
        }
        var guests = DaycareGuests.Where(_actors.ContainsKey).Select(id => _actors[id]).Where(g => !g.Indoors && !g.InGame).ToArray();
        if (keeper.Resident.Id == BlisseyId)
        {
            if (_eggFound is { } parents && Egg is null)
            {
                _eggFound = null;
                Add(keeper, new Label("Found something behind the cabin", "Surprised"), new GoTo(new(KeeperPost.X + 22, KeeperPost.Y + 4)),
                    new Act(ParkPose.Charge, 1600, 4, ParkEmote.Exclaim), new FindEgg(), new GoTo(KeeperPost),
                    new Label("Keeping an egg for you", "Joyful"), new Act(ParkPose.Idle, 20_000, 0, ParkEmote.Heart, ParkProp.Egg));
                keeper.EggParents = parents;
                return true;
            }
            if (Egg is null && guests.Length > 0 && keeper.Random.Next(4) == 0)
            {
                var guest = guests[keeper.Random.Next(guests.Length)];
                if (Beside(keeper, guest) is { } spot)
                {
                    Add(keeper, new Label($"Fussing over {guest.Resident.Name}", "Gentle"), new GoTo(spot), new Face(guest.Resident.Id),
                        new Act(ParkPose.Nod, 1500, Emote: ParkEmote.Heart), new GoTo(KeeperPost));
                    return true;
                }
            }
            Add(keeper, new Label(Egg is null ? "Minding the daycare" : "Keeping an egg for you", "Peaceful"), new GoTo(KeeperPost),
                new Act(ParkPose.Idle, keeper.Random.Next(8000, 16000), 0, Egg is null ? ParkEmote.None : ParkEmote.Heart, Egg is null ? ParkProp.None : ParkProp.Egg));
            return true;
        }
        // Audino: rounds among the guests, or a stroll near the cabin when there are none.
        if (guests.Length > 0 && Beside(keeper, guests[keeper.Random.Next(guests.Length)]) is { } beside)
        {
            var guest = guests.OrderBy(g => Distance(g.X, g.Y, beside.X, beside.Y)).First();
            Add(keeper, new Label($"Checking on {guest.Resident.Name}", "Helpful"), new GoTo(beside), new Face(guest.Resident.Id),
                new Act(ParkPose.Nod, 1800, Emote: ParkEmote.Note), new Act(ParkPose.Idle, keeper.Random.Next(3000, 6000)));
            return true;
        }
        var near = new ParkPoint(KeeperPost.X - 30 + keeper.Random.Next(60), KeeperPost.Y + keeper.Random.Next(20));
        Add(keeper, new Label("Tidying up the daycare", "Helpful"));
        if (keeper.Navigator.CanStand(near.X, near.Y)) Add(keeper, new GoTo(near));
        Add(keeper, new Act(ParkPose.Idle, keeper.Random.Next(6000, 12000), Emote: ParkEmote.Note));
        return true;
    }

    // A guest's day at the daycare: playing with the other guests near the cabin.
    private bool PlanGuest(ParkActor actor)
    {
        if (!AtDaycare(actor.Resident.Id) || actor.Random.Next(3) == 0) return false;
        var friend = DaycareGuests.Where(id => id != actor.Resident.Id && _actors.TryGetValue(id, out var o) && !o.IsAsleep && !o.Indoors)
            .Select(id => _actors[id]).FirstOrDefault();
        var near = new ParkPoint(KeeperPost.X - 50 + actor.Random.Next(70), KeeperPost.Y + 10 + actor.Random.Next(30));
        if (!actor.Navigator.CanStand(near.X, near.Y) || !actor.Navigator.Connected(new(actor.X, actor.Y), near)) return false;
        Add(actor, new Label(friend is null ? "Playing at the daycare" : $"Playing with {friend.Resident.Name} at the daycare", "Playful"), new GoTo(near));
        if (friend is not null) Add(actor, new Face(friend.Resident.Id));
        Add(actor, new Act(ParkPose.Hop, 1500, Emote: ParkEmote.Note), new Act(ParkPose.Idle, actor.Random.Next(4000, 9000)),
            new Gain(ParkNeed.Fun, 8), new Gain(ParkNeed.Social, 10));
        if (friend is not null) Add(actor, new Share(ParkSocialEventKind.Play, friend.Resident.Id));
        return true;
    }

    private bool RunDaycareStep(ParkActor actor, Step step)
    {
        if (step is not FindEgg) return false;
        if (actor.EggParents is { } parents && _actors.TryGetValue(parents.First, out var first) && _actors.TryGetValue(parents.Second, out var second))
        {
            Egg = parents;
            actor.Prop = ParkProp.Egg;
            Burst(ParkEffectKind.Egg, actor);
            _moments.Add(new ParkMoment(ParkSocialEventKind.Bond, parents.First, parents.Second,
                $"Blissey found an egg behind the cabin, right where {first.Resident.Name} and {second.Resident.Name} like to play."));
        }
        actor.EggParents = null;
        return true;
    }
}
