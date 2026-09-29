namespace VinDust.SharpVfs.Abstractions.Policies;

/// <summary>
/// Selects a target file system for a write operation when multiple file systems
/// are visible at the same path through overlay mounts.
/// </summary>
/// <remarks>
/// A write policy is consulted only when <c>OverlapBehavior</c> is <c>Overlay</c>.
/// It does not affect reads and does not persist routing decisions: each write
/// operation selects a target independently.
/// </remarks>
public interface IWritePolicy
{
    /// <summary>Selects the file system that should receive a write to the given path.</summary>
    /// <param name="context">The write context, including the path and candidate file systems.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The selected file system. Must be one of <see cref="WriteContext.Candidates"/>.</returns>
    ValueTask<IFileSystem> SelectAsync(WriteContext context, CancellationToken ct = default);
}
