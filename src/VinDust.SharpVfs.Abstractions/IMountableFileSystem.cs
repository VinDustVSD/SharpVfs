using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Abstractions;

/// <summary>
/// A file system that supports attaching other file systems at specified paths.
/// </summary>
/// <remarks>
/// <para>
/// Mount points are stored locally within each mounting file system. Resolution
/// delegates to the mounted target for paths under the mount point.
/// </para>
/// <para>
/// A file system may be mounted at multiple paths; reference counting is used to
/// dispose the target only when the last mount is removed.
/// </para>
/// </remarks>
public interface IMountableFileSystem : IFileSystem
{
    /// <summary>Mounts <paramref name="target"/> at the specified path.</summary>
    /// <param name="path">The mount point path within this file system.</param>
    /// <param name="target">The file system to mount.</param>
    /// <param name="options">The mount options.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The created mount point.</returns>
    /// <exception cref="Exceptions.VfsMountCycleException">Thrown when the mount would introduce a cycle.</exception>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask<MountPoint> MountAsync(FsPath path, IFileSystem target, MountOptions options, CancellationToken ct = default);

    /// <summary>Removes the mount point at the specified path.</summary>
    /// <param name="path">The mount point path.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that completes once the mount point has been removed.</returns>
    /// <exception cref="Exceptions.VfsNotFoundException">Thrown when no mount point exists at <paramref name="path"/>.</exception>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask UnmountAsync(FsPath path, CancellationToken ct = default);

    /// <summary>Gets a snapshot of the current mount points.</summary>
    /// <returns>A read-only collection of mount points.</returns>
    IReadOnlyCollection<MountPoint> GetMounts();

    /// <summary>
    /// Finds the mount point whose path is the longest matching prefix of <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path to resolve.</param>
    /// <param name="remaining">
    /// When this method returns, contains the remainder of <paramref name="path"/> after the
    /// matched mount point, or <paramref name="path"/> itself when no mount matched.
    /// </param>
    /// <returns>
    /// The matching mount point, or <see langword="null"/> if no mount point covers
    /// <paramref name="path"/>.
    /// </returns>
    /// <remarks>
    /// This is a low-level hook used by the path resolver. Callers normally do not invoke it
    /// directly.
    /// </remarks>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="path"/> is the default value.</exception>
    MountPoint? TryResolveMount(FsPath path, out FsPath remaining);

    /// <summary>Occurs when the mount table changes.</summary>
    event EventHandler<MountTableChangedEventArgs>? MountTableChanged;
}