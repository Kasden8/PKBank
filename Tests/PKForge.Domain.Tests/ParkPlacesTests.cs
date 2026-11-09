using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>The park is one map: its places name every point, every spot is reachable open
/// ground, and residents live an hour in it without ever standing where they can't.</summary>
public sealed class ParkPlacesTests
{
    private static readonly ParkMap Map = ParkMap.Park;

    private static ParkLifeResident[] Residents() =>
    [
        new("pikachu", "Pikachu", 25, [ParkType.Electric], ParkPersonality.Playful),
        new("charmander", "Charmander", 4, [ParkType.Fire], ParkPersonality.Bold),
        new("squirtle", "Squirtle", 7, [ParkType.Water], ParkPersonality.Calm),
        new("magikarp", "Magikarp", 129, [ParkType.Water], ParkPersonality.Energetic),
        new("geodude", "Geodude", 74, [ParkType.Rock, ParkType.Ground], ParkPersonality.Gentle),
        new("pidgey", "Pidgey", 16, [ParkType.Normal, ParkType.Flying], ParkPersonality.Shy),
        new("gengar", "Gengar", 94, [ParkType.Ghost, ParkType.Poison], ParkPersonality.Playful),
        new("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Curious),
    ];

    [Fact]
    public void The_map_is_whole_tiles_with_the_home_park_inside()
    {
        Assert.True(Map.Width % 16 == 0 && Map.Height % 16 == 0, $"{Map.Width}×{Map.Height}");
        Assert.Equal("home", Map.RegionAt(Map.AtHome(168, 150).X, Map.AtHome(168, 150).Y).Id);
        Assert.Equal(ParkTerrain.Land, Map.TerrainAt(Map.AtHome(168, 150).X, Map.AtHome(168, 150).Y));
    }

    [Fact]
    public void Every_place_has_its_own_id_and_the_last_covers_the_map()
    {
        Assert.Equal(Map.Regions.Count, Map.Regions.Select(r => r.Id).Distinct().Count());
        var last = Map.Regions[^1];
        Assert.True(last.Left == 0 && last.Top == 0 && last.Width == Map.Width && last.Height == Map.Height);
        foreach (var region in Map.Regions)
            Assert.True(region.Left >= 0 && region.Top >= 0 && region.Left + region.Width <= Map.Width && region.Top + region.Height <= Map.Height, region.Name);
    }

    [Fact]
    public void Every_spot_is_open_ground_reachable_from_the_home_park()
    {
        var land = ParkNavigator.For(Map, ParkTraversal.Land);
        var heart = Map.AtHome(168, 150);
        Assert.Contains(Map.Spots, s => s.Kind == ParkSpotKind.Daycare);
        foreach (var spot in Map.Spots)
        {
            Assert.True(Map.CanOccupy(spot.X, spot.Y, ParkTraversal.Land), $"{spot.Kind} at {spot.X},{spot.Y}");
            Assert.True(land.Connected(heart, new(spot.X, spot.Y)), $"{spot.Kind} at {spot.X},{spot.Y} ({Map.RegionAt(spot.X, spot.Y).Name})");
        }
    }

    [Fact]
    public void Every_place_but_the_meadow_has_somewhere_to_nap_or_hide()
    {
        // The open sea is water: nobody naps there.
        foreach (var region in Map.Regions.Take(Map.Regions.Count - 1).Where(r => Map.TerrainAt(r.Center.X, r.Center.Y) != ParkTerrain.Water))
            Assert.True(Map.Spots.Any(s => region.Contains(s.X, s.Y) && s.Kind is ParkSpotKind.Nap or ParkSpotKind.Hide), region.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void An_hour_in_the_park(int index)
    {
        var map = Map;
        foreach (var phase in new[] { ParkDayPhase.Day, ParkDayPhase.Evening })
        {
            var life = new ParkLife(map, Residents(), seed: index + 3) { Phase = phase, Today = new DateOnly(2026, 10, 6) };
            for (long t = 0; t <= 60 * 60 * 1000; t += 250)
            {
                life.Advance(t);
                foreach (var actor in life.Actors)
                    if (!actor.Indoors && !actor.InFlight && !actor.Riding)
                        Assert.True(map.CanOccupy(actor.X, actor.Y, actor.Resident.Traversal, ParkNavigator.Radius - .5f),
                            $"{map.Id}: {actor.Resident.Name} on {map.TerrainAt(actor.X, actor.Y)} at {actor.X},{actor.Y} ({actor.Activity}, {t} ms)");
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Every_game_can_be_played_or_politely_declined(int index)
    {
        var map = Map;
        foreach (var kind in new[] { ParkGameKind.HideAndSeek, ParkGameKind.Race, ParkGameKind.Brush, ParkGameKind.Spar })
        {
            var residents = Residents().Append(new ParkLifeResident("lucario", "Lucario", 448, [ParkType.Fighting, ParkType.Steel], ParkPersonality.Bold)).ToArray();
            var life = new ParkLife(map, residents, seed: index * 7 + (int)kind) { Today = new DateOnly(2026, 10, 6) };
            life.Restore(residents.Select(r => new ParkCareState(r.Id, Bond: 90)), DateTimeOffset.Now);
            life.Advance(0);
            var host = kind == ParkGameKind.Spar ? "lucario" : life.Actors.First(a => !a.Resident.WaterBound).Resident.Id;
            var reply = life.StartGame(kind, host);
            Assert.NotNull(reply.Line);
            for (long t = 250; t <= 3 * 60 * 1000; t += 250)
            {
                life.Advance(t);
                foreach (var actor in life.Actors)
                    if (!actor.Indoors && !actor.InFlight && !actor.Riding)
                        Assert.True(map.CanOccupy(actor.X, actor.Y, actor.Resident.Traversal, ParkNavigator.Radius - .5f),
                            $"{map.Id} {kind}: {actor.Resident.Name} at {actor.X},{actor.Y} ({actor.Activity})");
            }
            life.EndGame();
        }
    }

    [Fact]
    public void The_keepers_stand_by_the_daycare_door()
    {
        var map = Map;
        var life = new ParkLife(map, [new ParkLifeResident("eevee", "Eevee", 133, [ParkType.Normal], ParkPersonality.Calm)], seed: 1) { DaycareOpen = true };
        life.Advance(0); life.Advance(250);
        var door = map.Spots.First(s => s.Kind == ParkSpotKind.Daycare);
        var blissey = life.Actors.Single(a => a.Resident.Name == "Blissey");
        Assert.True(MathF.Abs(blissey.X - door.X) < 32 && MathF.Abs(blissey.Y - door.Y) < 32, $"{blissey.X},{blissey.Y}");
    }
}
