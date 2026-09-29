using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Abstractions.Entries;

/// <summary>
/// Represents a symbolic link. A symbolic link is neither a file nor a directory;
/// it is a distinct node type whose only content is a target <see cref="RelativePath"/>.
/// </summary>
public sealed class SymbolicLink : FileSystemEntry
{
    /// <summary>Initializes a new instance of the <see cref="SymbolicLink"/> class.</summary>
    /// <param name="path">The path of this entry within the owning file system.</param>
    /// <param name="target">The target of the link, expressed as a relative path.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="target"/> is the default value.</exception>
    public SymbolicLink(FsPath path, RelativePath target)
        : base(path)
    {
        if (target.IsDefault)
        {
            throw new ArgumentException("Target must be initialized.", nameof(target));
        }

        Target = target;
    }

    /// <summary>Gets the target of the link, expressed relative to the link's directory.</summary>
    public RelativePath Target { get; }
}