using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>
/// The park is not all sunshine: temperaments decide who goes along with the player, touching
/// some Pokémon has consequences, and species pairs love, hate, hunt or fear each other.
/// </summary>
public sealed class ParkLoreTests
{
    private static readonly ParkMap Home = ParkMap.Park;

    private static ParkLifeResident R(string id, int species, ParkPersonality personality, params int[] types) =>
        new(id, id, species, types, personality);

    private static List<ParkMoment> Run(ParkLife life, long until, Func<bool>? stop = null)
    {
        var moments = new List<ParkMoment>();
        for (long t = 0; t <= until && stop?.Invoke() != true; t += 250)
        {
            life.Advance(t);
            moments.AddRange(life.TakeMoments());
        }
        return moments;
    }

    [Fact]
    public void Temperaments_come_from_the_species_first()
    {
        Assert.Equal(ParkTemperament.Fierce, ParkLore.TemperamentOf(130, ParkPersonality.Gentle));    // Gyarados
        Assert.Equal(ParkTemperament.Aloof, ParkLore.TemperamentOf(150, ParkPersonality.Playful));    // Mewtwo
        Assert.Equal(ParkTemperament.Mischievous, ParkLore.TemperamentOf(94, ParkPersonality.Calm));  // Gengar
        Assert.Equal(ParkTemperament.Playful, ParkLore.TemperamentOf(25, ParkPersonality.Shy));       // Pikachu
        Assert.Equal(ParkTemperament.Timid, ParkLore.TemperamentOf(10, ParkPersonality.Shy));         // Caterpie, by personality
        Assert.True(ParkLore.Docility(ParkTemperament.Fierce, 100, "Happy", null) > ParkLore.Docility(ParkTemperament.Fierce, 0, "Happy", null));
        Assert.True(ParkLore.Docility(ParkTemperament.Gentle, 0, "Grumpy", null) < ParkLore.Docility(ParkTemperament.Gentle, 0, "Happy", null));
    }

    [Fact]
    public void Species_pairs_feel_what_the_games_say()
    {
        Assert.Equal(ParkPairKind.Love, ParkLore.Feeling(313, 314));     // Volbeat, Illumise
        Assert.Equal(ParkPairKind.Love, ParkLore.Feeling(314, 313));
        Assert.Equal(ParkPairKind.Rivals, ParkLore.Feeling(335, 336));   // Zangoose, Seviper
        Assert.Equal(ParkPairKind.Hunts, ParkLore.Feeling(631, 632));    // Heatmor hunts Durant
        Assert.Equal(ParkPairKind.Fears, ParkLore.Feeling(632, 631));    // and Durant knows it
        Assert.Contains(ParkLore.Feeling(778, 25, 0), new[] { ParkPairKind.Rivals, ParkPairKind.Fears }); // Mimikyu and Pikachu
        Assert.Contains(ParkLore.Feeling(778, 25, 1), new[] { ParkPairKind.Rivals, ParkPairKind.Fears });
        Assert.NotEqual(ParkLore.Feeling(778, 25, 0), ParkLore.Feeling(778, 25, 1));
        Assert.Equal(ParkPairKind.None, ParkLore.Feeling(25, 778));      // Pikachu has no idea
    }

    [Fact]
    public void Touching_an_electric_type_can_zap_you_less_once_it_trusts_you()
    {
        var (kind, stranger) = ParkLore.AccidentOf(25, [ParkType.Electric], ParkTemperament.Playful, 0, "Happy");
        var (_, friend) = ParkLore.AccidentOf(25, [ParkType.Electric], ParkTemperament.Playful, 100, "Happy");
        Assert.Equal(ParkAccident.Zap, kind);
        Assert.True(friend < stranger);
        Assert.Equal(ParkAccident.Lick, ParkLore.AccidentOf(108, [ParkType.Normal], ParkTemperament.Friendly, 0, "Happy").Kind);
        Assert.Contains("Zap", ParkLore.AccidentLine(ParkAccident.Zap, "Pikachu"));
    }

    [Fact]
    public void Porygon2_has_its_own_line()
    {
        Assert.Equal("Porygon2 lightly taps your electronic device out of curiosity. You wonder whether it wants to play with it or go inside it.",
            ParkLore.TalkLine(233, "Porygon2", 0));
    }

    [Fact]
    public void An_aloof_pokemon_lets_the_ball_go_and_a_playful_one_fetches_it()
    {
        var life = new ParkLife(Home, [R("mewtwo", 150, ParkPersonality.Proud, ParkType.Psychic), R("pikachu", 25, ParkPersonality.Playful, ParkType.Electric)], seed: 2);
        Run(life, 2000);
        var reply = life.Fetch("mewtwo", ParkMap.Park.AtHome(168, 150));
        Assert.False(reply.Accepted);
        Assert.Contains("dashes after it instead", reply.Line);
        var pikachuHeld = false;
        for (long t = 2000; t < 40_000; t += 100)
        {
            life.Advance(t);
            pikachuHeld |= life.Ball?.HolderId == "pikachu";
        }
        Assert.True(pikachuHeld);
    }

    [Fact]
    public void Jigglypuffs_lullaby_puts_the_park_to_sleep()
    {
        var life = new ParkLife(Home, [R("puff", 39, ParkPersonality.Playful, ParkType.Normal, ParkType.Fairy),
            R("eevee", 133, ParkPersonality.Calm, ParkType.Normal), R("riolu", 447, ParkPersonality.Balanced, ParkType.Fighting)], seed: 6);
        var moments = Run(life, 20 * 60 * 1000, () => false);
        Assert.Contains(moments, m => m.Text?.Contains("lullaby") == true);
    }

    [Fact]
    public void Durant_runs_from_Heatmor()
    {
        var life = new ParkLife(Home, [R("heatmor", 631, ParkPersonality.Bold, ParkType.Fire), R("durant", 632, ParkPersonality.Balanced, ParkType.Bug, ParkType.Steel)], seed: 4);
        var fled = false;
        for (long t = 0; t < 30 * 60 * 1000 && !fled; t += 250)
        {
            life.Advance(t);
            fled = life["durant"]!.Activity == "Running away from heatmor";
        }
        Assert.True(fled);
    }

    [Fact]
    public void Sworn_rivals_fight_and_lovers_spend_time_together()
    {
        var rivals = Run(new ParkLife(Home, [R("zangoose", 335, ParkPersonality.Bold, ParkType.Normal), R("seviper", 336, ParkPersonality.Bold, ParkType.Poison)], seed: 1), 30 * 60 * 1000);
        Assert.Contains(rivals, m => m.Kind == ParkSocialEventKind.Conflict && m.Text?.Contains("fought") == true || m.Text?.Contains("picked a fight") == true);
        var lovers = Run(new ParkLife(Home, [R("volbeat", 313, ParkPersonality.Gentle, ParkType.Bug), R("illumise", 314, ParkPersonality.Gentle, ParkType.Bug)], seed: 1), 30 * 60 * 1000);
        Assert.Contains(lovers, m => m.Kind == ParkSocialEventKind.Bond);
        Assert.DoesNotContain(lovers, m => m.Kind == ParkSocialEventKind.Conflict);
    }

    [Fact]
    public void Gengar_plays_pranks()
    {
        var life = new ParkLife(Home, [R("gengar", 94, ParkPersonality.Playful, ParkType.Ghost, ParkType.Poison),
            R("eevee", 133, ParkPersonality.Calm, ParkType.Normal), R("bulbasaur", 1, ParkPersonality.Gentle, ParkType.Grass)], seed: 3);
        var moments = Run(life, 30 * 60 * 1000);
        Assert.Contains(moments, m => m.Text?.Contains("scared it half to death") == true);
    }

    [Fact]
    public void A_fierce_pokemon_is_often_in_no_mood()
    {
        var refusals = 0;
        for (var seed = 0; seed < 20; seed++)
        {
            var life = new ParkLife(Home, [R("gyarados", 130, ParkPersonality.Bold, ParkType.Water, ParkType.Flying)], seed: seed);
            life.Advance(1000);
            if (!life.Command("gyarados", ParkCommand.Talk).Accepted) refusals++;
        }
        Assert.InRange(refusals, 8, 20);
    }
}

public sealed class ParkBondTests
{
    private static readonly ParkMap Home = ParkMap.Park;

    [Theory]
    [InlineData(125, 126, ParkPairKind.Rivals)]   // Electabuzz, Magmar
    [InlineData(466, 467, ParkPairKind.Rivals)]   // Electivire, Magmortar
    [InlineData(127, 214, ParkPairKind.Rivals)]   // Pinsir, Heracross
    [InlineData(491, 488, ParkPairKind.Rivals)]   // Darkrai, Cresselia
    [InlineData(243, 250, ParkPairKind.Family)]   // Raikou and Ho-Oh
    [InlineData(1, 7, ParkPairKind.Friends)]      // Bulbasaur, Squirtle
    [InlineData(810, 816, ParkPairKind.Friends)]  // Grookey, Sobble
    [InlineData(79, 90, ParkPairKind.Friends)]    // Slowpoke, Shellder
    [InlineData(16, 10, ParkPairKind.Hunts)]      // Pidgey hunts Caterpie
    [InlineData(10, 16, ParkPairKind.Fears)]      // and Caterpie knows
    [InlineData(747, 222, ParkPairKind.Hunts)]    // Mareanie, Corsola
    [InlineData(780, 172, ParkPairKind.Family)]   // Drampa minds Pichu
    public void Pairs_from_the_games(int a, int b, ParkPairKind kind) => Assert.Equal(kind, ParkLore.Feeling(a, b));

    [Fact]
    public void Those_who_fell_out_can_make_up()
    {
        // Meowth keeps pranking Eevee, so on some days they never get round to it: a few days.
        ParkMoment? makeUp = null;
        foreach (var seed in new[] { 3, 4, 5, 6 })
        {
            var life = new ParkLife(Home, [new("a", "Eevee", 133, [ParkType.Normal], ParkPersonality.Gentle),
                new("b", "Meowth", 52, [ParkType.Normal], ParkPersonality.Calm)], seed: seed) { Relation = (_, _) => (30, 40) };
            for (long t = 0; t < 2 * 60 * 60 * 1000 && makeUp is null; t += 250)
            {
                life.Advance(t);
                // The two of them (a visitor may make up with someone too).
                makeUp = life.TakeMoments().FirstOrDefault(m => m.Kind == ParkSocialEventKind.Deescalation && new[] { m.FirstId, m.SecondId }.Order().SequenceEqual(["a", "b"]));
            }
            if (makeUp is not null) break;
        }
        Assert.NotNull(makeUp);
        Assert.Contains("said sorry", makeUp.Text);

        var state = ParkSocialState.Empty(DateTimeOffset.Now) with
        {
            Relationships = [new ParkRelationship("a", "b", Friendship: 10, Rivalry: 30)]
        };
        var after = ParkSocialSimulation.Witness(state, makeUp, "Eevee", "Meowth", DateTimeOffset.Now).State.Relationships.Single();
        Assert.True(after.Rivalry < 30 && after.Friendship > 10);
    }
}

public sealed class ParkTrollTests
{
    private static readonly ParkMap Home = ParkMap.Park;

    private static List<ParkMoment> Run(ParkLife life, long until)
    {
        var moments = new List<ParkMoment>();
        for (long t = 0; t <= until; t += 250) { life.Advance(t); moments.AddRange(life.TakeMoments()); }
        return moments;
    }

    [Fact]
    public void Trolls_make_trouble_and_someone_stands_up_to_them()
    {
        // Standing up to a troublemaker takes the right moment: over a few afternoons it comes.
        var mischief = new List<string>();
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var life = new ParkLife(Home, [
                new("gengar", "Gengar", 94, [ParkType.Ghost, ParkType.Poison], ParkPersonality.Playful),
                new("murkrow", "Murkrow", 198, [ParkType.Dark, ParkType.Flying], ParkPersonality.Bold),
                new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm),
                new("bulbasaur", "Bulbasaur", 1, [ParkType.Grass], ParkPersonality.Gentle),
                new("ralts", "Ralts", 280, [ParkType.Psychic, ParkType.Fairy], ParkPersonality.Shy),
                new("lucario", "Lucario", 448, [ParkType.Fighting, ParkType.Steel], ParkPersonality.Bold)], seed: seed);
            mischief.AddRange(Run(life, 2 * 60 * 60 * 1000).Where(m => m.Kind == ParkSocialEventKind.Conflict && m.Text is not null).Select(m => m.Text!));
        }
        Assert.Contains(mischief, t => t.Contains("woke") || t.Contains("snatched") || t.Contains("tripped") || t.Contains("cornered") || t.Contains("scared"));
        Assert.Contains(mischief, t => t.Contains("stood up for"));
    }

    [Fact]
    public void A_troublemaker_is_busy_and_varies_its_tricks()
    {
        var life = new ParkLife(Home, [
            new("meowth", "Meowth", 52, [ParkType.Normal], ParkPersonality.Playful),
            new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm),
            new("bulbasaur", "Bulbasaur", 1, [ParkType.Grass], ParkPersonality.Gentle),
            new("pikachu", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Curious)], seed: 4);
        var tricks = Run(life, 60 * 60 * 1000).Where(m => m.FirstId == "meowth" && m.Kind is ParkSocialEventKind.Conflict or ParkSocialEventKind.Play && m.Text is not null)
            .Select(m => (Trick: m.Text!.Split(' ')[1], Victim: m.SecondId)).ToList();
        // Clearly a troublemaker: several tricks an hour…
        Assert.True(tricks.Count >= 4, $"{tricks.Count} tricks");
        // …of more than one kind, never the same one on the same victim twice running.
        Assert.True(tricks.Select(t => t.Trick).Distinct().Count() >= 2, string.Join(", ", tricks));
        for (var i = 1; i < tricks.Count; i++) Assert.NotEqual(tricks[i - 1], tricks[i]);
    }

    [Fact]
    public void Turning_down_a_game_never_talks_about_a_ball()
    {
        foreach (var temperament in Enum.GetValues<ParkTemperament>())
        foreach (var game in Enum.GetValues<ParkGameKind>())
        for (var roll = 0; roll < 4; roll++)
            Assert.DoesNotContain("ball", ParkLore.RefusalLine(temperament, "Eevee", ParkCommand.Play, roll, game), StringComparison.OrdinalIgnoreCase);
        // The ball itself still gets its lines.
        Assert.Contains("ball", ParkLore.RefusalLine(ParkTemperament.Aloof, "Eevee", ParkCommand.Play, 0));
    }

    [Fact]
    public void Zorua_frames_the_innocent()
    {
        var life = new ParkLife(Home, [
            new("zorua", "Zorua", 570, [ParkType.Dark], ParkPersonality.Playful),
            new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm),
            new("pikachu", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Calm)], seed: 2);
        var framed = Run(life, 2 * 60 * 60 * 1000).FirstOrDefault(m => m.Text?.Contains("never pulled") == true);
        Assert.NotNull(framed);
        Assert.DoesNotContain("zorua", new[] { framed!.FirstId, framed.SecondId });
    }

    [Fact]
    public void Who_counts_as_a_troll()
    {
        Assert.True(ParkLife.IsTroll(new("g", "Gengar", 94, [ParkType.Ghost], ParkPersonality.Calm)));
        Assert.True(ParkLife.IsTroll(new("s", "Sneasel", 215, [ParkType.Dark], ParkPersonality.Calm)));
        Assert.False(ParkLife.IsTroll(new("c", "Chansey", 113, [ParkType.Normal], ParkPersonality.Gentle)));
    }
}
