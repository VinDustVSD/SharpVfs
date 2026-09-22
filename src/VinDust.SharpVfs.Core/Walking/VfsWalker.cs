using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core.Walking;

/// <summary>
/// Recursively traverses a VFS tree, optionally descending into archives detected by
/// an <see cref="IArchiveDetector"/>.
/// </summary>
/// <remarks>
/// <para>
/// The walker does not modify mount state: archive contents are exposed through
/// short-lived file system instances owned by the walker and disposed when the walk
/// leaves the archive. This keeps traversal independent of the <c>VfsRoot</c> state
/// and lets multiple walkers run concurrently over the same tree.
/// </para>
/// <para>
/// The walk is depth-first. Directories are visited before their children; files are
/// visited in enumeration order; archives are visited as files first, then opened for
/// traversal.
/// </para>
/// </remarks>
public sealed class VfsWalker
{
    private readonly VfsRoot _root;
    private readonly IVfsWalkerVisitor _visitor;
    private readonly IArchiveDetector? _archiveDetector;
    private readonly IArchiveOpener? _archiveOpener;

    /// <summary>Initializes a new instance of the <see cref="VfsWalker"/> class.</summary>
    /// <param name="root">The root to walk.</param>
    /// <param name="visitor">The visitor that receives traversal events.</param>
    /// <param name="archiveDetector">An optional detector for archives to descend into.</param>
    /// <param name="archiveOpener">An optional opener that constructs file systems from archive content.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="root"/> or <paramref name="visitor"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when only one of <paramref name="archiveDetector"/> and <paramref name="archiveOpener"/> is provided.</exception>
    public VfsWalker(
        VfsRoot root,
        IVfsWalkerVisitor visitor,
        IArchiveDetector? archiveDetector = null,
        IArchiveOpener? archiveOpener = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(visitor);

        if ((archiveDetector is null) != (archiveOpener is null))
        {
            throw new ArgumentException(
                "Both archiveDetector and archiveOpener must be provided together, or neither.");
        }

        _root = root;
        _visitor = visitor;
        _archiveDetector = archiveDetector;
        _archiveOpener = archiveOpener;
    }

    /// <summary>Walks the tree rooted at the specified URI.</summary>
    /// <param name="start">The URI at which to start the walk.</param>
    /// <param name="ct">A token to observe.</param>
    /// <returns>A task that completes when the walk has finished.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="start"/> is the default value.</exception>
    public async ValueTask WalkAsync(VfsUri start, CancellationToken ct = default)
    {
        if (start.IsDefault)
        {
            throw new ArgumentException("URI must be initialized.", nameof(start));
        }

        var resolution = await _root.GetResolutionAsync(start, ct).ConfigureAwait(false);
        await WalkFsAsync(resolution.FileSystem, resolution.Path, start, ct).ConfigureAwait(false);
    }

    private async ValueTask WalkFsAsync(
        IFileSystem fs,
        FsPath path,
        VfsUri uri,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            await _visitor.OnDirectoryAsync(uri, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _visitor.OnErrorAsync(uri, ex, ct).ConfigureAwait(false);
            return;
        }

        List<FileSystemEntry> children;
        try
        {
            children = [];
            await foreach (var entry in fs.EnumerateAsync(path, ct).ConfigureAwait(false))
            {
                children.Add(entry);
            }
        }
        catch (Exception ex)
        {
            await _visitor.OnErrorAsync(uri, ex, ct).ConfigureAwait(false);
            return;
        }

        foreach (var entry in children)
        {
            ct.ThrowIfCancellationRequested();
            var childUri = MakeChildUri(uri, entry.Path);

            if (entry is DirectoryEntry)
            {
                await WalkFsAsync(fs, entry.Path, childUri, ct).ConfigureAwait(false);
            }
            else if (entry is FileEntry)
            {
                try
                {
                    await _visitor.OnFileAsync(childUri, entry, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await _visitor.OnErrorAsync(childUri, ex, ct).ConfigureAwait(false);
                    continue;
                }

                if (_archiveDetector is not null && _archiveDetector.IsArchive(childUri, entry))
                {
                    await DescendAsync(fs, entry.Path, childUri, ct).ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask DescendAsync(IFileSystem fs, FsPath path, VfsUri uri, CancellationToken ct)
    {
        Stream stream;
        try
        {
            stream = await fs.OpenReadAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _visitor.OnErrorAsync(uri, ex, ct).ConfigureAwait(false);
            return;
        }

        IFileSystem? innerFs;
        try
        {
            innerFs = await _archiveOpener!.OpenAsync(uri, stream, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            await _visitor.OnErrorAsync(uri, ex, ct).ConfigureAwait(false);
            return;
        }

        if (innerFs is null)
        {
            // Opener declined; it has taken ownership of the stream.
            return;
        }

        try
        {
            await WalkFsAsync(innerFs, FsPath.Root, uri, ct).ConfigureAwait(false);
        }
        finally
        {
            await innerFs.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static VfsUri MakeChildUri(VfsUri parent, FsPath childPath)
    {
        var name = childPath.GetFileName();
        return name is null
            ? parent
            : RelativePath.Parse(name).ResolveAgainst(parent);
    }
}