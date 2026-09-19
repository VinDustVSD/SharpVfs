using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Abstractions.Policies;

/// <summary>Describes a pending write operation to be routed by an <see cref="IWritePolicy"/>.</summary>
public sealed class WriteContext
{
    /// <summary>Initializes a new instance of the <see cref="WriteContext"/> class.</summary>
    /// <param name="path">The path being written.</param>
    /// <param name="candidates">The file systems visible at <paramref name="path"/>, topmost first.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidates"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is the default value or <paramref name="candidates"/> is empty.</exception>
    public WriteContext(FsPath path, IReadOnlyList<IFileSystem> candidates)
    {
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one candidate is required.", nameof(candidates));
        }

        Path = path;
        Candidates = candidates;
    }

    /// <summary>Gets the path being written.</summary>
    public FsPath Path { get; }

    /// <summary>
    /// Gets the candidate file systems visible at <see cref="Path"/>, ordered from the
    /// topmost overlay layer to the bottommost.
    /// </summary>
    public IReadOnlyList<IFileSystem> Candidates { get; }
}