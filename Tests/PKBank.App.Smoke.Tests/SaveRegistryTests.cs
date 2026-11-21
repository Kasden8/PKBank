using PKBank.App.Services;
using Xunit;

namespace PKBank.App.Smoke.Tests;

/// <summary>
/// The editor remembers where every opened save lives, so the SAVES screen can reopen it
/// in one click. These tests use an in-memory store: the real settings file is untouched.
/// </summary>
public sealed class SaveRegistryTests
{
    private sealed class MemoryStore
    {
        public string? Raw;

        public SaveRegistry NewRegistry() => new(() => Raw, raw => Raw = raw);
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "pkbank-registry-" + Guid.NewGuid().ToString("N"), "main");

    [Fact]
    public void RegisteringASaveKeepsItsLocationAndIdentity()
    {
        var registry = new MemoryStore().NewRegistry();
        var path = TempPath();
        var when = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        var entry = registry.Register(path, "Pokémon Y", 6, "Kasden", 31, when);

        Assert.Equal(Path.GetFullPath(path), entry.Path);
        Assert.Equal("main", entry.FileName);
        Assert.Equal("Pokémon Y", entry.GameLabel);
        Assert.Equal(6, entry.Generation);
        Assert.Equal("Kasden", entry.TrainerName);
        Assert.Equal(31, entry.BoxCount);
        Assert.False(entry.Exists); // the temp path was never created
        Assert.Single(registry.GetAll());
    }

    [Fact]
    public void OpeningTheSameSaveAgainKeepsOneEntryAndRefreshesTheDate()
    {
        var registry = new MemoryStore().NewRegistry();
        var path = TempPath();
        var first = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
        var second = first.AddHours(3);

        registry.Register(path, "Pokémon Y", 6, "Kasden", 31, first);
        registry.Register(path, "Pokémon Y", 6, "Kasden", 31, second);

        var entry = Assert.Single(registry.GetAll());
        Assert.Equal(second, entry.LastOpened);
    }

    [Fact]
    public void TheRegistrySurvivesANewInstance()
    {
        var store = new MemoryStore();
        var path = TempPath();
        store.NewRegistry().Register(path, "Pokémon Sun", 7, "Kasden", 32);

        var reopened = store.NewRegistry();

        var entry = Assert.Single(reopened.GetAll());
        Assert.Equal(Path.GetFullPath(path), entry.Path);
        Assert.Equal("Pokémon Sun", entry.GameLabel);
    }

    [Fact]
    public void ForgettingASaveRemovesIt()
    {
        var registry = new MemoryStore().NewRegistry();
        var path = TempPath();
        registry.Register(path, "Pokémon Y", 6, "Kasden", 31);

        Assert.True(registry.Remove(path));
        Assert.False(registry.Remove(path));
        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public void TheNewestSaveComesFirst()
    {
        var registry = new MemoryStore().NewRegistry();
        var older = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
        registry.Register(TempPath(), "Pokémon Y", 6, "Kasden", 31, older);
        registry.Register(TempPath(), "Pokémon Sun", 7, "Kasden", 32, older.AddMinutes(5));

        var all = registry.GetAll();

        Assert.Equal("Pokémon Sun", all[0].GameLabel);
    }
}
