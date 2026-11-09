using Avalonia.Media.Imaging;
using PKForge.Domain;
using PKForge.Engine;

namespace PKBank.App.Services;

/// <summary>
/// Loads and caches bundled PKHeX sprite assets (copied next to the exe under Assets/) as
/// Avalonia bitmaps. Offline; no CDN dependency. Front-facing port of the Android service.
/// </summary>
public sealed class SpriteService
{
    private const int MaxCacheEntries = 4096;
    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loading = new(StringComparer.Ordinal);
    private readonly Queue<string> _eviction = new();
    private readonly Queue<string> _animatedEviction = new();
    private readonly Lock _gate = new();
    private static readonly SemaphoreSlim DecodeGate = new(Math.Max(2, Environment.ProcessorCount - 1));
    private readonly string _root;

    public SpriteService(string? assetsRoot = null)
        => _root = assetsRoot ?? Path.Combine(AppContext.BaseDirectory, "Assets");

    private static Bitmap? DecodeFile(string path)
    {
        try { return new Bitmap(path); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException) { return null; }
    }

    private void Cache(string key, Bitmap? bitmap)
    {
        lock (_gate)
        {
            _cache[key] = bitmap;
            _eviction.Enqueue(key);
            while (_eviction.Count > 0 && _cache.Count > MaxCacheEntries)
            {
                var old = _eviction.Dequeue();
                if (old == key) continue;
                if (_cache.Remove(old, out var evicted))
                    evicted?.Dispose();
            }
        }
    }

    /// <summary>Best bundled candidate for a look, synchronously decoded with the gap cached.</summary>
    public Bitmap? GetSprite(SpriteLook look)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(look.CacheKey, out var cached))
                return cached;
        }
        Bitmap? bitmap = null;
        foreach (var candidate in SpriteCatalog.BundledCandidates(look))
        {
            bitmap = DecodeLogical(candidate.Path);
            if (bitmap is not null) break;
        }
        Cache(look.CacheKey, bitmap);
        return bitmap;
    }

    public void Warm(SpriteLook look, Action onLoaded)
    {
        lock (_gate)
        {
            if (_cache.ContainsKey(look.CacheKey)) { onLoaded(); return; }
            if (!_loading.Add(look.CacheKey)) return;
        }
        Task.Run(() =>
        {
            DecodeGate.Wait();
            try { GetSprite(look); }
            finally
            {
                lock (_gate) _loading.Remove(look.CacheKey);
                DecodeGate.Release();
            }
            onLoaded();
        });
    }

    public Bitmap? GetSprite(int species, int form, bool shiny)
        => GetSprite(new SpriteLook(species, form, shiny));

    public Bitmap? GetBall(int ball)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue($"ball-{ball}", out var cached))
                return cached;
        }
        var bitmap = DecodeLogical($"balls/_ball{ball}.png");
        Cache($"ball-{ball}", bitmap);
        return bitmap;
    }

    /// <summary>Box wallpaper by PKHeX asset name ("box_wp01xy"); null until loaded / unknown.</summary>
    public Bitmap? GetWallpaper(string assetName)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue("wallpaper-" + assetName, out var cached))
                return cached;
        }
        var bitmap = DecodeLogical($"wallpapers/{assetName}.png");
        Cache("wallpaper-" + assetName, bitmap);
        return bitmap;
    }

    /// <summary>LUMI icon, when the catalog proposes one (Sprites/lumi files are bundled too).</summary>
    public Bitmap? GetLumiIcon(SpriteLook look)
    {
        foreach (var candidate in SpriteCatalog.BundledCandidates(look))
        {
            if (candidate.Path.Contains("/lumi/", StringComparison.Ordinal))
                return DecodeLogical(candidate.Path);
        }
        return null;
    }

    /// <summary>Decodes a logical asset path ("sprites/b_6.png") from the Assets folder.</summary>
    public Bitmap? DecodeLogical(string logicalPath)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(logicalPath, out var cached))
                return cached;
        }
        var local = logicalPath.Replace('/', Path.DirectorySeparatorChar);
        var bitmap = DecodeFile(Path.Combine(_root, local));
        Cache(logicalPath, bitmap);
        return bitmap;
    }
}
