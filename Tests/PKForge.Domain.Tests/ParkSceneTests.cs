using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>Scenes acted out together: formations, the Eevee family's picnic, sparring with a
/// crowd, the Hitmons training, poses on the heights, loners and their one friend.</summary>
public sealed class ParkSceneTests
{
    private static readonly ParkMap Park = ParkMap.Park;

    private static ParkLifeResident Mon(string id, int species, ParkPersonality personality, params int[] types) =>
        new(id, id, species, types, personality);

    // Runs the park and returns the journal lines and every activity label seen.
    private static (List<string> Journal, HashSet<string> Labels) Run(ParkLifeResident[] residents, int hours = 2, int seed = 3,
        Action<ParkLife, long>? check = null, ParkDayPhase phase = ParkDayPhase.Day)
    {
        var life = new ParkLife(Park, residents, seed) { Today = new DateOnly(2026, 10, 6), Phase = phase, Weather = ParkWeather.Sunny };
        var journal = new List<string>();
        var labels = new HashSet<string>();
        for (long t = 0; t <= hours * 60 * 60 * 1000L; t += 200)
        {
            life.Advance(t);
            foreach (var actor in life.Actors)
            {
                labels.Add(actor.Activity);
                Assert.True(actor.InFlight || actor.Riding || Park.CanOccupy(actor.X, actor.Y, actor.Resident.Traversal, ParkNavigator.Radius - .5f),
                    $"{actor.Resident.Name} at {actor.X},{actor.Y} ({actor.Activity})");
            }
            check?.Invoke(life, t);
            journal.AddRange(life.TakeMoments().Where(m => m.Text is not null).Select(m => m.Text!));
        }
        return (journal, labels);
    }

    [Fact]
    public void Three_Magnemite_click_into_a_Magneton_for_a_moment()
    {
        var (journal, labels) = Run([
            Mon("m1", 81, ParkPersonality.Curious, ParkType.Electric, ParkType.Steel),
            Mon("m2", 81, ParkPersonality.Calm, ParkType.Electric, ParkType.Steel),
            Mon("m3", 81, ParkPersonality.Playful, ParkType.Electric, ParkType.Steel)]);
        Assert.Contains("Clicked into a Magneton (for now)", labels);
        Assert.Contains(journal, line => line.Contains("Magneton"));
    }

    [Fact]
    public void Three_Diglett_bob_in_a_row_like_a_Dugtrio()
    {
        var (journal, labels) = Run([
            Mon("d1", 50, ParkPersonality.Curious, ParkType.Ground),
            Mon("d2", 50, ParkPersonality.Energetic, ParkType.Ground),
            Mon("d3", 50, ParkPersonality.Playful, ParkType.Ground)]);
        Assert.Contains("Pretending to be a Dugtrio", labels);
        Assert.Contains(journal, line => line.Contains("Dugtrio"));
    }

    [Fact]
    public void Two_Magnemite_make_no_Magneton()
    {
        var (_, labels) = Run([
            Mon("m1", 81, ParkPersonality.Curious, ParkType.Electric, ParkType.Steel),
            Mon("m2", 81, ParkPersonality.Calm, ParkType.Electric, ParkType.Steel)], hours: 1);
        Assert.DoesNotContain("Clicked into a Magneton (for now)", labels);
    }

    [Fact]
    public void The_Eevee_family_has_a_picnic()
    {
        var (journal, labels) = Run([
            Mon("eevee", 133, ParkPersonality.Playful, ParkType.Normal),
            Mon("espeon", 196, ParkPersonality.Calm, ParkType.Psychic),
            Mon("sylveon", 700, ParkPersonality.Gentle, ParkType.Fairy)]);
        Assert.Contains("Having a picnic with the family", labels);
        Assert.Contains(journal, line => line.Contains("picnic") || line.Contains("shared berries"));
    }

    [Fact]
    public void Brawlers_spar_in_a_ring_and_a_crowd_watches()
    {
        var (journal, labels) = Run([
            Mon("incineroar", 727, ParkPersonality.Proud, ParkType.Fire, ParkType.Dark),
            Mon("primeape", 57, ParkPersonality.Bold, ParkType.Fighting),
            Mon("pikachu", 25, ParkPersonality.Playful, ParkType.Electric),
            Mon("bulbasaur", 1, ParkPersonality.Gentle, ParkType.Grass, ParkType.Poison)]);
        Assert.Contains(labels, label => label.StartsWith("Sparring with"));
        Assert.Contains(journal, line => line.Contains("spar"));
    }

    [Fact]
    public void The_Hitmons_train_together()
    {
        var (journal, labels) = Run([
            Mon("lee", 106, ParkPersonality.Bold, ParkType.Fighting),
            Mon("chan", 107, ParkPersonality.Proud, ParkType.Fighting),
            Mon("top", 237, ParkPersonality.Energetic, ParkType.Fighting)]);
        Assert.Contains("Training with the Hitmons", labels);
        Assert.Contains(journal, line => line.Contains("trained together"));
    }

    [Fact]
    public void The_cool_ones_pose_on_the_heights()
    {
        var posedHigh = false;
        var (_, labels) = Run([Mon("lucario", 448, ParkPersonality.Proud, ParkType.Fighting, ParkType.Steel)], check: (life, _) =>
        {
            var lucario = life["lucario"]!;
            if (lucario.Activity.StartsWith("Striking a dramatic pose") && lucario.Pose == ParkPose.Pose)
            {
                Assert.True(Park.RegionAt(lucario.X, lucario.Y).High, $"posing at {lucario.X},{lucario.Y}");
                posedHigh = true;
            }
        });
        Assert.True(posedHigh, string.Join(", ", labels));
    }

    [Fact]
    public void Two_cool_ones_stare_each_other_down()
    {
        var (journal, labels) = Run([
            Mon("lucario", 448, ParkPersonality.Proud, ParkType.Fighting, ParkType.Steel),
            Mon("greninja", 658, ParkPersonality.Bold, ParkType.Water, ParkType.Dark)], hours: 4);
        Assert.Contains(labels, label => label.StartsWith("Staring down"));
        Assert.Contains(journal, line => line.Contains("stared each other down"));
    }

    [Fact]
    public void A_loner_finds_the_quietest_corner()
    {
        var crowd = new[] { 25, 1, 4, 7, 39, 52 }.Select((s, i) => Mon($"c{i}", s, ParkPersonality.Playful, ParkType.Normal)).ToArray();
        var quiet = new List<float>();
        Run([Mon("absol", 359, ParkPersonality.Calm, ParkType.Dark), .. crowd], hours: 2, check: (life, _) =>
        {
            var absol = life["absol"]!;
            if (absol.Activity == "Alone with its thoughts" && absol.Pose == ParkPose.Sit)
                quiet.Add(life.Actors.Where(a => a != absol && a.PartnerId != "absol").Min(a => MathF.Sqrt((a.X - absol.X) * (a.X - absol.X) + (a.Y - absol.Y) * (a.Y - absol.Y))));
        });
        Assert.NotEmpty(quiet);
        Assert.True(quiet.Average() > 40, $"{quiet.Average()}");
    }

    [Fact]
    public void Shiny_ones_glint()
    {
        var life = new ParkLife(Park, [new ParkLifeResident("s", "s", 25, [ParkType.Electric], ParkPersonality.Calm, Shiny: true),
            new ParkLifeResident("n", "n", 25, [ParkType.Electric], ParkPersonality.Calm)], seed: 1) { Phase = ParkDayPhase.Day };
        var shines = new List<ParkEffect>();
        for (long t = 0; t <= 120_000; t += 200)
        {
            life.Advance(t);
            shines.AddRange(life.Effects.Where(e => e.Kind == ParkEffectKind.Shine && !shines.Contains(e)));
        }
        // About every 18 seconds, for the shiny one only.
        Assert.InRange(shines.Count, 4, 8);
    }

    [Fact]
    public void The_Eevee_family_shows_Eevee_what_it_could_become()
    {
        var (journal, labels) = Run([
            Mon("eevee", 133, ParkPersonality.Curious, ParkType.Normal),
            Mon("vaporeon", 134, ParkPersonality.Calm, ParkType.Water),
            Mon("jolteon", 135, ParkPersonality.Energetic, ParkType.Electric),
            Mon("flareon", 136, ParkPersonality.Bold, ParkType.Fire)], hours: 3);
        Assert.Contains("Showing off for the family", labels);
        Assert.Contains("Watching the family show off", labels);
        Assert.Contains(journal, line => line.Contains("cannot decide what it wants to be"));
    }

    [Fact]
    public void The_Eevee_family_curls_up_together_at_night()
    {
        var (journal, labels) = Run([
            Mon("eevee", 133, ParkPersonality.Curious, ParkType.Normal),
            Mon("espeon", 196, ParkPersonality.Calm, ParkType.Psychic),
            Mon("umbreon", 197, ParkPersonality.Calm, ParkType.Dark)], hours: 2, phase: ParkDayPhase.Night);
        Assert.Contains("Curled up with the family", labels);
        Assert.Contains(journal, line => line.Contains("one warm heap"));
    }

    [Fact]
    public void Everyone_gives_a_legendary_room_but_the_oblivious_walk_right_up()
    {
        var (journal, labels) = Run([
            Mon("mewtwo", 150, ParkPersonality.Proud, ParkType.Psychic),
            Mon("psyduck", 54, ParkPersonality.Calm, ParkType.Water),
            Mon("pikachu", 25, ParkPersonality.Playful, ParkType.Electric),
            Mon("bulbasaur", 1, ParkPersonality.Gentle, ParkType.Grass, ParkType.Poison),
            Mon("charmander", 4, ParkPersonality.Bold, ParkType.Fire),
            Mon("eevee", 133, ParkPersonality.Curious, ParkType.Normal)], hours: 3, check: (life, _) =>
            {
                // Nobody but the oblivious goes to say hello.
                foreach (var actor in life.Actors.Where(a => a.Resident.Id is not ("psyduck" or "mewtwo")))
                    Assert.DoesNotContain("hello to mewtwo", actor.Activity);
            });
        Assert.Contains(labels, label => label is "Making way for mewtwo" or "Keeping a respectful distance from mewtwo" or "Running away from mewtwo");
        Assert.Contains("Staring at mewtwo", labels);
        Assert.Contains(journal, line => line.StartsWith("psyduck walked straight up to mewtwo"));
    }

    [Fact]
    public void Family_and_friends_come_running_when_one_of_them_is_picked_on()
    {
        var seen = false;
        foreach (var seed in new[] { 3, 5, 8 })
        {
            var (journal, _) = Run([
                Mon("murkrow", 198, ParkPersonality.Proud, ParkType.Dark, ParkType.Flying),
                Mon("sneasel", 215, ParkPersonality.Bold, ParkType.Dark, ParkType.Ice),
                Mon("pichu", 172, ParkPersonality.Playful, ParkType.Electric),
                Mon("pikachu", 25, ParkPersonality.Playful, ParkType.Electric),
                Mon("raichu", 26, ParkPersonality.Bold, ParkType.Electric),
                Mon("eevee", 133, ParkPersonality.Curious, ParkType.Normal),
                Mon("vaporeon", 134, ParkPersonality.Calm, ParkType.Water),
                Mon("jolteon", 135, ParkPersonality.Energetic, ParkType.Electric)], hours: 3, seed: seed);
            if (journal.Any(line => line.Contains("came running"))) { seen = true; break; }
        }
        Assert.True(seen);
    }
}
