using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class MusicPlacesTests
{
    private static readonly MusicTrack[] Library = [new("a", "Route 1"), new("b", "Lavender Town"), new("c", "Ecruteak")];

    private static int[]? Playlist(MusicPlace place, Dictionary<MusicPlace, MusicSource> sources) =>
        MusicPlaces.Playlist(place, p => sources.GetValueOrDefault(p, MusicSource.Default), Library);

    [Fact]
    public void Menus_play_the_whole_library_and_the_park_follows_them_by_default()
    {
        Assert.Equal([0, 1, 2], Playlist(MusicPlace.Menus, [])!);
        Assert.Equal([0, 1, 2], Playlist(MusicPlace.ParkDay, [])!);
        var sources = new Dictionary<MusicPlace, MusicSource> { [MusicPlace.Menus] = MusicSource.Of(["b"]) };
        Assert.Equal([1], Playlist(MusicPlace.ParkNight, sources)!);
    }

    [Fact]
    public void Each_place_can_have_its_own_tracks_or_silence()
    {
        var sources = new Dictionary<MusicPlace, MusicSource>
        {
            [MusicPlace.Menus] = MusicSource.Silence,
            [MusicPlace.ParkDay] = MusicSource.Of(["c", "a"]),
            [MusicPlace.ParkNight] = MusicSource.AllTracks,
        };
        Assert.Null(Playlist(MusicPlace.Menus, sources));
        Assert.Equal([0, 2], Playlist(MusicPlace.ParkDay, sources)!);
        Assert.Equal([0, 1, 2], Playlist(MusicPlace.ParkNight, sources)!);
    }

    [Fact]
    public void Tracks_that_left_the_library_fall_back_on_the_default()
    {
        var sources = new Dictionary<MusicPlace, MusicSource>
        {
            [MusicPlace.Menus] = MusicSource.Of(["b"]),
            [MusicPlace.ParkDay] = MusicSource.Of(["gone"]),
        };
        Assert.Equal([1], Playlist(MusicPlace.ParkDay, sources)!);
    }

    [Fact]
    public void Sources_survive_a_round_trip_through_the_preferences()
    {
        foreach (var source in new[] { MusicSource.Default, MusicSource.AllTracks, MusicSource.Silence, MusicSource.Of(["a", "c"]) })
        {
            var back = MusicSource.Parse(source.Serialize());
            Assert.Equal(source.Kind, back.Kind);
            Assert.Equal(source.Tracks, back.Tracks);
        }
        Assert.Equal(MusicSourceKind.Default, MusicSource.Of([]).Kind);
        Assert.Equal("Same as menus", MusicPlaces.Describe(MusicPlace.ParkDay, MusicSource.Default, Library));
        Assert.Equal("Ecruteak", MusicPlaces.Describe(MusicPlace.ParkNight, MusicSource.Of(["c"]), Library));
    }
}
