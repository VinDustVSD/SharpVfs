using System.Runtime.CompilerServices;
using VinDust.SharpVfs.Abstractions.Entries;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// Merges two asynchronous sequences of directory entries with primary-wins semantics:
/// when both sequences contain an entry with the same name, the primary entry is kept
/// and the secondary is discarded.
/// </summary>
/// <remarks>
/// Entries are matched by their file name (the last segment of <see cref="FileSystemEntry.Path"/>).
/// The merge is streaming and does not buffer the primary sequence; the secondary is
/// filtered against the names already seen from the primary.
/// </remarks>
public static class DirectoryMerger
{
    /// <summary>Merges two entry sequences, keeping primary entries on name conflicts.</summary>
    /// <param name="primary">The higher-priority sequence (typically the mounted target).</param>
    /// <param name="secondary">The lower-priority sequence (typically the base file system).</param>
    /// <param name="ct">A token to observe while enumerating.</param>
    /// <returns>An asynchronous sequence of merged entries.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="primary"/> or <paramref name="secondary"/> is <see langword="null"/>.</exception>
    public static async IAsyncEnumerable<FileSystemEntry> MergeAsync(
        IAsyncEnumerable<FileSystemEntry> primary,
        IAsyncEnumerable<FileSystemEntry> secondary,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(secondary);

        var seen = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var entry in primary.WithCancellation(ct).ConfigureAwait(false))
        {
            var name = entry.Path.GetFileName();
            if (name is not null)
            {
                seen.Add(name);
            }

            yield return entry;
        }

        await foreach (var entry in secondary.WithCancellation(ct).ConfigureAwait(false))
        {
            var name = entry.Path.GetFileName();
            if (name is null || seen.Contains(name))
            {
                continue;
            }

            yield return entry;
        }
    }
}