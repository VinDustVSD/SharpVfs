using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Abstractions;

/// <summary>
/// Low-level abstraction over a single file system.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IFileSystem"/> operates exclusively on <see cref="FsPath"/> and knows
/// nothing about schemes, mounting, or the wider VFS tree. That knowledge lives in
/// the higher-level <c>VfsRoot</c> and <c>PathResolver</c> types.
/// </para>
/// <para>
/// End users are not expected to interact with <see cref="IFileSystem"/> directly;
/// they use <c>VfsRoot</c> with <see cref="VfsUri"/> values. Direct usage is intended
/// for tests of a specific file system and for building higher-level constructs such
/// as union or caching decorators.
/// </para>
/// <para>
/// All members are asynchronous. Cancellation is propagated via the
/// <see cref="CancellationToken"/> parameter on every method.
/// </para>
/// </remarks>
public interface IFileSystem : IAsyncDisposable
{
    /// <summary>Gets the set of operations supported by this file system.</summary>
    FileSystemCapabilities Capabilities { get; }

    /// <summary>Determines whether an entry exists at the given path.</summary>
    /// <param name="path">The path to check.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns><see langword="true"/> if the entry exists; otherwise <see langword="false"/>.</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask<bool> ExistsAsync(FsPath path, CancellationToken ct = default);

    /// <summary>Retrieves the entry at the given path, or <see langword="null"/> if it does not exist.</summary>
    /// <param name="path">The path to look up.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>The entry, or <see langword="null"/> if no entry exists at <paramref name="path"/>.</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask<FileSystemEntry?> GetEntryAsync(FsPath path, CancellationToken ct = default);

    /// <summary>Enumerates the immediate children of the directory at the given path.</summary>
    /// <param name="path">The directory path.</param>
    /// <param name="ct">A token to observe while enumerating.</param>
    /// <returns>An asynchronous sequence of child entries.</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when enumeration is cancelled.</exception>
    IAsyncEnumerable<FileSystemEntry> EnumerateAsync(FsPath path, CancellationToken ct = default);

    /// <summary>Opens the file at the given path for reading.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A readable stream over the file contents.</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask<Stream> OpenReadAsync(FsPath path, CancellationToken ct = default);

    /// <summary>Opens (or creates) the file at the given path for writing.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="mode">Specifies how the file should be opened.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A writable stream over the file contents.</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask<Stream> OpenWriteAsync(FsPath path, FileWriteMode mode = FileWriteMode.Create, CancellationToken ct = default);

    /// <summary>Creates a directory at the given path.</summary>
    /// <param name="path">The directory path.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that completes once the directory has been created.</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask CreateDirectoryAsync(FsPath path, CancellationToken ct = default);

    /// <summary>Deletes the entry at the given path.</summary>
    /// <param name="path">The path to delete.</param>
    /// <param name="recursive">When <see langword="true"/>, deletes a directory and all of its contents.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that completes once the entry has been deleted.</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask DeleteAsync(FsPath path, bool recursive = false, CancellationToken ct = default);

    /// <summary>Moves the entry at <paramref name="source"/> to <paramref name="destination"/>.</summary>
    /// <param name="source">The source path.</param>
    /// <param name="destination">The destination path.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that completes once the entry has been moved.</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask MoveAsync(FsPath source, FsPath destination, CancellationToken ct = default);

    /// <summary>Copies the entry at <paramref name="source"/> to <paramref name="destination"/>.</summary>
    /// <param name="source">The source path.</param>
    /// <param name="destination">The destination path.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that completes once the entry has been copied.</returns>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask CopyAsync(FsPath source, FsPath destination, CancellationToken ct = default);

    /// <summary>Creates a symbolic link at the given path.</summary>
    /// <param name="path">The path at which to create the link.</param>
    /// <param name="target">The target of the link, expressed relative to the link's directory.</param>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that completes once the link has been created.</returns>
    /// <exception cref="System.NotSupportedException">Thrown when the file system does not support symbolic links.</exception>
    /// <exception cref="System.OperationCanceledException">Thrown when the operation is cancelled.</exception>
    ValueTask CreateSymbolicLinkAsync(FsPath path, RelativePath target, CancellationToken ct = default);
}
