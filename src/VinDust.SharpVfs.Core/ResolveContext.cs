using VinDust.SharpVfs.Abstractions;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// Carries per-resolution state through a chain of nested file systems: the set of
/// visited file systems for cycle detection, and the optional tracer.
/// </summary>
/// <remarks>
/// A new context is created at the entry point of a resolution (typically
/// <see cref="VfsRoot"/>) and passed down the delegation chain. It must not be stored
/// or reused across top-level resolutions.
/// </remarks>
internal sealed class ResolveContext
{
    private readonly HashSet<IFileSystem> _visited;

    public ResolveContext(IResolveTracer? tracer)
    {
        _visited = new HashSet<IFileSystem>(ReferenceEqualityComparer.Instance);
        Tracer = tracer;
    }

    public IResolveTracer? Tracer { get; }

    public bool TryVisit(IFileSystem fileSystem) => _visited.Add(fileSystem);

    public int Depth => _visited.Count;
}
