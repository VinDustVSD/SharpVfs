using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// Walks a chain of mount points to find the file system that owns a path, reporting
/// the full chain. Unlike the one-level dispatch used by <see cref="MountedFileSystem"/>,
/// this resolver jumps through all nested mounts to find the deepest target.
/// </summary>
public static class VfsChainResolver
{
    /// <summary>Resolves a path starting from a file system, following every mount point on the way.</summary>
    /// <param name="start">The file system at which to start resolution.</param>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The resolution, including the crossed mount points.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="start"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is the default value.</exception>
    /// <exception cref="VfsMountCycleException">Thrown when resolution encounters a mount cycle.</exception>
    public static VfsResolution Resolve(IFileSystem start, FsPath path)
    {
        ArgumentNullException.ThrowIfNull(start);

        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        var chain = new List<MountPoint>();
        var visited = new HashSet<IFileSystem>(ReferenceEqualityComparer.Instance) { start };
        var current = start;
        var remaining = path;
        var readOnly = false;

        while (current is IMountableFileSystem mountable)
        {
            var point = mountable.TryResolveMount(remaining, out var rest);
            if (point is null)
            {
                break;
            }

            if (!visited.Add(point.Target))
            {
                throw new VfsMountCycleException(
                    $"Mount cycle detected: file system '{point.Target}' is already in the resolution chain.");
            }

            chain.Add(point);
            readOnly |= point.Options.ReadOnly;
            current = point.Target;
            remaining = rest;
        }

        return new VfsResolution(current, remaining, chain, readOnly);
    }
}