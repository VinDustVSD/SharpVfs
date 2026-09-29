using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// Receives notifications about the progress of path resolution.
/// </summary>
/// <remarks>
/// <para>
/// All methods have default no-op implementations. Implementers override only the
/// callbacks they care about. The default implementation does nothing and is used by
/// <see cref="PathResolver"/> when no tracer is supplied.
/// </para>
/// <para>
/// Tracers must be thread-safe: resolution may occur concurrently on the same
/// <see cref="PathResolver"/> from multiple threads.
/// </para>
/// </remarks>
public interface IResolveTracer
{
    /// <summary>Called when a resolution begins.</summary>
    /// <param name="position">The initial position describing the entry point.</param>
    void OnStart(in ResolveStep position)
    {
    }

    /// <summary>Called each time resolution enters a new file system.</summary>
    /// <param name="position">The position describing the current location.</param>
    void OnEnterFileSystem(in ResolveStep position)
    {
    }

    /// <summary>Called when resolution crosses a mount point into another file system.</summary>
    /// <param name="position">The position describing the location after the transition.</param>
    /// <param name="mountPoint">The mount point that was crossed.</param>
    void OnMountTransition(in ResolveStep position, FsPath mountPoint)
    {
    }

    /// <summary>Called when resolution completes successfully.</summary>
    /// <param name="position">The final position, referring to the resolved file system.</param>
    /// <param name="remaining">The remaining path inside the resolved file system.</param>
    void OnComplete(in ResolveStep position, FsPath remaining)
    {
    }

    /// <summary>Called when resolution fails.</summary>
    /// <param name="path">The path that failed to resolve.</param>
    /// <param name="exception">The exception describing the failure.</param>
    void OnFailure(FsPath path, System.Exception exception)
    {
    }
}