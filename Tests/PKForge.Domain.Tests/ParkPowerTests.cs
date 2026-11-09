using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>Pokémon use what they can do in the park, and it shows.</summary>
public sealed class ParkPowerTests
{
    private static ParkLifeResident Mon(string name, int species, params int[] types) =>
        new(name.ToLowerInvariant(), name, species, types, ParkPersonality.Curious);

    // Runs until the check holds; the park's rules on where Pokémon may stand hold throughout.
    private static bool Until(ParkLifeResident[] residents, Func<ParkLife, bool> seen, double hours = 3, ParkDayPhase phase = ParkDayPhase.Day, int seed = 4)
    {
        var life = new ParkLife(ParkMap.Park, residents, seed) { Today = new DateOnly(2026, 10, 15), Phase = phase, Weather = ParkWeather.Sunny };
        for (long t = 0; t <= hours * 60 * 60 * 1000L; t += 200)
        {
            life.Advance(t);
            foreach (var a in life.Actors)
                Assert.True(a.InFlight || a.Riding || ParkMap.Park.CanOccupy(a.X, a.Y, a.Resident.Traversal, ParkNavigator.Radius - .5f),
                    $"{a.Resident.Name} at {a.X},{a.Y} ({a.Activity})");
            if (seen(life)) return true;
        }
        return false;
    }

    [Fact]
    public void A_Ghost_fades_out_of_sight_and_comes_back()
    {
        var faded = false; var back = false;
        Assert.True(Until([Mon("Gengar", 94, ParkType.Ghost, ParkType.Poison)], life =>
        {
            var gengar = life["gengar"]!;
            var opacity = gengar.OpacityAt(life.Now);
            if (opacity < .2f) faded = true;
            if (faded && opacity >= 1) back = true;
            return back;
        }));
    }

    [Fact]
    public void Abra_teleports()
    {
        ParkPoint? last = null; var jumped = false;
        Assert.True(Until([Mon("Abra", 63, ParkType.Psychic)], life =>
        {
            var abra = life["abra"]!;
            if (last is { } before && MathF.Abs(abra.X - before.X) + MathF.Abs(abra.Y - before.Y) > 60 && !abra.InFlight) jumped = true;
            last = new(abra.X, abra.Y);
            return jumped;
        }));
    }

    [Fact]
    public void Kecleon_blends_in() =>
        Assert.True(Until([Mon("Kecleon", 352, ParkType.Normal)], life => life["kecleon"]!.Camouflaged));

    [Fact]
    public void Squirtle_sprays_someone() =>
        Assert.True(Until([Mon("Squirtle", 7, ParkType.Water), Mon("Charmander", 4, ParkType.Fire), Mon("Eevee", 133, ParkType.Normal)],
            life => life.Effects.Any(e => e.Kind == ParkEffectKind.Spray)));

    [Fact]
    public void Machop_lifts_a_boulder() =>
        Assert.True(Until([Mon("Machop", 66, ParkType.Fighting)], life => life["machop"]!.Prop == ParkProp.Boulder));

    [Fact]
    public void Geodude_rolls_about() =>
        Assert.True(Until([Mon("Geodude", 74, ParkType.Rock, ParkType.Ground)], life => life["geodude"]!.Activity == "Rolling about" && life["geodude"]!.Pose == ParkPose.Spin));

    [Fact]
    public void Magikarp_leaps_out_of_the_water() =>
        Assert.True(Until([Mon("Magikarp", 129, ParkType.Water)], life => life["magikarp"]!.Altitude > 5));

    [Fact]
    public void Darkrai_gives_bad_dreams_and_Cresselia_soothes_them()
    {
        var soothed = false;
        // More than the cabin holds, so some sleep out under the stars.
        Assert.True(Until([Mon("Darkrai", 491, ParkType.Dark), Mon("Pikachu", 25, ParkType.Electric), Mon("Eevee", 133, ParkType.Normal),
            Mon("Bulbasaur", 1, ParkType.Grass), Mon("Meowth", 52, ParkType.Normal), Mon("Psyduck", 54, ParkType.Water), Mon("Oddish", 43, ParkType.Grass),
            Mon("Cresselia", 488, ParkType.Psychic)], life =>
        {
            soothed |= life["cresselia"]!.Activity == "Soothing the bad dreams";
            return soothed;
        }, hours: 4, phase: ParkDayPhase.Night));
    }

    [Fact]
    public void Gardevoir_protects_the_hunted()
    {
        var journal = new List<string>();
        Until([Mon("Heatmor", 631, ParkType.Fire), Mon("Durant", 632, ParkType.Bug, ParkType.Steel), Mon("Gardevoir", 282, ParkType.Psychic, ParkType.Fairy)], life =>
        {
            journal.AddRange(life.TakeMoments().Where(m => m.Text is not null).Select(m => m.Text!));
            return journal.Any(l => l.Contains("raised a barrier"));
        }, hours: 4);
        Assert.Contains(journal, l => l.Contains("raised a barrier"));
    }

    [Fact]
    public void Nobody_stays_invisible()
    {
        var since = new Dictionary<string, long>();
        Until([Mon("Gengar", 94, ParkType.Ghost, ParkType.Poison), Mon("Haunter", 93, ParkType.Ghost, ParkType.Poison), Mon("Abra", 63, ParkType.Psychic),
            Mon("Kecleon", 352, ParkType.Normal), Mon("Porygon", 137, ParkType.Normal), Mon("Pikachu", 25, ParkType.Electric)], life =>
        {
            foreach (var a in life.Actors)
            {
                if (a.OpacityAt(life.Now) < .5f || a.Camouflaged) since.TryAdd(a.Resident.Id, life.Now);
                else since.Remove(a.Resident.Id);
                if (since.TryGetValue(a.Resident.Id, out var from)) Assert.True(life.Now - from < 60_000, $"{a.Resident.Name} invisible for a minute ({a.Activity})");
            }
            return false;
        }, hours: 2);
    }

    [Fact]
    public void Fire_types_and_lures_glow_after_dark()
    {
        Assert.NotNull(ParkLife.GlowOf(Mon("Charmander", 4, ParkType.Fire)));
        Assert.NotNull(ParkLife.GlowOf(Mon("Lanturn", 171, ParkType.Water, ParkType.Electric)));
        Assert.Null(ParkLife.GlowOf(Mon("Bulbasaur", 1, ParkType.Grass)));
    }
}
