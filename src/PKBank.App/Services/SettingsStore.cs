using System.Text.Json;

namespace PKBank.App.Services;

/// <summary>
/// The app's saved settings, kept next to the bank data in %AppData%\PKBank. Replaces the
/// platform preferences store with a small typed JSON file; writes are atomic (tmp → rename).
/// </summary>
public static class SettingsStore
{
    private static readonly Lock Gate = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKBank", "settings.json");
    private static readonly Dictionary<string, JsonElement> Values = Load();

    public static string? Get(string key, string? fallback = null)
    {
        lock (Gate)
            return Values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : fallback;
    }

    public static int Get(string key, int fallback = 0)
    {
        lock (Gate)
        {
            if (!Values.TryGetValue(key, out var value)) return fallback;
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : fallback;
        }
    }

    public static bool Get(string key, bool fallback = false)
    {
        lock (Gate)
        {
            if (!Values.TryGetValue(key, out var value)) return fallback;
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback,
            };
        }
    }

    public static void Set(string key, object? value)
    {
        lock (Gate)
        {
            if (value is null) Values.Remove(key);
            else Values[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }
    }

    private static Dictionary<string, JsonElement> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(FilePath));
            return parsed ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Values, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Settings are nice to have; failing to persist them must not break the app.
        }
    }
}
