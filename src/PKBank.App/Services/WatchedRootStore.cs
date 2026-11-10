using PKHeX.Core;

namespace PKBank.App.Services;

/// <summary>A watched-root link a PC folder (emulator's save folder or a manual folder) may produce.</summary>
public enum WatchedRootKind
{
    /// <summary>A folder of emulator saves (RetroArch, melonDS, Dolphin, Azahar/Citra, Eden, ...).</summary>
    EmulatorSaves,

    /// <summary>A user-chosen folder (any saves it contains).</summary>
    UserFolder,
}

/// <summary>
/// PC's "Preferences" for watched folders: an explicit list of folders kept in
/// <see cref="SettingsStore"/> (replacing Android's Preferences-guarded store).
/// </summary>
public static class WatchedRootStore
{
    private const string Key = "watched_folders";

    private static readonly Lock Gate = new();

    /// <summary>A folder keeps either a name (a friendly emulator title) or a raw path.</summary>
    public sealed record Folder(string Path, string? Label = null, WatchedRootKind Kind = WatchedRootKind.UserFolder);

    public static IReadOnlyList<Folder> GetWatchedFolders()
    {
        lock (Gate)
        {
            var raw = SettingsStore.Get(Key, (string?)null);
            if (string.IsNullOrWhiteSpace(raw)) return [];
            var entries = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var folders = new List<Folder>(entries.Length);
            foreach (var line in entries)
            {
                var columns = line.Split('|');
                var path = columns[0];
                var label = columns.Length >= 2 ? columns[1] : null;
                var kind = columns.Length >= 3 && Enum.TryParse<WatchedRootKind>(columns[2], out var parsed)
                    ? parsed
                    : WatchedRootKind.UserFolder;
                if (!string.IsNullOrWhiteSpace(path)) folders.Add(new Folder(path, label, kind));
            }
            return folders;
        }
    }

    public static void SetWatchedFolders(IEnumerable<Folder> folders)
    {
        lock (Gate)
        {
            var lines = folders
                .Select(f => $"{f.Path}|{f.Label ?? ""}|{f.Kind}")
                .ToList();
            SettingsStore.Set(Key, string.Join('\n', lines));
        }
    }

    public static void SetEmulatorSaves(Folder folder)
    {
        var all = GetWatchedFolders().Where(f => !f.Path.Equals(folder.Path, StringComparison.OrdinalIgnoreCase));
        SetWatchedFolders(all.Concat([folder]));
    }

    public static void AddFolder(Folder folder)
    {
        var all = GetWatchedFolders().Where(f => !f.Path.Equals(folder.Path, StringComparison.OrdinalIgnoreCase));
        SetWatchedFolders(all.Concat([folder]));
    }

    public static void Remove(string path)
    {
        var rest = GetWatchedFolders().Where(f => !f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        SetWatchedFolders(rest);
    }

    private static void ProcessDisposable(IEnumerable<Folder> folders) => SetWatchedFolders(folders);

    /// <summary>
    /// Best-guess standard locations for the emulators that write saves to known folders.
    /// Returns only existing folders (link them once; the scan filters by save magic).
    /// </summary>
    public static IReadOnlyList<Folder> KnownEmulatorSaves()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var candidates = new (string Path, string Label)[]
        {
            (Path.Combine(documents, "RetroArch", "saves"), "RetroArch (docs)"),
            (Path.Combine(AppContext.BaseDirectory, "saves"), "melonDS"),
            (Path.Combine(localAppData, "Dolphin Emulator", "GC"), "Dolphin GC"),
            (Path.Combine(localAppData, "Azahar", "natives"), "Azahar (natives)"),
            (Path.Combine(localAppData, "Lime3DS", "natives"), "Lime3DS (natives)"),
        };
        var ours = candidates.Select(x => new Folder(x.Path, x.Label, WatchedRootKind.EmulatorSaves)).ToList();
        return ours.Where(f => Directory.Exists(f.Path)).ToList();
    }
}
