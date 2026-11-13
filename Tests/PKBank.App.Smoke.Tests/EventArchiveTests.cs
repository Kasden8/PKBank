using Avalonia;
using Avalonia.Headless;
using PKBank.App.Services;
using PKForge.Domain;
using PKForge.Engine;
using PKHeX.Core;
using Xunit;

namespace PKBank.App.Smoke.Tests;

/// <summary>
/// The mystery gift album must offer the packed EventsGallery (EGDB) beside the
/// embedded database (MGDB). The gallery's cards largely duplicate PKHeX's MGDB
/// (same upstream source), so the album count may not grow — what must change is
/// that the local tables know the gallery files (EGDB non-empty) and the album
/// can show each card's source file. Loading another generation never drops the
/// first one's cards.
/// </summary>
public sealed class EventArchiveTests
{
    private static readonly EventDatabaseService Events = new();

    private static void EnsurePlatform() => HeadlessTestPlatform.Ensure();

    private static IReadOnlyList<EventGift> Gen7Album()
    {
        using var session = new SaveEngineSession(FixtureGen7());
        return Events.GetGifts(session);
    }

    /// <summary>A real Sun/Moon save from the Core test data (a blank SAV7SM is not a
    /// recognized save; the engine only opens genuine ones).</summary>
    private static byte[] FixtureGen7()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PKBank.slnx")))
            directory = directory.Parent;
        var fixture = Path.Combine(directory!.FullName, "Tests", "PKHeX.Core.Tests", "TestData", "SM Project 802.main");
        return File.ReadAllBytes(fixture);
    }

    [Fact]
    public void GalleryCardsJoinTheEmbeddedDatabase()
    {
        EnsurePlatform();
        var before = Gen7Album();
        Assert.True(before.Count > 0, "the embedded MGDB should already offer Gen 7 cards");

        EventArchive.EnsureLoaded(7);

        Assert.True(EncounterEvent.EGDB_G7.Length > 0, "the gallery must feed PKHeX's local tables");
        var after = Gen7Album();
        Assert.True(after.Count >= before.Count, "the merged album must never lose cards");
        Assert.Contains(after, g => g.Details?.SourceFile is not null);
    }

    [Fact]
    public void LoadingAnotherGenerationKeepsTheFirst()
    {
        EnsurePlatform();
        EventArchive.EnsureLoaded(7);
        var gen7Cards = EncounterEvent.EGDB_G7.Length;
        Assert.True(gen7Cards > 0);

        EventArchive.EnsureLoaded(9);

        Assert.True(EncounterEvent.EGDB_G9.Length > 0);
        Assert.Equal(gen7Cards, EncounterEvent.EGDB_G7.Length);
    }
}
