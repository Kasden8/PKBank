using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

/// <summary>
/// The park's collision grid follows the art (8 px cells), with spritedmistery's home park in the
/// middle of it; objects are cut into pieces for depth sorting, and walkers stay on open ground.
/// Points of the home park are given in its own pixels (ParkMap.AtHome).
/// </summary>
public sealed class ParkMapTests
{
    private static readonly ParkMap Park = ParkMap.Park;
    private static ParkPoint Home(float x, float y) => Park.AtHome(x, y);

    [Fact]
    public void The_park_is_whole_tiles()
    {
        Assert.Equal("park", Park.Id);
        Assert.Equal(1008, Park.Width);
        Assert.Equal(1376, Park.Height);
    }

    [Theory]
    [InlineData(168, 150, ParkTerrain.Land)]    // the path south of the statue
    [InlineData(168, 110, ParkTerrain.Blocked)] // the statue's plinth
    [InlineData(300, 180, ParkTerrain.Water)]   // the lake
    [InlineData(290, 40, ParkTerrain.Blocked)]  // the house
    [InlineData(132, 60, ParkTerrain.Blocked)]  // the arch's west post
    [InlineData(168, 40, ParkTerrain.Land)]     // under the arch
    public void Terrain_follows_the_home_park_art(float x, float y, ParkTerrain expected) =>
        Assert.Equal(expected, Park.TerrainAt(Home(x, y).X, Home(x, y).Y));

    [Theory]
    [InlineData(40, 30, ParkTerrain.Lava)]      // the volcano's lava lake
    [InlineData(500, 1300, ParkTerrain.Water)]  // the sea
    [InlineData(504, 150, ParkTerrain.Land)]    // the shrine's path
    [InlineData(886, 300, ParkTerrain.Land)]    // the mountain's lower stairs
    [InlineData(-1, 40, ParkTerrain.Blocked)]
    public void Terrain_follows_the_art(float x, float y, ParkTerrain expected) =>
        Assert.Equal(expected, Park.TerrainAt(x, y));

    [Fact]
    public void Land_walkers_cannot_step_into_the_lake_but_swimmers_can()
    {
        var lake = Home(300, 180);
        Assert.False(Park.CanOccupy(lake.X, lake.Y, ParkTraversal.Land));
        Assert.True(Park.CanOccupy(lake.X, lake.Y, ParkTraversal.Land | ParkTraversal.Water));
    }

    [Fact]
    public void Feet_must_fit_inside_the_map()
    {
        Assert.False(Park.CanOccupy(2, 1000, ParkTraversal.Land | ParkTraversal.Water));
        Assert.True(Park.CanOccupy(Home(20, 100).X, Home(20, 100).Y, ParkTraversal.Land));
    }

    [Fact]
    public void Rows_of_different_lengths_are_rejected() =>
        Assert.Throws<ArgumentException>(() => new ParkMap("bad", "..", "..."));

    [Fact]
    public void Routes_go_around_obstacles_and_stay_on_land()
    {
        var navigator = ParkNavigator.For(Park, ParkTraversal.Land);
        // From the home park's west path, past the statue, to the shore.
        var start = Home(20, 100);
        var route = navigator.Route(start, Home(300, 140));
        Assert.NotNull(route);
        var from = start;
        foreach (var leg in route!)
        {
            Assert.True(navigator.Clear(from, leg), $"{from} to {leg}");
            from = leg;
        }
        Assert.Equal(Home(300, 140), route[^1]);
        // A land walker sent into the lake stops on the shore.
        var shore = navigator.Route(start, Home(300, 180))![^1];
        Assert.Equal(ParkTerrain.Land, Park.TerrainAt(shore.X, shore.Y));
        Assert.NotNull(ParkNavigator.For(Park, ParkTraversal.Land | ParkTraversal.Water).Route(start, Home(300, 180)));
    }

    [Fact]
    public void Routes_cross_the_park_from_the_volcano_to_the_beach()
    {
        var navigator = ParkNavigator.For(Park, ParkTraversal.Land);
        var volcano = Park.Spots.First(s => s.Kind == ParkSpotKind.Statue && Park.RegionAt(s.X, s.Y).Id == "crater");
        var pier = Park.Spots.First(s => s.Kind == ParkSpotKind.Gate && Park.RegionAt(s.X, s.Y).Id == "coast");
        var route = navigator.Route(new(volcano.X, volcano.Y), new(pier.X, pier.Y));
        Assert.NotNull(route);
        Assert.Equal(new ParkPoint(pier.X, pier.Y), route![^1]);
    }

    [Fact]
    public void Objects_split_into_opaque_pieces_and_leave_shadows_out()
    {
        // Two blocks touching only by a corner form one piece; a soft shadow pixel joins nothing.
        const int w = 6, h = 4;
        var alpha = new byte[w * h];
        void Set(int x, int y, byte a) => alpha[y * w + x] = a;
        Set(0, 0, 255); Set(1, 1, 255);
        Set(4, 2, 255); Set(4, 3, 255);
        Set(5, 3, 90);
        var pieces = ParkLayerCut.Split(alpha, w, h);
        Assert.Equal(2, pieces.Count);
        Assert.Equal((0, 0, 2, 2, 2), (pieces[0].Left, pieces[0].Top, pieces[0].Width, pieces[0].Height, pieces[0].Base));
        Assert.Equal((4, 2, 1, 2, 4), (pieces[1].Left, pieces[1].Top, pieces[1].Width, pieces[1].Height, pieces[1].Base));
        Assert.DoesNotContain(3 * w + 5, pieces.SelectMany(p => p.Pixels));
    }

    [Fact]
    public void With_a_depth_map_touching_objects_keep_their_own_bases()
    {
        // A tree crown (base 3) touching a fence in front of it (base 4): two pieces, not one.
        const int w = 4, h = 4;
        var alpha = new byte[w * h];
        var bases = new int[w * h];
        for (var x = 0; x < 3; x++) { alpha[x] = 255; bases[x] = 3; alpha[w + x] = 255; bases[w + x] = 3; }
        for (var x = 0; x < 4; x++) { alpha[2 * w + x] = 255; bases[2 * w + x] = 4; }
        var pieces = ParkLayerCut.Split(alpha, w, h, bases);
        Assert.Equal([3, 4], pieces.Select(p => p.Base).Order());
        Assert.Equal(6, pieces.Single(p => p.Base == 3).Pixels.Length);
    }
}

/// <summary>The park camera on the Thor's map area: whole-number scales, pinch around the
/// fingers, drags that stop at the map's edges, and following the selected Pokémon.</summary>
public sealed class ParkViewportTests
{
    private static ParkViewport Thor()
    {
        var viewport = new ParkViewport(336, 192);
        viewport.Resize(1856, 845);
        return viewport;
    }

    [Fact]
    public void Shows_about_a_home_park_across_by_default_and_never_zooms_out_past_filling_the_view()
    {
        var viewport = Thor();
        Assert.Equal(6, viewport.Scale);
        Assert.Equal(6, viewport.MinScale);
        Assert.Equal(12, viewport.MaxScale);
        var park = new ParkViewport(1008, 1376);
        park.Resize(1856, 845);
        Assert.Equal(6, park.Scale);
        Assert.Equal(2, park.MinScale);
    }

    [Fact]
    public void A_stick_glides_across_the_map_and_zooms_smoothly_then_settles_on_a_whole_scale()
    {
        var park = new ParkViewport(1008, 1376);
        park.Resize(1856, 845);
        park.Follow(500, 700);
        park.Stick(1, 0, 0);
        Assert.False(park.Following);
        Assert.True(park.IsMoving);
        for (var i = 0; i < 25; i++) park.Step(20);
        var moved = park.CenterX - 500;
        // Half a second at full tilt: about half a view across (1.1 views a second).
        Assert.InRange(moved, 1856 / 6f * .5f, 1856 / 6f * .6f);
        park.Stick(.5f, 0, 0);
        var before = park.CenterX;
        park.Step(100);
        Assert.InRange(park.CenterX - before, moved / 5 * .4f, moved / 5 * .6f);
        park.Stick(0, 0, 0);
        before = park.CenterX;
        park.Step(100);
        Assert.Equal(before, park.CenterX);

        park.Stick(0, 0, 1); // down: out
        for (var i = 0; i < 10; i++) park.Step(16);
        var between = park.Scale;
        Assert.True(between < 6 && between > 2, $"{between}");
        Assert.NotEqual(MathF.Round(between), between);
        park.Stick(0, 0, 0);
        for (var i = 0; i < 60; i++) park.Step(16);
        Assert.Equal(MathF.Round(park.Scale), park.Scale);
        Assert.False(park.IsMoving);
    }

    [Fact]
    public void Flying_to_a_place_glides_there_and_stays()
    {
        var park = new ParkViewport(1008, 1376);
        park.Resize(1856, 845);
        park.Follow(500, 800);
        park.FlyTo(100, 200);
        Assert.False(park.Following);
        for (var i = 0; i < 200; i++) park.Step(16);
        Assert.False(park.IsMoving);
        var (left, top) = park.Corner;
        Assert.True(left == 0 || MathF.Abs(park.CenterX - 100) < 1, $"{park.CenterX}");
        Assert.True(MathF.Abs(park.CenterY - 200) < 1, $"{park.CenterY}");
    }

    [Fact]
    public void Pinching_keeps_the_point_under_the_fingers()
    {
        var viewport = Thor();
        var before = viewport.ToMap(600, 300);
        viewport.BeginPinch();
        viewport.Pinch(1.5f, 600, 300);
        Assert.Equal(9, viewport.Scale);
        var after = viewport.ToMap(600, 300);
        Assert.InRange(after.X - before.X, -.5f, .5f);
        Assert.InRange(after.Y - before.Y, -.5f, .5f);
        Assert.False(viewport.Following);
    }

    [Fact]
    public void A_pinch_zooms_smoothly_then_settles_on_a_whole_scale()
    {
        var viewport = Thor();
        viewport.BeginPinch();
        viewport.Pinch(1.25f, 600, 300);
        Assert.Equal(7.5f, viewport.Scale, .01f);   // in between while the fingers move
        viewport.EndPinch();
        var seen = new List<float>();
        for (var i = 0; i < 40; i++) { viewport.Step(16); seen.Add(viewport.Scale); }
        Assert.Equal(8, viewport.Scale);             // then a whole scale, so the pixels stay square
        Assert.True(seen.Distinct().Count() > 3, "it should glide, not jump");
        Assert.False(viewport.IsMoving);
    }

    [Fact]
    public void The_zoom_buttons_glide_one_step()
    {
        var viewport = Thor();
        viewport.Zoom(1);
        viewport.Step(16);
        Assert.InRange(viewport.Scale, 6.01f, 6.99f);
        for (var i = 0; i < 40; i++) viewport.Step(16);
        Assert.Equal(7, viewport.Scale);
        viewport.Zoom(-5);
        for (var i = 0; i < 40; i++) viewport.Step(16);
        Assert.Equal(viewport.MinScale, viewport.Scale);
    }

    [Fact]
    public void Zooming_out_never_shows_outside_the_map()
    {
        var viewport = Thor();
        viewport.BeginPinch();
        viewport.Pinch(.1f, 900, 400);
        Assert.Equal(6, viewport.Scale);
        var (left, top) = viewport.Corner;
        Assert.True(left >= 0 && top >= 0 && left + 1856 / 6f <= 336 && top + 845 / 6f <= 192, $"{left},{top}");
    }

    [Fact]
    public void Dragging_stops_at_the_edge_without_building_up()
    {
        var viewport = Thor();
        viewport.Drag(100_000, 100_000);
        Assert.Equal((0f, 0f), viewport.Corner);
        viewport.Drag(-60, 0);
        Assert.Equal(10f, viewport.Corner.Left);
    }

    [Fact]
    public void A_fling_glides_then_stops()
    {
        var viewport = Thor();
        var start = viewport.CenterX;
        viewport.Fling(-1, 0);
        for (var i = 0; i < 300; i++) viewport.Step(16);
        Assert.True(viewport.CenterX > start);
        Assert.False(viewport.IsMoving);
    }

    [Fact]
    public void Reset_follows_again_at_the_default_scale()
    {
        var viewport = Thor();
        viewport.Zoom(3);
        for (var i = 0; i < 40; i++) viewport.Step(16);
        viewport.Drag(50, 0);
        viewport.Track(300, 150, 1000);
        Assert.Equal(9, viewport.Scale);
        viewport.Reset();
        for (var i = 0; i < 40; i++) viewport.Step(16);
        viewport.Track(300, 150, 1000);
        Assert.Equal(6, viewport.Scale);
        Assert.True(viewport.Following);
        Assert.Equal(336 - 1856 / 6f / 2, viewport.CenterX, .01f);
    }
}
