using VinDust.SharpVfs.Abstractions.Policies;

namespace VinDust.SharpVfs.Abstractions;

/// <summary>Options controlling a single mount operation.</summary>
public sealed class MountOptions
{
    /// <summary>Default options: create-if-missing, replace, read-write, same-FS symlinks.</summary>
    public static readonly MountOptions Default = new();

    /// <summary>Initializes a new instance of the <see cref="MountOptions"/> class.</summary>
    public MountOptions()
    {
    }

    /// <summary>
    /// Gets a value indicating whether the mount point directory should be created
    /// in the base file system if it does not already exist.
    /// </summary>
    public bool CreateIfMissing { get; init; } = true;

    /// <summary>Gets the behavior when the mount point path overlaps existing content.</summary>
    public MountOverlapBehavior OverlapBehavior { get; init; } = MountOverlapBehavior.Replace;

    /// <summary>Gets a value indicating whether writes through this mount point are rejected.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Gets the boundary within which symbolic links under this mount may resolve.</summary>
    public SymlinkScope SymlinkScope { get; init; } = SymlinkScope.SameFs;

    /// <summary>
    /// Gets the write policy applied when <see cref="OverlapBehavior"/> is
    /// <see cref="MountOverlapBehavior.Overlay"/>. When <see langword="null"/>, the
    /// default policy (first visible file system) is used.
    /// </summary>
    /// <remarks>
    /// The policy is consulted only to construct the initial union layer mapping. It does
    /// not persist: subsequent write operations consult the union's own policy configuration.
    /// </remarks>
    public IWritePolicy? WritePolicy { get; init; }
}