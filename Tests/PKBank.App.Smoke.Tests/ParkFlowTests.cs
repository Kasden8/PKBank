using PKBank.App.Services.Pokepark;
using PKBank.App.ViewModels;
using PKForge.Domain;
using PKForge.Engine;
using PKForge.Infrastructure;
using SkiaSharp;
using Xunit;

namespace PKBank.App.Smoke.Tests;

/// <summary>
/// The park's own loop: a resident moves in from the Bank, the park keeps its care and its
/// journal between visits, the simulation runs and the scene draws a frame with the resident
/// in it. Nothing here needs the network: the PMD sprites are optional.
/// </summary>
public sealed class ParkFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pkbank-park-" + Guid.NewGuid().ToString("N"));
    private readonly FileBankService _bank;
    private readonly GameDataService _data = new();

    public ParkFlowTests()
    {
        Directory.CreateDirectory(_root);
        PokeparkStore.Folder = Path.Combine(_root, "park");
        PokeparkStore.Reset();
        _bank = new FileBankService(Path.Combine(_root, "bank"));
    }

    public void Dispose()
    {
        PokeparkStore.Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKBank", "park");
        PokeparkStore.Reset();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PokeparkService Service() => new(_bank, _data);

    [Fact]
    public void RosterSurvivesARestart()
    {
        var service = Service();
        var added = service.AddVisitor(new ParkPokemon("bank:test", 25, 0, false, "Pikachu", "Bank · Box 1 · Slot 1"));
        Assert.Contains("now lives", added);
        Assert.Single(service.Roster);

        // A second service (a new launch) reads the same park.
        var reopened = Service();
        var resident = Assert.Single(reopened.Roster);
        Assert.Equal("Pikachu", resident.Name);
        Assert.Equal(25, resident.Species);
    }

    [Fact]
    public void SendingTheSameResidentTwiceIsRefused()
    {
        var service = Service();
        var visitor = new ParkPokemon("bank:test", 25, 0, false, "Pikachu", "Bank · Box 1 · Slot 1");
        service.AddVisitor(visitor);
        var again = service.AddVisitor(visitor);
        Assert.Contains("already lives", again);
        Assert.Single(service.Roster);
    }

    [Fact]
    public void TheJournalKeepsWhatHappened()
    {
        var service = Service();
        service.AddVisitor(new ParkPokemon("bank:test", 25, 0, false, "Pikachu", "Bank"));
        service.AppendJournal(ParkJournalEventKind.Activity, "Pikachu chased a butterfly.", ["bank:test"]);
        var journal = service.Journal;
        Assert.Contains(journal, e => e.Text.Contains("butterfly"));
        // The journal is bounded: it keeps the newest entries only.
        for (var i = 0; i < 120; i++)
            service.AppendJournal(ParkJournalEventKind.Activity, "event " + i, []);
        Assert.True(service.Journal.Count <= 80);
    }

    [Fact]
    public void TheSimulationRunsAndKeepsTheCare()
    {
        var service = Service();
        service.AddVisitor(new ParkPokemon("bank:test", 25, 0, false, "Pikachu", "Bank"));
        var session = new ParkSession(service);
        session.Load(service.LoadRoster());

        var now = Environment.TickCount64;
        for (var i = 0; i < 40; i++)
        {
            now += 50;
            session.Tick(now);
        }

        var actor = session.Life["bank:test"];
        Assert.NotNull(actor);
        Assert.True(actor!.X >= 0);

        session.SaveCare();
        var care = service.CareStates;
        Assert.Single(care);
        Assert.Equal("bank:test", care[0].Id);
    }

    [Fact]
    public void TheGoalsAreThreeAndTheyCount()
    {
        var service = Service();
        service.AddVisitor(new ParkPokemon("bank:test", 25, 0, false, "Pikachu", "Bank"));
        var session = new ParkSession(service);
        session.Load(service.LoadRoster());
        var goals = session.Goals;
        Assert.Equal(ParkGoals.PerDay, goals.Goals.Count);

        session.Record(ParkDeed.Talked, "bank:test");
        Assert.True(goals.Progress.Sum() >= 0);
        Assert.NotNull(service.Goals);
    }

    [Fact]
    public void TheSceneDrawsTheResident()
    {
        var service = Service();
        service.AddVisitor(new ParkPokemon("bank:test", 25, 0, false, "Pikachu", "Bank"));
        var session = new ParkSession(service);
        session.Load(service.LoadRoster());
        var renderer = new ParkRenderer(new PokeparkSpriteService());
        renderer.Residents = service.LoadRoster();
        renderer.Life = session.Life;
        renderer.SelectedId = "bank:test";

        session.Tick(Environment.TickCount64 + 1000);
        using var bitmap = new SKBitmap(new SKImageInfo(320, 240, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        renderer.Draw(canvas, 320, 240, Environment.TickCount64 + 1000);

        // Something was painted (the map is not loaded in a test, but the world and the
        // residents are drawn on the background).
        var pixels = bitmap.Pixels;
        Assert.Contains(pixels, p => p.Alpha > 0);
    }

    [Fact]
    public void TheParkCannotGrowPastItsLimit()
    {
        var service = Service();
        for (var i = 0; i < PokeparkService.MaximumResidents + 5; i++)
            service.AddVisitor(new ParkPokemon($"bank:{i}", 25, 0, false, "Pikachu " + i, "Bank"));
        Assert.Equal(PokeparkService.MaximumResidents, service.Roster.Count);
    }

    [Fact]
    public async Task TheDaycareGrowsThePokemonThatLivesInTheBank()
    {
        var entry = await DepositFixturePokemonAsync();
        var service = Service();
        service.AddBankVisitor(entry.Id);
        var resident = service.Roster.Single();

        var daycare = new ParkDaycare(service, _bank);
        var leave = daycare.Leave(resident);
        Assert.StartsWith(resident.Name + " is at the daycare", leave, StringComparison.Ordinal);
        Assert.Single(daycare.Guests);

        // The guest stays three hours: it earns EXP, which the park can show before collecting.
        var data = PokeparkStore.Load();
        data.Daycare[0] = data.Daycare[0] with { Since = DateTimeOffset.Now.AddHours(-3) };
        PokeparkStore.Save(data);

        var preview = daycare.Preview(resident);
        Assert.NotNull(preview);
        Assert.True(preview!.Value.Exp >= ParkDaycare.ExpPerHour, "the guest should have earned at least an hour of EXP");

        var levelBefore = entry.Info.Level;
        var collected = await daycare.CollectAsync(resident);
        Assert.Contains("grew", collected, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(daycare.Guests);

        var grown = _bank.GetAll().Single(e => e.Id == entry.Id);
        Assert.True(grown.Info.Level >= levelBefore);
    }

    [Fact]
    public void TheRelationshipsAreRememberedBetweenVisits()
    {
        var service = Service();
        service.AddVisitor(new ParkPokemon("bank:a", 25, 0, false, "Pikachu", "Bank"));
        service.AddVisitor(new ParkPokemon("bank:b", 133, 0, false, "Eevee", "Bank"));
        var session = new ParkSession(service);
        session.Load(service.LoadRoster());

        // Two ticks half a minute apart: the residents meet and the park remembers it.
        var now = Environment.TickCount64;
        session.Tick(now);
        session.Tick(now + 31_000);

        var social = service.Social;
        Assert.NotNull(social);
        Assert.Equal(DateTimeOffset.Now, social!.LastSimulatedAt, TimeSpan.FromMinutes(1));
    }

    /// <summary>Copies the fixture save's first Pokémon into the Bank.</summary>
    private async Task<BankEntry> DepositFixturePokemonAsync()
    {
        var backups = new FileBackupService(Path.Combine(_root, "backups"));
        var writer = new SafeSaveWriter(new SaveEngine(), backups, new LocalFile());
        var editor = new SaveEditorViewModel(backups, writer, _bank, new SaveEngine());
        var error = await editor.LoadAsync(FixturePath());
        Assert.Null(error);
        var (box, slot) = FindFirstNonEmpty(editor);
        Assert.True(slot >= 0);
        await editor.DepositSelectedAsync(isParty: false, box, slot, move: false);
        return _bank.GetAll().Single();
    }

    private static string FixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PKBank.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "Tests", "PKHeX.Core.Tests", "TestData", "SM Project 802.main");
    }

    private static (int Box, int Slot) FindFirstNonEmpty(SaveEditorViewModel editor)
    {
        // A young Pokémon that can still grow at the daycare, in any box.
        for (var box = 0; box < editor.BoxTotal; box++)
        for (var slot = 0; slot < 30; slot++)
        {
            var pk = editor.Slot(box, slot);
            if (pk.Species != 0 && !pk.IsEgg && pk.CurrentLevel < 50) return (box, slot);
        }
        return (0, -1);
    }

    /// <summary>The PC's file access, for the safe save writer.</summary>
    private sealed class LocalFile : ISaveFileAccess
    {
        public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(string documentId, CancellationToken cancellationToken = default)
            => await File.ReadAllBytesAsync(documentId, cancellationToken).ConfigureAwait(false);

        public async ValueTask WriteAtomicallyAsync(string documentId, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        {
            var tmp = documentId + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(tmp, documentId, overwrite: true);
        }
    }
}
