using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>The famous things Pokémon do, each touching the others.</summary>
public sealed class ParkIconTests
{
    private static readonly ParkMap Park = ParkMap.Park;

    private static ParkLifeResident Mon(string name, int species, params int[] types) =>
        new(name.ToLowerInvariant(), name, species, types, ParkPersonality.Curious);

    private static (ParkLife Life, List<string> Journal, HashSet<string> Labels) Run(ParkLifeResident[] residents, int hours = 3,
        ParkDayPhase phase = ParkDayPhase.Day, Func<ParkLife, bool>? until = null, int seed = 5)
    {
        var life = new ParkLife(Park, residents, seed) { Today = new DateOnly(2026, 10, 15), Phase = phase, Weather = ParkWeather.Sunny };
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
            journal.AddRange(life.TakeMoments().Where(m => m.Text is not null).Select(m => m.Text!));
            if (until?.Invoke(life) == true) break;
        }
        return (life, journal, labels);
    }

    [Fact]
    public void Lapras_ferries_those_who_cannot_swim()
    {
        var (_, journal, labels) = Run([Mon("Lapras", 131, ParkType.Water, ParkType.Ice), Mon("Pikachu", 25, ParkType.Electric),
            Mon("Charmander", 4, ParkType.Fire), Mon("Eevee", 133, ParkType.Normal)], hours: 4);
        Assert.Contains(labels, l => l.StartsWith("Riding across the water"));
        Assert.Contains(journal, l => l.Contains("across the water on its back"));
    }

    [Fact]
    public void Psyduck_forgets_it_cannot_swim_and_is_brought_back()
    {
        var (_, journal, _) = Run([Mon("Psyduck", 54, ParkType.Water), Mon("Squirtle", 7, ParkType.Water), Mon("Marill", 183, ParkType.Water, ParkType.Fairy)], hours: 4);
        Assert.Contains(journal, l => l.Contains("forgot it cannot swim"));
    }

    [Fact]
    public void Slowpoke_fishes_with_its_tail()
    {
        var (_, _, labels) = Run([Mon("Slowpoke", 79, ParkType.Water, ParkType.Psychic)], hours: 2);
        Assert.Contains("Fishing with its tail", labels);
    }

    [Fact]
    public void Croagunk_stops_the_lovestruck()
    {
        var (_, journal, _) = Run([Mon("Volbeat", 313, ParkType.Bug), Mon("Illumise", 314, ParkType.Bug), Mon("Croagunk", 453, ParkType.Poison, ParkType.Fighting)], hours: 4);
        Assert.Contains(journal, l => l.Contains("Poison Jab"));
    }

    [Fact]
    public void Snorlax_strips_the_berry_bush_bare()
    {
        var (_, _, labels) = Run([Mon("Snorlax", 143, ParkType.Normal), Mon("Bulbasaur", 1, ParkType.Grass), Mon("Oddish", 43, ParkType.Grass),
            Mon("Caterpie", 10, ParkType.Bug)], hours: 4);
        Assert.Contains("Eating every last berry", labels);
    }

    [Fact]
    public void Diglett_pops_up_under_a_nap()
    {
        var (_, journal, _) = Run([Mon("Diglett", 50, ParkType.Ground), Mon("Snorlax", 143, ParkType.Normal), Mon("Slakoth", 287, ParkType.Normal)], hours: 4);
        Assert.Contains(journal, l => l.Contains("popped up right in the middle"));
    }

    [Fact]
    public void Gengar_hides_in_a_shadow()
    {
        var (_, journal, labels) = Run([Mon("Gengar", 94, ParkType.Ghost, ParkType.Poison), Mon("Eevee", 133, ParkType.Normal), Mon("Pikachu", 25, ParkType.Electric)],
            hours: 3, phase: ParkDayPhase.Evening);
        Assert.Contains(labels, l => l.StartsWith("Hiding in"));
        Assert.Contains(journal, l => l.Contains("shadow"));
    }

    [Fact]
    public void Jigglypuff_draws_on_the_faces_of_sleepers()
    {
        var (life, journal, _) = Run([Mon("Jigglypuff", 39, ParkType.Normal, ParkType.Fairy), Mon("Pikachu", 25, ParkType.Electric),
            Mon("Eevee", 133, ParkType.Normal), Mon("Bulbasaur", 1, ParkType.Grass)], hours: 3,
            until: l => l.Actors.Any(a => a.DoodledUntil > l.Now));
        Assert.Contains(life.Actors, a => a.DoodledUntil > life.Now);
        Assert.Contains(journal, l => l.Contains("drew all over"));
    }

    [Fact]
    public void Mimikyu_droops_when_Pikachu_gets_patted()
    {
        var life = new ParkLife(Park, [Mon("Pikachu", 25, ParkType.Electric), Mon("Mimikyu", 778, ParkType.Ghost, ParkType.Fairy)], seed: 2)
            { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day };
        var t = 0L;
        // Bring them close, then pat Pikachu until one goes through.
        var seen = false;
        for (var i = 0; i < 60 && !seen; i++)
        {
            life.Advance(t += 5000);
            if (life.Pet("pikachu").Accepted)
            {
                life.Advance(t += 300);
                seen = life["mimikyu"]!.Activity.StartsWith("Watching Pikachu");
            }
        }
        Assert.True(seen);
    }

    [Fact]
    public void The_player_can_pull_Psyduck_out_of_the_water()
    {
        var life = new ParkLife(Park, [Mon("Psyduck", 54, ParkType.Water)], seed: 5) { Today = new DateOnly(2026, 10, 15), Phase = ParkDayPhase.Day };
        long t = 0;
        for (; t <= 4 * 60 * 60 * 1000L && life["psyduck"]!.Activity != "Forgot it cannot swim"; t += 200) life.Advance(t);
        Assert.Equal("Forgot it cannot swim", life["psyduck"]!.Activity);
        var reply = life.Command("psyduck", ParkCommand.Talk);
        Assert.Contains("pull", reply.Line);
        // It is brought back to the shore (later it may well wade in again).
        var psyduck = life["psyduck"]!;
        var out_ = false;
        for (var i = 0; i < 300 && !out_; i++) { life.Advance(t += 200); out_ = !life.IsSwimming(psyduck); }
        Assert.True(out_, $"{psyduck.X},{psyduck.Y} {psyduck.Activity}");
    }
}
