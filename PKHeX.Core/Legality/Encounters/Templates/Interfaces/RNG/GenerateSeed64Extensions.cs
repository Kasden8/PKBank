namespace PKHeX.Core;

/// <summary>
/// Compatibility shim for consumers written against the pre-ITrainerInfo signature
/// (e.g. the Auto Legality Mod plugin). Passes <see langword="null"/> for the trainer;
/// implementations that need trainer data treat it as absent (maximum roll count).
/// </summary>
public static class GenerateSeed64Extensions
{
    public static void GenerateSeed64(this IGenerateSeed64 gs, PKM pk, ulong seed) => gs.GenerateSeed64(pk, null!, seed);
}
