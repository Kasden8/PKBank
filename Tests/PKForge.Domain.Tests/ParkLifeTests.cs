using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>
/// The park's life over simulated hours: residents only stand where they can, swimmers swim,
/// everyone gets round to the varied activities, and shared moments untangle cleanly.
/// </summary>
public sealed class ParkLifeTests
{
    private static readonly ParkMap Home = ParkMap.Park;

    private static ParkLifeResident[] Residents() =>
    [
        new("pikachu", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Playful),
        new("squirtle", "Squirtle", 7, [ParkType.Water], ParkPersonality.Calm),
        new("bulbasaur", "Bulbasaur", 1, [ParkType.Grass, ParkType.Poison], ParkPersonality.Gentle),
        new("charmander", "Charmander", 4, [ParkType.Fire], ParkPersonality.Bold),
        new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Curious),
        new("meowth", "Meowth", 52, [ParkType.Normal], ParkPersonality.Proud),
        new("piplup", "Piplup", 393, [ParkType.Water], ParkPersonality.Energetic),
        new("gastly", "Gastly", 92, [ParkType.Ghost, ParkType.Poison], ParkPersonality.Shy),
        new("riolu", "Riolu", 447, [ParkType.Fighting], ParkPersonality.Balanced),
        new("turtwig", "Turtwig", 387, [ParkType.Grass], ParkPersonality.Helpful),
        new("magikarp", "Magikarp", 129, [ParkType.Water], ParkPersonality.Energetic),
    ];

    [Fact]
    public void An_afternoon_in_the_park()
    {
        var life = new ParkLife(Home, Residents(), seed: 7);
        var poses = new HashSet<ParkPose>();
        var activities = new HashSet<string>();
        var swam = false;
        var together = 0;
        for (long t = 0; t <= 3 * 60 * 60 * 1000; t += 200)
        {
            life.Advance(t);
            foreach (var actor in life.Actors)
            {
                var allowed = actor.Resident.Traversal;
                // A flyer in the air may be over anything.
                Assert.True(actor.InFlight || actor.Riding || Home.CanOccupy(actor.X, actor.Y, allowed, ParkNavigator.Radius - .5f),
                    $"{actor.Resident.Name} stands on {Home.TerrainAt(actor.X, actor.Y)} at {actor.X},{actor.Y} ({actor.Activity}, {t} ms)");
                poses.Add(actor.Pose);
                activities.Add(actor.Activity.Split(' ')[0]);
                swam |= life.IsSwimming(actor);
                if (actor.PartnerId is not null) together++;
            }
        }
        foreach (var pose in new[] { ParkPose.Walk, ParkPose.Sleep, ParkPose.Eat, ParkPose.Sit, ParkPose.Nod, ParkPose.Attack, ParkPose.Hurt, ParkPose.LookUp, ParkPose.Float })
            Assert.Contains(pose, poses);
        Assert.True(swam);
        Assert.True(together > 0);
        Assert.True(activities.Count >= 10, string.Join(", ", activities));
    }

    [Fact]
    public void Resting_residents_keep_their_distance()
    {
        var life = new ParkLife(Home, Residents(), seed: 3);
        int samples = 0, close = 0;
        for (long t = 0; t <= 60 * 60 * 1000; t += 1000)
        {
            life.Advance(t);
            var resting = life.Actors.Where(a => !a.IsWalking && a.PartnerId is null).ToArray();
            foreach (var a in resting)
            foreach (var b in resting.Where(b => string.CompareOrdinal(a.Resident.Id, b.Resident.Id) < 0))
            {
                samples++;
                if (MathF.Abs(a.X - b.X) < 8 && MathF.Abs(a.Y - b.Y) < 6) close++;
            }
        }
        Assert.True(close <= samples / 50, $"{close} of {samples} pairs on top of each other");
    }

    [Fact]
    public void A_snack_from_the_player_comes_first()
    {
        var life = new ParkLife(Home, Residents(), seed: 1);
        life.Advance(30_000);
        life.Command("pikachu", ParkCommand.Snack);
        life.Advance(30_200);
        var pikachu = life["pikachu"]!;
        Assert.Equal(ParkPose.Eat, pikachu.Pose);
        Assert.Equal(ParkProp.Berry, pikachu.Prop);
        Assert.Equal("Delighted", pikachu.Mood);
    }

    [Fact]
    public void Interrupting_a_shared_moment_frees_both()
    {
        var life = new ParkLife(Home, Residents(), seed: 5);
        ParkActor? busy = null;
        for (long t = 0; t < 2 * 60 * 60 * 1000 && busy is null; t += 500)
        {
            life.Advance(t);
            busy = life.Actors.FirstOrDefault(a => a.PartnerId is not null);
        }
        Assert.NotNull(busy);
        var partner = life[busy!.PartnerId!]!;
        life.Command(busy.Resident.Id, ParkCommand.Talk);
        Assert.Null(busy.PartnerId);
        Assert.Null(partner.PartnerId);
    }

    [Fact]
    public void Leaving_residents_free_their_partners()
    {
        var life = new ParkLife(Home, Residents(), seed: 5);
        ParkActor? busy = null;
        for (long t = 0; t < 2 * 60 * 60 * 1000 && busy is null; t += 500)
        {
            life.Advance(t);
            busy = life.Actors.FirstOrDefault(a => a.PartnerId is not null);
        }
        var partnerId = busy!.PartnerId!;
        life.SetResidents(Residents().Where(r => r.Id != busy.Resident.Id));
        Assert.Null(life[busy.Resident.Id]);
        Assert.Null(life[partnerId]!.PartnerId);
        Assert.Equal(10, life.Actors.Count);
    }
}

public sealed class ParkWaterBoundTests
{
    [Fact]
    public void A_fish_never_leaves_the_lake()
    {
        var home = ParkMap.Park;
        var life = new ParkLife(home, [new("karp", "Magikarp", 129, [ParkType.Water], ParkPersonality.Playful),
            new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Gentle)], seed: 2);
        for (long t = 0; t <= 60 * 60 * 1000; t += 250)
        {
            life.Advance(t);
            var karp = life["karp"]!;
            Assert.Equal(ParkTerrain.Water, home.TerrainAt(karp.X, karp.Y));
        }
    }
}
