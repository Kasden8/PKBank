using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>Flyers: the true Flying types cross the park over whatever lies below, always come
/// down where they can stand and live on the Summit; a call brings them down first. The others,
/// insects and birds that cannot fly among them, keep their feet on the ground.</summary>
public sealed class ParkFlightTests
{
    private static readonly ParkMap Park = ParkMap.Park;

    private static ParkLife Life(params ParkLifeResident[] residents) =>
        new(Park, residents, seed: 4) { Today = new DateOnly(2026, 10, 6), Phase = ParkDayPhase.Day, Weather = ParkWeather.Sunny };

    private static readonly ParkLifeResident Pidgeot = new("pidgeot", "Pidgeot", 18, [ParkType.Normal, ParkType.Flying], ParkPersonality.Energetic);

    [Fact]
    public void A_flyer_crosses_what_it_cannot_walk_on_and_lands_on_open_ground()
    {
        var life = Life(Pidgeot);
        var land = ParkNavigator.For(Park, ParkTraversal.Land);
        int flights = 0, overBlocked = 0;
        var wasFlying = false;
        for (long t = 0; t <= 60 * 60 * 1000; t += 200)
        {
            life.Advance(t);
            var bird = life["pidgeot"]!;
            if (bird.InFlight)
            {
                Assert.True(bird.Altitude >= 0 && bird.Altitude < 60, $"{bird.Altitude}");
                if (!land.CanStand(bird.X, bird.Y)) overBlocked++;
            }
            else
            {
                Assert.Equal(0, bird.Altitude);
                Assert.True(Park.CanOccupy(bird.X, bird.Y, ParkTraversal.Land, ParkNavigator.Radius - .5f), $"landed at {bird.X},{bird.Y} ({bird.Activity})");
                if (wasFlying) flights++;
            }
            wasFlying = bird.InFlight;
        }
        Assert.True(flights >= 3, $"{flights} flights");
        Assert.Equal("summit", life.HomePlace(Pidgeot).Id);
        Assert.True(overBlocked > 0, "never flew over anything it could not walk on");
    }

    [Fact]
    public void A_call_brings_a_flyer_down_first()
    {
        var life = Life(Pidgeot);
        long t = 0;
        for (; t <= 60 * 60 * 1000 && !life["pidgeot"]!.InFlight; t += 200) life.Advance(t);
        var bird = life["pidgeot"]!;
        Assert.True(bird.InFlight, "it never took off");
        life.Advance(t += 600);
        Assert.True(bird.Altitude > 0);
        life.Command("pidgeot", ParkCommand.Talk);
        for (var i = 0; i < 150 && bird.InFlight; i++) life.Advance(t += 200);
        Assert.False(bird.InFlight);
        Assert.True(Park.CanOccupy(bird.X, bird.Y, ParkTraversal.Land, ParkNavigator.Radius - .5f), $"{bird.X},{bird.Y}");
    }

    [Theory]
    [InlineData("dragonite", 149, new[] { ParkType.Dragon, ParkType.Flying })]
    [InlineData("articuno", 144, new[] { ParkType.Ice, ParkType.Flying })]
    public void Every_true_flyer_flies_and_goes_home_to_the_summit(string id, int species, int[] types)
    {
        var life = Life(new ParkLifeResident(id, id, species, types, ParkPersonality.Energetic));
        var flew = false;
        for (long t = 0; t <= 30 * 60 * 1000 && !flew; t += 250)
        {
            life.Advance(t);
            flew |= life[id]!.InFlight;
        }
        Assert.True(flew, $"{id} never took off");
        Assert.Equal("summit", life.HomePlace(life[id]!.Resident).Id);
    }

    [Theory]
    [InlineData(6, new[] { ParkType.Fire, ParkType.Flying }, "crater")]       // Charizard: not in the snow
    [InlineData(279, new[] { ParkType.Water, ParkType.Flying }, "lighthouse")] // Pelipper: by the sea
    [InlineData(468, new[] { ParkType.Fairy, ParkType.Flying }, "shrine")]     // Togekiss
    [InlineData(144, new[] { ParkType.Ice, ParkType.Flying }, "summit")]       // Articuno
    public void Each_flyer_lives_on_the_height_that_suits_it(int species, int[] types, string height)
    {
        var resident = new ParkLifeResident("f", "f", species, types, ParkPersonality.Calm);
        var place = Life(resident).HomePlace(resident);
        Assert.Equal(height, place.Id);
        Assert.True(place.High);
    }

    [Theory]
    [InlineData("piplup", 393, new[] { ParkType.Water })]                    // no wings
    [InlineData("butterfree", 12, new[] { ParkType.Bug, ParkType.Flying })]  // flutters like a butterfly: an insect, not a flyer
    [InlineData("doduo", 84, new[] { ParkType.Normal, ParkType.Flying })]    // a bird that cannot fly
    public void Others_keep_their_feet_on_the_ground(string id, int species, int[] types)
    {
        var life = Life(new ParkLifeResident(id, id, species, types, ParkPersonality.Energetic));
        for (long t = 0; t <= 30 * 60 * 1000; t += 250)
        {
            life.Advance(t);
            Assert.False(life[id]!.InFlight, $"{id} took off");
        }
    }
}

/// <summary>The giants of the sea live out at sea, far from the shore, and dive.</summary>
public sealed class ParkSeaGiantTests
{
    [Theory]
    [InlineData("kyogre", 382)]
    [InlineData("wailord", 321)]
    public void A_giant_lives_out_at_sea_and_dives(string id, int species)
    {
        var park = ParkMap.Park;
        var life = new ParkLife(park, [new ParkLifeResident(id, id, species, [ParkType.Water], ParkPersonality.Calm)], seed: 2)
            { Today = new DateOnly(2026, 10, 6), Phase = ParkDayPhase.Day };
        int atSea = 0, samples = 0;
        var dived = false;
        for (long t = 0; t <= 60 * 60 * 1000; t += 500)
        {
            life.Advance(t);
            var giant = life[id]!;
            Assert.Equal(ParkTerrain.Water, park.TerrainAt(giant.X, giant.Y));
            dived |= giant.Submerged;
            samples++;
            if (park.RegionAt(giant.X, giant.Y).Id == "sea") atSea++;
        }
        Assert.Equal("sea", life.HomePlace(life[id]!.Resident).Id);
        Assert.True(dived, "it never dived");
        Assert.True(atSea > samples * .8, $"{atSea} of {samples} at sea");
    }
}
