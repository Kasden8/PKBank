namespace PKForge.Domain;

public enum ParkGameKind { HideAndSeek, Race, Dig, Brush, Spar }

public enum ParkGameStage { Starting, Countdown, Playing, Over }

/// <summary>A game the player is playing with residents. One at a time; the page reads it every
/// frame for its banner and ends it once it has shown the result.</summary>
public sealed class ParkGame
{
    public ParkGameKind Kind { get; init; }
    /// <summary>The resident the player asked to play.</summary>
    public string HostId { get; init; } = "";
    /// <summary>Everyone playing: the host, and the other racers in a race.</summary>
    public IReadOnlyList<string> Players { get; internal set; } = [];
    public ParkGameStage Stage { get; internal set; }
    public long StageSince { get; internal set; }
    /// <summary>When it ends on its own (the seeker gives up, the brushed one gets bored).</summary>
    public long Deadline { get; internal set; }
    /// <summary>Hide and seek: guesses so far.</summary>
    public int Tries { get; internal set; }
    /// <summary>Brushing: 0 to 1.</summary>
    public float Progress { get; internal set; }
    /// <summary>Race: arrival order.</summary>
    public IReadOnlyList<string> Finished => _finished;
    internal readonly List<string> _finished = new();
    /// <summary>Where the hider hides (hide and seek).</summary>
    public ParkPoint Spot { get; internal set; }
    internal bool Zapped { get; set; }
    /// <summary>Sparring: the round (0–7) and strikes blocked.</summary>
    public int Round { get; internal set; }
    public int Blocked { get; internal set; }
    /// <summary>Sparring: the master is striking now; block!</summary>
    public bool Striking { get; internal set; }
    internal long WindupAt { get; set; }
    internal long StrikeAt { get; set; }
    internal bool Winding { get; set; }
    internal bool Answered { get; set; }

    /// <summary>The line to show once it is over.</summary>
    public string? Result { get; internal set; }
    /// <summary>What the dig turned up, for the collection.</summary>
    public ParkTreasure? Treasure { get; internal set; }
}

/// <summary>Something dug up in the park.</summary>
public sealed record ParkTreasure(string Name, int Rarity);

/// <summary>Games with the residents: hide and seek, races, digging for treasure, brushing.
/// Who agrees to play depends on its temperament, like everything else.</summary>
public sealed partial class ParkLife
{
    public const int SeekMilliseconds = 90_000, BrushMilliseconds = 60_000, RaceMilliseconds = 60_000, SparRounds = 8;
    // The race runs along the home park's path, from its west end to the flag.
    private ParkPoint RaceStart => _map.AtHome(24, 100);
    private ParkPoint RaceFinish => _map.AtHome(296, 128);

    internal sealed record Hide : Step;
    internal sealed record WaitForGo : Step;
    internal sealed record Finish : Step;
    internal sealed record Unearth : Step;

    /// <summary>The game being played, if any.</summary>
    public ParkGame? Game { get; private set; }

    /// <summary>Asks a resident to play a game (not Dig, see <see cref="StartDig"/>). It may say no.</summary>
    public ParkReply StartGame(ParkGameKind kind, string hostId) { lock (_gate) return StartGameCore(kind, hostId); }

    private ParkReply StartGameCore(ParkGameKind kind, string hostId)
    {
        if (Game is not null || IsVisitor(hostId) || !_actors.TryGetValue(hostId, out var host)) return ParkReply.None;
        var temperament = host.Resident.Temperament;
        var appeal = (kind, temperament) switch
        {
            (ParkGameKind.HideAndSeek, ParkTemperament.Playful or ParkTemperament.Mischievous or ParkTemperament.Timid) => .25f,
            (ParkGameKind.Race, ParkTemperament.Playful or ParkTemperament.Feisty) => .25f,
            (ParkGameKind.Race, ParkTemperament.Lazy) => -.3f,
            (ParkGameKind.Brush, ParkTemperament.Gentle or ParkTemperament.Friendly) => .2f,
            (ParkGameKind.Brush, ParkTemperament.Fierce) => -.25f,
            (ParkGameKind.Spar, _) => .4f,
            (_, ParkTemperament.Aloof) => -.15f,
            _ => .05f
        };
        if (host.IsAsleep) { Interrupt(host); Wake(host); }
        else Interrupt(host);
        if (host.Resident.WaterBound && kind is ParkGameKind.Race or ParkGameKind.Brush)
            return new(false, $"{host.Resident.Name} cannot leave the water for that.", "Sigh");
        if (kind == ParkGameKind.Spar && !host.Resident.Types.Contains(ParkType.Fighting))
            return new(false, $"{host.Resident.Name} does not know any martial arts. It looks at your fists, puzzled.", "Normal");
        if (!Willing(host, appeal)) return Refuse(host, ParkCommand.Play, kind);

        var now = Now;
        switch (kind)
        {
            case ParkGameKind.HideAndSeek:
            {
                // It hides somewhere in the place where it stands, so the search stays on one screenful.
                var place = _map.RegionAt(host.X, host.Y);
                var spots = _map.Spots.Where(s => s.Kind == ParkSpotKind.Hide && place.Contains(s.X, s.Y) && host.Navigator.CanStand(s.X, s.Y) &&
                    host.Navigator.Connected(new(host.X, host.Y), new(s.X, s.Y)) && !Crowded(host, s.X, s.Y, 12)).ToArray();
                if (spots.Length == 0) return new(false, $"{host.Resident.Name} looks around for a hiding place and cannot find one.", "Sigh");
                // Timid ones pick the hiding place farthest away.
                var spot = host.Resident.Temperament == ParkTemperament.Timid
                    ? spots.OrderByDescending(s => Distance(s.X, s.Y, host.X, host.Y)).First()
                    : spots[host.Random.Next(spots.Length)];
                Game = new ParkGame { Kind = kind, HostId = hostId, Players = [hostId], Stage = ParkGameStage.Starting, StageSince = now, Spot = new(spot.X, spot.Y) };
                host.InGame = true;
                Add(host, new Label("Running off to hide", "Playful"), new Act(ParkPose.Hop, 600, 0, ParkEmote.Note),
                    new GoTo(new(spot.X, spot.Y), 1.7f), new Hide(), new Label("Hiding", "Playful"), new Act(ParkPose.Idle, SeekMilliseconds + 30_000, spot.Facing));
                return new(true, $"{host.Resident.Name} giggles and runs off to hide. Cover your eyes and count to ten!", "Joyous");
            }
            case ParkGameKind.Race:
            {
                if (!host.Navigator.CanStand(RaceStart.X, RaceStart.Y) || !host.Navigator.CanStand(RaceFinish.X, RaceFinish.Y) ||
                    !host.Navigator.Connected(new(host.X, host.Y), RaceStart) || !host.Navigator.Connected(RaceStart, RaceFinish))
                    return new(false, $"{host.Resident.Name} looks around. There's no room for a race here.", "Sigh");
                // Up to three awake neighbors join in, if they feel like it.
                var rivals = _actors.Values.Where(o => o != host && !IsVisitor(o.Resident.Id) && !o.Busy && !o.IsAsleep && !o.Indoors && !o.Resident.WaterBound &&
                        o.Navigator.Connected(new(o.X, o.Y), RaceStart) && Willing(o, o.Resident.Temperament == ParkTemperament.Lazy ? -.4f : 0))
                    .OrderBy(o => Distance(o.X, o.Y, host.X, host.Y)).Take(3).ToList();
                if (rivals.Count == 0) return new(false, $"{host.Resident.Name} looks around for someone to race. Nobody is up for it right now.", "Sigh");
                var racers = rivals.Prepend(host).ToArray();
                Game = new ParkGame { Kind = kind, HostId = hostId, Players = racers.Select(r => r.Resident.Id).ToArray(), Stage = ParkGameStage.Starting,
                    StageSince = now, Deadline = now + RaceMilliseconds + 30_000 };
                for (var lane = 0; lane < racers.Length; lane++)
                {
                    var racer = racers[lane];
                    if (racer != host) Interrupt(racer);
                    racer.InGame = true;
                    // Its race pace from its Speed stat alone (its everyday pace already counts it).
                    var speed = (2.2f + Math.Clamp(racer.Resident.BaseSpeed, 1, 200) / 200f * 1.6f) * 12 / racer.Speed;
                    Add(racer, new Label("Lining up for the race", "Determined"), new GoTo(new(RaceStart.X, RaceStart.Y + lane * 8), 16 / racer.Speed),
                        new Act(ParkPose.Idle, 300, 2), new WaitForGo(), new Label("Racing!", "Determined"));
                    // A lazy racer lies down halfway; a clumsy one trips.
                    if (racer.Resident.Temperament == ParkTemperament.Lazy && racer.Random.Next(2) == 0)
                        Add(racer, new GoTo(_map.AtHome(150, 124), speed), new Act(ParkPose.Lie, 3500, Emote: ParkEmote.Ellipsis));
                    else if (racer.Resident.Personality is ParkPersonality.Energetic or ParkPersonality.Playful && racer.Random.Next(3) == 0)
                        Add(racer, new GoTo(_map.AtHome(130, 128), speed), new Act(ParkPose.Trip, 1100, Emote: ParkEmote.Sweat));
                    Add(racer, new GoTo(new(RaceFinish.X, RaceFinish.Y - lane * 6), speed), new Finish(), new Act(ParkPose.Idle, 60_000, 6));
                }
                return new(true, $"{string.Join(", ", racers.Select(r => r.Resident.Name))} line up at the west end of the home path. First to the flag wins! Tap your Pokémon to cheer it on.", "Determined");
            }
            case ParkGameKind.Spar:
            {
                Game = new ParkGame { Kind = kind, HostId = hostId, Players = [hostId], Stage = ParkGameStage.Countdown, StageSince = now };
                host.InGame = true;
                Add(host, new Label("Training you", "Determined"), new Act(ParkPose.Idle, 120_000, 0));
                var care = host.Care;
                var intro = ParkVoices.Line(ParkVoices.For(host.Resident.Species).Intro, care.SparCount, host.Resident.Name);
                if (care.SparBest > 0) intro += $" Last time you blocked {care.SparBest} of {SparRounds}.";
                return new(true, intro + " Block each strike the moment it lands.", "Determined");
            }
            case ParkGameKind.Brush:
                Game = new ParkGame { Kind = kind, HostId = hostId, Players = [hostId], Stage = ParkGameStage.Playing, StageSince = now, Deadline = now + BrushMilliseconds };
                host.InGame = true;
                Add(host, new Label("Getting brushed", "Peaceful"), new Act(ParkPose.Idle, BrushMilliseconds + 5000, 0));
                return new(true, $"{host.Resident.Name} sits still and waits for the brush. Rub it gently with your finger.", "Happy");
        }
        return ParkReply.None;
    }

    /// <summary>Asks a resident to dig where the player points. Ground and Rock types find more.</summary>
    public ParkReply StartDig(string hostId, ParkPoint at) { lock (_gate) return StartDigCore(hostId, at); }

    private ParkReply StartDigCore(string hostId, ParkPoint at)
    {
        if (Game is not null || IsVisitor(hostId) || !_actors.TryGetValue(hostId, out var host)) return ParkReply.None;
        var keen = host.Resident.Types.Contains(ParkType.Ground) || host.Resident.Types.Contains(ParkType.Rock) || host.Resident.Has(ParkQuirk.CoinFinder) ? .3f : 0;
        if (host.IsAsleep) { Interrupt(host); Wake(host); }
        else Interrupt(host);
        if (!Willing(host, keen)) return Refuse(host, ParkCommand.Play, ParkGameKind.Dig);
        var cell = host.Navigator.CellNear(at.X, at.Y);
        if (cell < 0) return ParkReply.None;
        var spot = host.Navigator.CanStand(at.X, at.Y) && host.Navigator.Connected(new(host.X, host.Y), at) ? at : host.Navigator.Center(cell);
        if (_map.TerrainAt(spot.X, spot.Y) == ParkTerrain.Water)
            return new(false, $"{host.Resident.Name} looks at the water, then at you. Nobody digs in a lake.", "Sigh");
        Game = new ParkGame { Kind = ParkGameKind.Dig, HostId = hostId, Players = [hostId], Stage = ParkGameStage.Playing, StageSince = Now, Spot = spot };
        host.InGame = true;
        Add(host, new Label("Off to dig", "Curious"), new GoTo(spot, 1.4f), new Label("Digging", "Determined"),
            new Spark(ParkEffectKind.Dirt), new Act(ParkPose.Pull, 1600, 0), new Spark(ParkEffectKind.Dirt), new Act(ParkPose.Charge, 1200, 0),
            new Spark(ParkEffectKind.Dirt), new Unearth());
        return new(true, null, "Determined");
    }

    /// <summary>Hide and seek: the player looks at a spot.</summary>
    /// <returns>True when that is where it hides.</returns>
    public bool Seek(ParkPoint at) { lock (_gate) return SeekCore(at); }

    private bool SeekCore(ParkPoint at)
    {
        if (Game is not { Kind: ParkGameKind.HideAndSeek, Stage: ParkGameStage.Playing } game || !_actors.TryGetValue(game.HostId, out var host)) return false;
        game.Tries++;
        if (Distance(at.X, at.Y, game.Spot.X, game.Spot.Y) > 26)
        {
            _effects.Add(new(ParkEffectKind.Miss, at.X, at.Y, Now));
            // After two misses the bushes rustle where it hides.
            if (game.Tries % 2 == 0) _effects.Add(new(ParkEffectKind.Rustle, game.Spot.X, game.Spot.Y - 6, Now));
            return false;
        }
        Found(game, host, $"Found it! {host.Resident.Name} jumps out with a squeal" +
            (game.Tries == 1 ? " and stares at you. How did you know?" : $". It took you {game.Tries} tries."), seeker: true);
        return true;
    }

    /// <summary>Race: the player cheers a racer on; it runs faster for a moment.</summary>
    public void Cheer(string id) { lock (_gate) CheerCore(id); }

    private void CheerCore(string id)
    {
        if (Game is not { Kind: ParkGameKind.Race, Stage: ParkGameStage.Playing } game || !game.Players.Contains(id) ||
            !_actors.TryGetValue(id, out var racer) || game.Finished.Contains(id)) return;
        racer.BoostUntil = Now + 600;
        racer.Say(ParkEmote.Exclaim, Now);
    }

    /// <summary>Brushing: the player's finger went over the Pokémon (distance in art pixels).</summary>
    public void Brush(float stroke) { lock (_gate) BrushCore(stroke); }

    private void BrushCore(float stroke)
    {
        if (Game is not { Kind: ParkGameKind.Brush, Stage: ParkGameStage.Playing } game || !_actors.TryGetValue(game.HostId, out var host)) return;
        var before = game.Progress;
        game.Progress = Math.Min(1, game.Progress + stroke / 900f);
        if ((int)(before * 10) != (int)(game.Progress * 10)) _effects.Add(new(ParkEffectKind.Sparkle, host.X, host.Y - 10, Now));
        var resident = host.Resident;
        // An electric type builds up static as it is rubbed, and lets it all go at once.
        if (!game.Zapped && game.Progress > .6f && resident.Types.Contains(ParkType.Electric))
        {
            game.Zapped = true;
            _effects.Add(new(ParkEffectKind.Zap, host.X, host.Y - 10, Now));
            host.Say(ParkEmote.Sweat, Now);
        }
        // The fierce put up with it for a while only.
        if (resident.Temperament == ParkTemperament.Fierce && game.Progress > .4f && host.Care.Bond < 50)
        {
            _effects.Add(new(ParkEffectKind.Bite, host.X, host.Y - 10, Now));
            Over(game, host, $"{resident.Name} has had enough. It snaps at the brush and walks off.", ParkPose.Attack, ParkEmote.Anger);
            return;
        }
        if (game.Progress >= 1)
        {
            host.Care = host.Care.Add(ParkNeed.Fun, 10).AddBond(5, Today);
            Over(game, host, game.Zapped
                ? $"{resident.Name}'s coat is soft and shiny now, and so full of static that your hair stands on end."
                : $"{resident.Name}'s coat is soft and shiny. It leans into your hand, eyes half closed.", ParkPose.Dance, ParkEmote.Heart);
        }
    }

    /// <summary>Sparring: the player blocks. Right as the strike lands it counts; during the wind-up it
    /// is too early (a feint got you).</summary>
    public void Block() { lock (_gate) BlockCore(); }

    private void BlockCore()
    {
        if (Game is not { Kind: ParkGameKind.Spar, Stage: ParkGameStage.Playing } game || game.Answered ||
            !_actors.TryGetValue(game.HostId, out var host)) return;
        var now = Now;
        if (game.Striking)
        {
            game.Answered = true;
            game.Blocked++;
            _effects.Add(new(ParkEffectKind.Sparkle, host.X, host.Y + 4, now));
            host.Say(ParkEmote.Exclaim, now);
            NextRound(game, host, now);
        }
        else if (game.Winding)
        {
            game.Answered = true;
            _effects.Add(new(ParkEffectKind.Miss, host.X, host.Y + 6, now));
            host.Say(ParkEmote.Note, now);
        }
    }

    private const int SparWindow = 560;

    private void NextRound(ParkGame game, ParkActor host, long now)
    {
        game.Striking = false; game.Winding = false;
        host.SetPose(ParkPose.Idle, now);
        game.Round++;
        if (game.Round >= SparRounds) { SparOver(game, host); return; }
        game.Answered = false;
        game.WindupAt = now + 700;
    }

    private void UpdateSpar(ParkGame game, ParkActor host, long now)
    {
        if (game.Stage == ParkGameStage.Countdown)
        {
            if (now - game.StageSince < 2500) return;
            game.Stage = ParkGameStage.Playing; game.StageSince = now; game.WindupAt = now;
        }
        if (!game.Winding && !game.Striking && now >= game.WindupAt)
        {
            // An unpredictable wind-up: it is the waiting that tests you.
            game.Winding = true;
            game.StrikeAt = now + host.Random.Next(500, 1500);
            host.Facing = 0;
            host.SetPose(ParkPose.Charge, now);
            host.Say(ParkEmote.Exclaim, now);
        }
        else if (game.Winding && now >= game.StrikeAt)
        {
            game.Winding = false; game.Striking = true;
            host.SetPose(ParkPose.Attack, now);
        }
        // The window narrows as the rounds go on.
        else if (game.Striking && now > game.StrikeAt + Math.Max(300, SparWindow - game.Round * 30))
        {
            _effects.Add(new(ParkEffectKind.Bite, host.X, host.Y + 6, now));
            NextRound(game, host, now);
        }
    }

    private void SparOver(ParkGame game, ParkActor host)
    {
        var voice = ParkVoices.For(host.Resident.Species);
        var care = host.Care;
        var lines = game.Blocked == SparRounds ? voice.Perfect : game.Blocked >= 5 ? voice.Good : voice.Poor;
        var line = ParkVoices.Line(lines, care.SparCount, host.Resident.Name) + $" ({game.Blocked} of {SparRounds} blocked.)";
        if (game.Blocked > care.SparBest && care.SparCount > 0) line += " A new personal best!";
        host.Care = care with { SparBest = Math.Max(care.SparBest, game.Blocked), SparCount = care.SparCount + 1 };
        host.Care = host.Care.Add(ParkNeed.Fun, 15).AddBond(game.Blocked >= 6 ? 8 : 4, Today);
        Over(game, host, line, game.Blocked >= 5 ? ParkPose.Pose : ParkPose.Nod, game.Blocked >= 5 ? ParkEmote.Sparkle : ParkEmote.Ellipsis);
        if (game.Blocked == SparRounds)
            _moments.Add(new ParkMoment(ParkSocialEventKind.Bond, host.Resident.Id, "", $"You blocked every one of {host.Resident.Name}'s strikes. It has been bowing to you all day."));
    }

    /// <summary>Stops the game (after the page showed its result, or when the player leaves).</summary>
    public void EndGame() { lock (_gate) EndGameCore(); }

    private void EndGameCore()
    {
        if (Game is not { } game) return;
        foreach (var id in game.Players)
            if (_actors.TryGetValue(id, out var player))
            {
                player.InGame = false; player.Hidden = false; player.BoostUntil = 0;
                // Stopped midway, a player drops the game's plan (the walk to its hiding place,
                // the race); a finished game lets the winner finish celebrating.
                if (game.Stage != ParkGameStage.Over || player.Current is WaitForGo) { player.Plan.Clear(); player.Current = null; }
            }
        Game = null;
    }

    private bool RunGameStep(ParkActor actor, Step step, long now)
    {
        switch (step)
        {
            case Hide:
                actor.Hidden = Game is { Kind: ParkGameKind.HideAndSeek, Stage: not ParkGameStage.Over } seeking && seeking.HostId == actor.Resident.Id;
                return true;
            case WaitForGo:
                return Game is not { Kind: ParkGameKind.Race } game || game.Stage == ParkGameStage.Playing || game.Stage == ParkGameStage.Over;
            case Finish:
                if (Game is { Kind: ParkGameKind.Race } race && !race._finished.Contains(actor.Resident.Id)) race._finished.Add(actor.Resident.Id);
                return true;
            case Unearth:
                if (Game is { Kind: ParkGameKind.Dig } dig)
                {
                    var treasure = ParkLoot.Dig(actor.Resident, actor.Random);
                    dig.Treasure = treasure.Treasure;
                    Burst(treasure.Treasure.Rarity >= 3 ? ParkEffectKind.Sparkle : ParkEffectKind.Coin, actor);
                    Over(dig, actor, treasure.Line, treasure.Treasure.Rarity >= 2 ? ParkPose.Dance : ParkPose.Hop,
                        treasure.Treasure.Rarity >= 2 ? ParkEmote.Sparkle : ParkEmote.Note);
                    actor.Care = actor.Care.Add(ParkNeed.Fun, 12).AddBond(3, Today);
                    if (treasure.Treasure.Rarity >= 3) _moments.Add(new ParkMoment(ParkSocialEventKind.Rest, actor.Resident.Id, "", treasure.Line));
                }
                return true;
        }
        return true;
    }

    private void UpdateGame(long now)
    {
        // A result nobody came to read closes on its own.
        if (Game is { Stage: ParkGameStage.Over } done && now - done.StageSince > 30_000) EndGame();
        if (Game is not { Stage: not ParkGameStage.Over } game || !_actors.TryGetValue(game.HostId, out var host)) return;
        switch (game.Kind)
        {
            case ParkGameKind.HideAndSeek:
                if (game.Stage == ParkGameStage.Starting && host.Hidden)
                {
                    game.Stage = ParkGameStage.Playing; game.StageSince = now; game.Deadline = now + SeekMilliseconds;
                }
                else if (game.Stage == ParkGameStage.Starting && now - game.StageSince > 30_000)
                    Over(game, host, $"{host.Resident.Name} could not find anywhere to hide and gives up, a little embarrassed.", ParkPose.Idle, ParkEmote.Sweat);
                else if (game.Stage == ParkGameStage.Playing && now > game.Deadline)
                    Found(game, host, $"{host.Resident.Name} gets tired of waiting and pops out on its own, very proud. It wins this round!", seeker: false);
                break;
            case ParkGameKind.Race:
                var racers = game.Players.Select(id => _actors.GetValueOrDefault(id)).OfType<ParkActor>().ToArray();
                if (game.Stage == ParkGameStage.Starting && racers.All(r => r.Current is WaitForGo))
                {
                    game.Stage = ParkGameStage.Countdown; game.StageSince = now;
                }
                else if (game.Stage == ParkGameStage.Starting && now - game.StageSince > 30_000)
                {
                    game.Stage = ParkGameStage.Countdown; game.StageSince = now; // stragglers start late
                }
                else if (game.Stage == ParkGameStage.Countdown && now - game.StageSince >= 3000)
                {
                    game.Stage = ParkGameStage.Playing; game.StageSince = now; game.Deadline = now + RaceMilliseconds;
                }
                else if (game.Stage == ParkGameStage.Playing && (game.Finished.Count == racers.Length || now > game.Deadline))
                    RaceOver(game, racers);
                break;
            case ParkGameKind.Spar:
                UpdateSpar(game, host, now);
                break;
            case ParkGameKind.Brush:
                if (now > game.Deadline) Over(game, host, $"{host.Resident.Name} got bored of sitting still and wanders off, half brushed.", ParkPose.Idle, ParkEmote.Ellipsis);
                break;
        }
    }

    private void RaceOver(ParkGame game, ParkActor[] racers)
    {
        var names = game.Finished.Select(id => _actors[id].Resident.Name).ToList();
        var winner = game.Finished.Count > 0 ? _actors[game.Finished[0]] : null;
        foreach (var racer in racers)
        {
            racer.Plan.Clear(); racer.Current = null;
            var won = racer == winner;
            Add(racer, new Label(won ? "Won the race!" : "Catching its breath after the race", won ? "Proud" : "Content"),
                new Act(won ? ParkPose.Pose : ParkPose.Hop, 1800, 0, won ? ParkEmote.Sparkle : ParkEmote.Note));
            racer.Care = racer.Care.Add(ParkNeed.Fun, 20).Add(ParkNeed.Energy, -8).Add(ParkNeed.Social, 8);
        }
        if (_actors.TryGetValue(game.HostId, out var host)) host.Care = host.Care.AddBond(4, Today);
        var line = winner is null ? "Nobody reached the flag. The race turned into a picnic halfway."
            : names.Count == 1 ? $"{names[0]} wins the race! The others are still on their way."
            : $"{names[0]} wins the race, ahead of {string.Join(", then ", names.Skip(1))}!";
        if (winner is not null && winner.Resident.Id == game.HostId && names.Count > 1) line += " Your cheering paid off.";
        game.Result = line;
        game.Stage = ParkGameStage.Over;
        game.StageSince = Now;
        if (winner is not null) _moments.Add(new ParkMoment(ParkSocialEventKind.Play, winner.Resident.Id, "", line));
    }

    private void Found(ParkGame game, ParkActor host, string line, bool seeker)
    {
        host.Hidden = false;
        host.Care = host.Care.Add(ParkNeed.Fun, 20).AddBond(seeker ? 6 : 3, Today);
        Over(game, host, line, seeker ? ParkPose.Hop : ParkPose.Pose, seeker ? ParkEmote.Exclaim : ParkEmote.Sparkle);
    }

    private void Over(ParkGame game, ParkActor host, string line, ParkPose pose, ParkEmote emote)
    {
        game.Result = line;
        game.Stage = ParkGameStage.Over;
        game.StageSince = Now;
        host.Plan.Clear(); host.Current = null; host.Hidden = false;
        Add(host, new Act(pose, 1800, 0, emote), new Act(ParkPose.Idle, 1200, 0));
    }
}

/// <summary>What digging turns up, by luck and by type.</summary>
public static class ParkLoot
{
    private static readonly (string Name, int Rarity, int Weight)[] Table =
    [
        ("a worm", 0, 22), ("a smooth pebble", 0, 18), ("an Oran Berry", 1, 16), ("a Pecha Berry", 1, 10), ("a shiny coin", 1, 12),
        ("an old Poké Ball, empty", 2, 6), ("a Fire Stone", 2, 3), ("a Water Stone", 2, 3), ("a Thunder Stone", 2, 3), ("a Leaf Stone", 2, 3),
        ("a Moon Stone", 2, 2), ("a Nugget", 3, 2), ("a Helix Fossil", 3, 1), ("a Dome Fossil", 3, 1), ("an Old Amber", 3, 1)
    ];

    public static (ParkTreasure Treasure, string Line) Dig(ParkLifeResident digger, Random random)
    {
        // Diggers by nature skip most of the worms.
        var expert = digger.Types.Contains(ParkType.Ground) || digger.Types.Contains(ParkType.Rock) || digger.Has(ParkQuirk.CoinFinder);
        var weights = Table.Select(t => t.Rarity == 0 && expert ? t.Weight / 3 : t.Rarity >= 2 && expert ? t.Weight * 2 : t.Weight).ToArray();
        var roll = random.Next(weights.Sum());
        var index = 0;
        while (roll >= weights[index]) roll -= weights[index++];
        var (name, rarity, _) = Table[index];
        var who = digger.Name;
        var line = (name, rarity) switch
        {
            ("a worm", _) => $"{who} digs up a worm, looks at it, and lets it go. Maybe next time.",
            ("a smooth pebble", _) => $"{who} proudly hands you a smooth pebble. It is a very good pebble.",
            (_, 3) => $"{who} digs and digs, then freezes. It found {name}! It will not stop showing it off.",
            (_, 2) => $"{who} unearths {name} and brings it to you with great ceremony.",
            _ => $"{who} digs up {name} and drops it at your feet."
        };
        return (new ParkTreasure(name, rarity), line);
    }
}
