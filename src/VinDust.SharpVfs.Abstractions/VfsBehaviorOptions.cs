namespace VinDust.SharpVfs.Abstractions;

/// <summary>
/// Base class for per-file-system behavior options.
/// </summary>
/// <remarks>
/// Behavior options are configured once when a file system is constructed and do not
/// vary per operation. Concrete file systems derive from this type and add
/// format-specific settings (see <c>MemoryFileSystemOptions</c>,
/// <c>ZipFileSystemOptions</c>).
/// </remarks>
public abstract class VfsBehaviorOptions
{
    /// <summary>Initializes a new instance of the <see cref="VfsBehaviorOptions"/> class.</summary>
    protected VfsBehaviorOptions()
    {
    }

    /// <summary>
    /// Gets a value indicating whether parent directories are created automatically
    /// when opening a file for writing. Equivalent to <c>mkdir -p</c> before the write.
    /// </summary>
    /// <remarks>
    /// Has no effect on formats without directory hierarchy, such as flat archives.
    /// </remarks>
    public bool CreateParentOnWrite { get; init; }

    /// <summary>Gets the behavior when creating a directory that already exists.</summary>
    public DirectoryExistsBehavior DirectoryExistsBehavior { get; init; } = DirectoryExistsBehavior.Ok;

    /// <summary>Gets the behavior when deleting a path that does not exist.</summary>
    public DeleteBehavior DeleteBehavior { get; init; } = DeleteBehavior.MissingOk;
}