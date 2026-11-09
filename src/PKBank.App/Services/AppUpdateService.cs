using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json.Serialization;

namespace PKBank.App.Services;

/// <summary>
/// Checks the GitHub releases feed for a newer build (PKForge keeps its release APKs there).
/// Offline failure is silent; the home page shows a pill when one is available.
/// </summary>
public sealed class AppUpdateService
{
    private readonly HttpClient _http;

    public AppUpdateService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PKBank/0.1.0");
    }

    private const string LatestReleaseUrl = "https://api.github.com/repos/Kasden8/PKBank/releases/latest";

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    }

    /// <summary>Current running version (assembly), e.g. "0.1.0".</summary>
    public static string CurrentVersion => typeof(AppUpdateService).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";

    /// <summary>Returns (title, url) when a newer release is available; null either silent or when current.</summary>
    public async Task<(string Title, string Url)?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(LatestReleaseUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await System.Text.Json.JsonSerializer.DeserializeAsync<ReleaseDto>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (release?.TagName is null) return null;
            return IsNewer(CurrentVersion, release.TagName) ? ($"v{release.TagName} ready", release.HtmlUrl ?? "") : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null; // offline or feed changed: silent.
        }
    }

    /// <summary>Semantic "0.1.0" &lt; "0.2.0" (ignores the 'v' prefix and '+build' suffix).</summary>
    public static bool IsNewer(string current, string latest)
    {
        static (int[] Nums, string Tail) SplitOnce(string value)
        {
            var v = value.Trim().TrimStart('v', 'V');
            var plus = v.IndexOf('+');
            if (plus > 0) v = v[..plus];
            var dash = v.IndexOf('-');
            var tail = dash > 0 ? v[(dash + 1)..] : "";
            var numbers = dash > 0 ? v[..dash].Split('.') : v.Split('.');
            return (numbers.Select(n => int.TryParse(n, out var x) ? x : 0).ToArray(), tail);
        }
        var (curNums, curTail) = SplitOnce(current);
        var (newNums, newTail) = SplitOnce(latest);
        for (var i = 0; i < Math.Max(curNums.Length, newNums.Length); i++)
        {
            var c = i < curNums.Length ? curNums[i] : 0;
            var n = i < newNums.Length ? newNums[i] : 0;
            if (n > c) return true;
            if (n < c) return false;
        }
        // Same numeric version: a plain release beats a prerelease tag.
        return string.IsNullOrEmpty(curTail) ? false : string.Compare(newTail, curTail, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
