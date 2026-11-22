using Avalonia;
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
    public void GalleryCardsJoinTheEmbeddedDatabase() => HeadlessTestPlatform.Run(() =>
    {
        var before = Gen7Album();
        Assert.True(before.Count > 0, "the embedded MGDB should already offer Gen 7 cards");

        EventArchive.EnsureLoaded(7);

        Assert.True(EncounterEvent.EGDB_G7.Length > 0, "the gallery must feed PKHeX's local tables");
        var after = Gen7Album();
        Assert.True(after.Count >= before.Count, "the merged album must never lose cards");
        Assert.Contains(after, g => g.Details?.SourceFile is not null);
    });

    [Fact]
    public void LoadingAnotherGenerationKeepsTheFirst() => HeadlessTestPlatform.Run(() =>
    {
        EventArchive.EnsureLoaded(7);
        var gen7Cards = EncounterEvent.EGDB_G7.Length;
        Assert.True(gen7Cards > 0);

        EventArchive.EnsureLoaded(9);

        Assert.True(EncounterEvent.EGDB_G9.Length > 0);
        Assert.Equal(gen7Cards, EncounterEvent.EGDB_G7.Length);
    });

    /// <summary>Injecting a card must mirror a real distribution: the wonder card lands in
    /// the save's gift album (fresh, not marked received) so the game's own delivery man
    /// hands the gift over, instead of the mon appearing in a box slot.</summary>
    [Fact]
    public void InjectedCardWaitsInTheAlbumForTheDeliveryMan() => HeadlessTestPlatform.Run(() =>
    {
        using var session = new SaveEngineSession(FixtureGen7());
        Assert.True(Events.StoresWonderCards(session), "Gen 7 saves hold wonder cards");

        var pokemonCard = Events.GetGifts(session).First(g => g.Kind == EventGiftKind.Pokemon);
        var emptyBefore = session.GetMysteryGiftInbox().Cards.Count;

        var outcome = Events.ReceiveCard(session, pokemonCard.Id);

        Assert.True(outcome.Success, outcome.Message);
        var inbox = session.GetMysteryGiftInbox();
        var stored = Assert.Single(inbox.Cards, c => c.CardId == pokemonCard.CardId);
        Assert.False(stored.GiftUsed, "a used card would be skipped by the delivery man");
        Assert.False(stored.IsReceivedRecord, "the delivery man skips cards already received");
        Assert.Equal(emptyBefore + 1, inbox.Cards.Count);
    });

    [Fact]
    public void AlbumlessGamesReportIt() => HeadlessTestPlatform.Run(() =>
    {
        var engine = new SaveEngine();
        using var session = engine.OpenBlankSession(9);
        Assert.False(Events.StoresWonderCards(session), "Gen 9 saves have no wonder card album");
    });
}
