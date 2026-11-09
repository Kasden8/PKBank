using PKHeX.Core;
using static PKHeX.Core.GameVersion;

namespace PKBank.App.Services;

/// <summary>
/// Local port of PKHeX.Drawing.Misc's wallpaper naming (we only need the name, never its GDI+ bitmap).
/// Mirrors <c>WallpaperUtil.GetResourceSuffix(GameVersion, int)</c> plus <c>GetWallpaperResourceName</c>,
/// so the same box shows the same wallpaper as upstream.
/// </summary>
public static class WallpaperName
{
    public static string GetName(GameVersion version, int index)
    {
        index++; // start indexes at 1
        var suffix = Suffix(version, index);
        var variant = version switch
        {
            SL when index is 20 => "_n", // Naranja
            VL when index is 20 => "_u", // Uva
            _ => string.Empty,
        };
        return $"box_wp{index:00}{suffix}{variant}";
    }

    private static string Suffix(GameVersion version, int index) => version.Context switch
    {
        EntityContext.Gen3 when version == E => "e",
        EntityContext.Gen3 when FRLG.Contains(version) && index > 12 => "frlg",
        EntityContext.Gen3 => "rs",
        _ when version == COLO => "colo",
        _ when version == XD => "xd",

        EntityContext.Gen4 when DP.Contains(version) || index <= 16 => "dp",
        EntityContext.Gen4 when version == Pt => "pt",
        EntityContext.Gen4 when HGSS.Contains(version) => "hgss",

        EntityContext.Gen5 => B2W2.Contains(version) && index > 16 ? "b2w2" : "bw",
        EntityContext.Gen6 => ORAS.Contains(version) && index > 16 ? "ao" : "xy",
        EntityContext.Gen7 => "xy",
        EntityContext.Gen8b => "bdsp",
        EntityContext.Gen8 => "swsh",
        EntityContext.Gen9 => "sv",
        _ => string.Empty,
    };
}
