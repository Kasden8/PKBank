using PKForge.Domain;

namespace PKBank.App.Services;

/// <summary>
/// The PC's file access for the safe save writer: the document id is the file's path on disk.
/// Reads are whole files; writes go to a temporary file that replaces the original only when it
/// is complete, so an interrupted write never leaves a half-written save.
/// </summary>
public sealed class LocalFileAccess : ISaveFileAccess
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
