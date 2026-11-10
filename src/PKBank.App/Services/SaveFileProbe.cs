using PKHeX.Core;

namespace PKBank.App.Services;

/// <summary>Recognizes save files during a folder scan.</summary>
public static class SaveFileProbe
{
    /// <summary>
    /// Returns true when a file parses as a PKHeX save (any supported game, incl. emulators).
    /// </summary>
    public static bool LooksLikeSave(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return SaveUtil.GetSaveFile(bytes, path) is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                     or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
