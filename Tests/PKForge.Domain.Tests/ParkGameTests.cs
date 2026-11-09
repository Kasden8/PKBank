using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>Games with the residents: hide and seek, races, digging and brushing, and the ways
/// they end (found, timed out, refused, interrupted).</summary>
public sealed class ParkGameTests
{
    private static readonly ParkMap Home = ParkMap.Park;

    private static ParkLifeResident R(string id, int species, ParkPersonality personality, int speed = 60, params int[] types) =>
        new(id, id, species, types.Length == 0 ? [ParkType.Normal] : types, personality, BaseSpeed: speed);

    private static ParkLife Park(params ParkLifeResident[] residents)
    {
        var life = new ParkLife(Home, residents, seed: 5) { Today = new DateOnly(2026, 10, 6) };
        life.Restore(residents.Select(r => new ParkCareState(r.Id, Bond: 80)), DateTimeOffset.Now);
        life.Advance(1000);
        return life;
    }

    private static long RunUntil(ParkLife life, long from, Func<bool> done, long limit = 5 * 60 * 1000)
    {
        var t = from;
        for (; t < from + limit && !done(); t += 100) life.Advance(t);
        return t;
    }

    [Fact]
    public void Every_hiding_spot_is_somewhere_to_stand()
    {
        foreach (var spot in Home.Spots.Where(s => s.Kind == ParkSpotKind.Hide))
            Assert.True(Home.CanOccupy(spot.X, spot.Y, ParkTraversal.Land | ParkTraversal.Water), $"{spot.X},{spot.Y}");
    }

    [Fact]
    public void Hide_and_seek_is_won_by_finding_it()
    {
        var life = Park(R("eevee", 133, ParkPersonality.Playful));
        Assert.True(life.StartGame(ParkGameKind.HideAndSeek, "eevee").Accepted);
        var t = RunUntil(life, 1000, () => life.Game!.Stage == ParkGameStage.Playing);
        var eevee = life["eevee"]!;
        Assert.True(eevee.Hidden);
        Assert.False(life.Seek(eevee.X > Home.AtHome(168, 0).X ? Home.AtHome(20, 100) : Home.AtHome(320, 100)));
        Assert.True(life.Seek(new ParkPoint(eevee.X + 4, eevee.Y - 3)));
        Assert.Equal(ParkGameStage.Over, life.Game!.Stage);
        Assert.Contains("2 tries", life.Game.Result);
        Assert.False(eevee.Hidden);
        life.EndGame();
        Assert.Null(life.Game);
        Assert.False(eevee.InGame);
        RunUntil(life, t, () => false, 20_000);
    }

    [Fact]
    public void Nobody_found_it_and_it_comes_out_on_its_own()
    {
        var life = Park(R("eevee", 133, ParkPersonality.Playful));
        life.StartGame(ParkGameKind.HideAndSeek, "eevee");
        RunUntil(life, 1000, () => life.Game!.Stage == ParkGameStage.Over, ParkLife.SeekMilliseconds + 60_000);
        Assert.Contains("pops out on its own", life.Game!.Result);
    }

    [Fact]
    public void A_race_ends_with_everyone_in_order()
    {
        var life = Park(R("ninjask", 291, ParkPersonality.Bold, 160, ParkType.Bug), R("slugma", 218, ParkPersonality.Calm, 20, ParkType.Fire),
            R("eevee", 133, ParkPersonality.Calm, 55), R("meowth", 52, ParkPersonality.Calm, 90));
        var reply = life.StartGame(ParkGameKind.Race, "ninjask");
        Assert.True(reply.Accepted, reply.Line);
        var t = RunUntil(life, 1000, () => life.Game!.Stage == ParkGameStage.Playing);
        for (var cheer = 0; cheer < 30; cheer++) { life.Cheer("ninjask"); life.Advance(t += 300); }
        RunUntil(life, t, () => life.Game!.Stage == ParkGameStage.Over);
        var game = life.Game!;
        Assert.Equal(game.Players.Count, game.Finished.Count);
        Assert.Equal("ninjask", game.Finished[0]);
        Assert.StartsWith("ninjask wins the race", game.Result);
    }

    [Fact]
    public void Digging_turns_something_up()
    {
        var life = Park(R("diglett", 50, ParkPersonality.Curious, 95, ParkType.Ground));
        Assert.True(life.StartDig("diglett", Home.AtHome(60, 110)).Accepted);
        RunUntil(life, 1000, () => life.Game!.Stage == ParkGameStage.Over);
        Assert.NotNull(life.Game!.Treasure);
        Assert.Contains("diglett", life.Game.Result);
        Assert.False(life.StartDig("diglett", Home.AtHome(300, 180)).Accepted);
    }

    [Fact]
    public void Brushing_an_electric_type_ends_in_static()
    {
        var life = Park(R("pikachu", 25, ParkPersonality.Gentle, 90, ParkType.Electric));
        Assert.True(life.StartGame(ParkGameKind.Brush, "pikachu").Accepted);
        for (var stroke = 0; stroke < 40 && life.Game!.Stage != ParkGameStage.Over; stroke++) life.Brush(30);
        Assert.Contains("static", life.Game!.Result);
        Assert.Contains(life.Effects, e => e.Kind == ParkEffectKind.Zap);
    }

    [Fact]
    public void A_game_ends_when_its_player_leaves()
    {
        var eevee = R("eevee", 133, ParkPersonality.Playful);
        var life = Park(eevee, R("pikachu", 25, ParkPersonality.Playful));
        life.StartGame(ParkGameKind.HideAndSeek, "eevee");
        life.SetResidents([R("pikachu", 25, ParkPersonality.Playful)]);
        Assert.Null(life.Game);
    }

    [Fact]
    public void Only_one_game_at_a_time()
    {
        var life = Park(R("eevee", 133, ParkPersonality.Playful), R("pikachu", 25, ParkPersonality.Playful));
        Assert.True(life.StartGame(ParkGameKind.HideAndSeek, "eevee").Accepted);
        Assert.Same(ParkReply.None, life.StartGame(ParkGameKind.Brush, "pikachu"));
    }
}

/// <summary>Sparring with a fighting type: block as each strike lands, not during the wind-up.</summary>
public sealed class ParkSparTests
{
    private static ParkLife Dojo(int species = 68)
    {
        var master = new ParkLifeResident("master", "Machamp", species, [ParkType.Fighting], ParkPersonality.Bold);
        var life = new ParkLife(ParkMap.Park, [master], seed: 2) { Today = new DateOnly(2026, 10, 6) };
        life.Restore([new ParkCareState("master", Bond: 90)], DateTimeOffset.Now);
        life.Advance(1000);
        return life;
    }

    private static ParkGame Spar(ParkLife life, Func<ParkGame, bool> blockNow)
    {
        var reply = life.StartGame(ParkGameKind.Spar, "master");
        Assert.True(reply.Accepted, reply.Line);
        for (long t = 1000; t < 120_000 && life.Game!.Stage != ParkGameStage.Over; t += 50)
        {
            life.Advance(t);
            if (life.Game.Stage == ParkGameStage.Playing && blockNow(life.Game)) life.Block();
        }
        return life.Game!;
    }

    [Fact]
    public void Blocking_every_strike_is_a_perfect_session_and_is_remembered()
    {
        var life = Dojo();
        var game = Spar(life, g => g.Striking);
        Assert.Equal(ParkLife.SparRounds, game.Blocked);
        Assert.Contains("8 of 8 blocked", game.Result);
        Assert.Equal(8, life["master"]!.Care.SparBest);
        life.EndGame();
        Assert.Contains("Last time you blocked 8 of 8", life.StartGame(ParkGameKind.Spar, "master").Line);
    }

    [Fact]
    public void Never_blocking_or_blocking_too_early_scores_nothing()
    {
        Assert.Equal(0, Spar(Dojo(), _ => false).Blocked);
        Assert.Equal(0, Spar(Dojo(), g => !g.Striking).Blocked);
    }

    [Fact]
    public void Only_fighting_types_spar()
    {
        var life = new ParkLife(ParkMap.Park, [new ParkLifeResident("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Bold)], seed: 1);
        life.Advance(1000);
        var reply = life.StartGame(ParkGameKind.Spar, "eevee");
        Assert.False(reply.Accepted);
        Assert.Contains("martial arts", reply.Line);
        Assert.Null(life.Game);
    }

    [Fact]
    public void Each_family_has_its_own_voice()
    {
        Assert.NotSame(ParkVoices.For(68), ParkVoices.For(448));
        Assert.Contains("aura", ParkVoices.Line(ParkVoices.For(448).Intro, 1, "Lucario"));
        Assert.NotEqual(ParkVoices.Line(ParkVoices.For(68).Talk, 0, "Machamp"), ParkVoices.Line(ParkVoices.For(68).Talk, 1, "Machamp"));
    }
}
