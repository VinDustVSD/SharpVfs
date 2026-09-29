using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core.Walking;

/// <summary>Receives notifications as <see cref="VfsWalker"/> traverses a tree.</summary>
public interface IVfsWalkerVisitor
{
    /// <summary>Called when the walker enters a directory.</summary>
    /// <param name="uri">The directory URI.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A task that completes when the visitor has processed the directory.</returns>
    ValueTask OnDirectoryAsync(VfsUri uri, CancellationToken ct);

    /// <summary>Called when the walker encounters a file.</summary>
    /// <param name="uri">The file URI.</param>
    /// <param name="entry">The file entry metadata.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A task that completes when the visitor has processed the file.</returns>
    ValueTask OnFileAsync(VfsUri uri, FileSystemEntry entry, CancellationToken ct);

    /// <summary>Called when an error occurs while processing an entry. Default: rethrow.</summary>
    /// <param name="uri">The URI being processed when the error occurred.</param>
    /// <param name="exception">The exception.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A task that completes when the error has been handled.</returns>
    ValueTask OnErrorAsync(VfsUri uri, Exception exception, CancellationToken ct)
        => throw exception;
}