using System.IO.Compression;
using System.Text;
using Avalonia.Platform;
using PKForge.Engine;
using PKHeX.Core;

namespace PKBank.App.Services;

/// <summary>
/// The projectpokemon EventsGallery, packed per generation at build time by
/// tools/EventGen (4.3k wondercards, ~280 KB). The matching generation is
/// extracted once and handed to PKHeX's local event tables (EGDB), which the
/// event service merges with the embedded database (MGDB).
/// Ported from PKForge's MAUI EventArchive — the pack format is identical:
/// GZip'd BinaryReader stream with a file count and name/length/data records.
/// </summary>
public static class EventArchive
{
    /// <summary>Generation → its extracted gallery folder, kept so switching saves
    /// between generations never drops a generation's cards (RefreshMGDB rebuilds
    /// every table from the paths it is given).</summary>
    private static readonly Dictionary<int, string> Folders = [];

    private static int _loadedGeneration = -1;
    private static readonly Lock Gate = new();

    public static void EnsureLoaded(int generation)
    {
        if (generation is < 4 or > 9) return;
        lock (Gate)
        {
            if (_loadedGeneration == generation) return;
            try
            {
                if (!Folders.TryGetValue(generation, out var target))
                {
                    target = Extract(generation);
                    Folders[generation] = target;
                }
                EncounterEvent.RefreshMGDB([.. Folders.Values]);
                EventDatabaseService.IndexArchive([.. Folders.Values]);
                _loadedGeneration = generation;
            }
            catch (Exception error)
            {
                AppLog.Error("events", $"Event archive g{generation} could not be loaded", error);
                // A missing or corrupt archive must never break the wonder card menu;
                // the embedded database still works.
                _loadedGeneration = -1;
            }
        }
    }

    private static string Extract(int generation)
    {
        var target = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PKBank", "cache", "events", $"g{generation}");
        if (Directory.Exists(target) && Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Any())
            return target;
        Directory.CreateDirectory(target);

        using var asset = AssetLoader.Open(new Uri($"avares://PKBank/Resources/Events/events-g{generation}.bin.gz"));
        using var gz = new GZipStream(asset, CompressionMode.Decompress);
        using var reader = new BinaryReader(gz, Encoding.UTF8, leaveOpen: true);
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var nameLength = reader.ReadUInt16();
            var name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
            var length = reader.ReadInt32();
            var data = reader.ReadBytes(length);

            var path = Path.GetFullPath(Path.Combine(target, name));
            if (!path.StartsWith(target, StringComparison.Ordinal)) continue; // path traversal guard
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }
        return target;
    }
}
