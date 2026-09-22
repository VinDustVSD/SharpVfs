using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core.Walking;

/// <summary>Determines whether a file entry should be opened as an archive.</summary>
public interface IArchiveDetector
{
    /// <summary>Determines whether the entry at the specified URI is an archive.</summary>
    /// <param name="uri">The URI of the file.</param>
    /// <param name="entry">The file entry metadata.</param>
    /// <returns><see langword="true"/> if the walker should attempt to open the file as an archive.</returns>
    bool IsArchive(VfsUri uri, FileSystemEntry entry);
}