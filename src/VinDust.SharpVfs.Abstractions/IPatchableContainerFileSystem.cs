using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Abstractions;

/// <summary>
/// A container-backed file system that supports efficient replacement of a single
/// entry's content without rewriting the entire container.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must not overwrite the bytes of unchanged entries when the
/// underlying format allows it. When in-place update is not feasible, the
/// implementation falls back to a full repack; callers can detect this by
/// inspecting <see cref="FileSystemCapabilities.PatchableContainer"/> together with
/// the implementation's documented behavior.
/// </para>
/// <para>
/// This interface is the primary integration point for delta builders that need to
/// patch existing containers predictably.
/// </para>
/// </remarks>
public interface IPatchableContainerFileSystem : IFileSystem
{
    /// <summary>Replaces the content of the entry at <paramref name="path"/>.</summary>
    /// <param name="path">The path of the entry to replace within the container.</param>
    /// <param name="newContent">The new content. The stream must be readable for its full length.</param>
    /// <param name="options">Options controlling the patch operation.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that completes once the entry has been replaced.</returns>
    /// <exception cref="Exceptions.VfsNotFoundException">Thrown when no entry exists at <paramref name="path"/>.</exception>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask ReplaceEntryAsync(FsPath path, Stream newContent, ContainerPatchOptions options, CancellationToken ct = default);
}