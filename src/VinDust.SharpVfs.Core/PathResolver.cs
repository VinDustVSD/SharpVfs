using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// Walks a chain of mount points to find the file system that owns a given path.
/// </summary>
/// <remarks>
/// <para>
/// Resolution starts at a given file system and repeatedly asks the current file system,
/// if mountable, for the longest matching mount point. When a match is found, resolution
/// jumps into the mounted target with the remaining path and continues.
/// </para>
/// <para>
/// Cycle detection is delegated to a <see cref="ResolveContext"/> supplied by the caller.
/// A top-level entry point typically creates a fresh context; nested calls pass it down
/// the delegation chain.
/// </para>
/// <para>
/// This type is thread-safe. It holds no per-resolution state; all state lives in the
/// <see cref="ResolveContext"/>.
/// </para>
/// </remarks>
public sealed class PathResolver
{
    private readonly IResolveTracer? _tracer;

    /// <summary>Initializes a new instance of the <see cref="PathResolver"/> class.</summary>
    /// <param name="tracer">An optional tracer that receives resolution events.</param>
    public PathResolver(IResolveTracer? tracer = null)
    {
        _tracer = tracer;
    }

    /// <summary>Resolves a path starting from a file system, creating a new resolution context.</summary>
    /// <param name="start">The file system at which to start resolution.</param>
    /// <param name="path">The path to resolve.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>The resolved path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="start"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is the default value.</exception>
    /// <exception cref="VfsMountCycleException">Thrown when resolution encounters a mount cycle.</exception>
    public ValueTask<ResolvedPath> ResolveAsync(IFileSystem start, FsPath path, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        var context = new ResolveContext(_tracer);
        return ResolveAsync(start, path, context, ct);
    }

    /// <summary>Resolves a path starting from a file system, using an existing resolution context.</summary>
    /// <param name="start">The file system at which to start resolution.</param>
    /// <param name="path">The path to resolve.</param>
    /// <param name="context">The resolution context carrying visited file systems and the tracer.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>The resolved path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="start"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is the default value.</exception>
    /// <exception cref="VfsMountCycleException">Thrown when resolution encounters a mount cycle.</exception>
    internal static async ValueTask<ResolvedPath> ResolveAsync(
        IFileSystem start,
        FsPath path,
        ResolveContext context,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(context);
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        if (!context.TryVisit(start))
        {
            throw new VfsMountCycleException(
                $"Mount cycle detected: file system '{start}' is already being resolved.");
        }

        var tracer = context.Tracer;
        var currentFs = start;
        var remaining = path;
        var readOnly = false;

        tracer?.OnStart(new ResolveStep(remaining, currentFs, context.Depth));

        while (currentFs is IMountableFileSystem mountable)
        {
            ct.ThrowIfCancellationRequested();

            var mountPoint = mountable.TryResolveMount(remaining, out var rest);
            if (mountPoint is null)
            {
                break;
            }

            if (!context.TryVisit(mountPoint.Target))
            {
                throw new VfsMountCycleException(
                    $"Mount cycle detected: file system '{mountPoint.Target}' is already in the resolution chain.");
            }

            tracer?.OnMountTransition(
                new ResolveStep(rest, mountPoint.Target, context.Depth),
                mountPoint.Path);

            if (mountPoint.Options.ReadOnly)
            {
                readOnly = true;
            }

            currentFs = mountPoint.Target;
            remaining = rest;
        }

        tracer?.OnComplete(new ResolveStep(remaining, currentFs, context.Depth), remaining);

        return new ResolvedPath(currentFs, remaining, readOnly);
    }
}