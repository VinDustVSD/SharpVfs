using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core.Walking;

/// <summary>
/// Constructs a file system view over an archive file's contents.
/// </summary>
/// <remarks>
/// The opener takes ownership of the supplied stream. When it returns a file system,
/// that file system owns the stream and is responsible for disposing it. When it
/// returns <see langword="null"/>, the opener has consumed and disposed the stream
/// itself.
/// </remarks>
public interface IArchiveOpener
{
    /// <summary>Attempts to open the supplied archive content as a file system.</summary>
    /// <param name="uri">The URI of the archive file.</param>
    /// <param name="content">The archive content. The opener takes ownership of the stream.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A file system view over the archive, or <see langword="null"/> when the format is not supported.</returns>
    ValueTask<IFileSystem?> OpenAsync(VfsUri uri, Stream content, CancellationToken ct);
}