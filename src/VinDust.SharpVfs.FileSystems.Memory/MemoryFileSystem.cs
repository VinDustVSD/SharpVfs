using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.FileSystems.Memory;

/// <summary>
/// An in-memory file system. Content and metadata are stored in managed memory and
/// are not persisted beyond the lifetime of the instance.
/// </summary>
/// <remarks>
/// <para>
/// All operations are thread-safe. A single internal lock serializes mutations; reads
/// take the lock only briefly to obtain a reference to the relevant node.
/// </para>
/// <para>
/// Streams returned by <see cref="OpenReadAsync"/> are independent snapshots of the
/// file's content at the time of the call. Streams returned by <see cref="OpenWriteAsync"/>
/// buffer writes in memory and commit the accumulated content back to the file system
/// when the stream is disposed. Callers must dispose write streams for their content
/// to be persisted.
/// </para>
/// <para>
/// Concurrent writers to the same file are resolved by last-write-wins semantics:
/// the stream that is disposed last determines the final content. Concurrent reads
/// do not block writers and vice versa.
/// </para>
/// </remarks>
public sealed class MemoryFileSystem : IFileSystem
{
    private readonly Lock _gate = new();
    private readonly MemoryFileSystemOptions _options;
    private readonly DirectoryNode _root = DirectoryNode.CreateRoot();
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="MemoryFileSystem"/> class.</summary>
    public MemoryFileSystem()
        : this(MemoryFileSystemOptions.Default)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MemoryFileSystem"/> class.</summary>
    /// <param name="options">The behavior options.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    public MemoryFileSystem(MemoryFileSystemOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public FileSystemCapabilities Capabilities =>
        FileSystemCapabilities.Read
        | FileSystemCapabilities.Write
        | FileSystemCapabilities.CreateDirectory
        | FileSystemCapabilities.Delete
        | FileSystemCapabilities.Move
        | FileSystemCapabilities.Copy
        | FileSystemCapabilities.Symlinks
        | FileSystemCapabilities.Seekable;

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return new(FindNode(path) is not null);
        }
    }

    /// <inheritdoc />
    public ValueTask<FileSystemEntry?> GetEntryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var node = FindNode(path);
            return new(node is null ? null : Materialize(path, node));
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FileSystemEntry> EnumerateAsync(
        FsPath path,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);

        List<FileSystemEntry> children;

        lock (_gate)
        {
            var node = FindNode(path)
                ?? throw new VfsNotFoundException($"Directory not found: '{path}'.");

            if (node is not DirectoryNode dir)
            {
                throw new VfsNotDirectoryException($"'{path}' is not a directory.");
            }

            children = new List<FileSystemEntry>(dir.Children.Count);
            foreach (var (name, child) in dir.Children)
            {
                var childPath = Combine(path, name);
                children.Add(Materialize(childPath, child));
            }
        }

        foreach (var entry in children)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return entry;
        }
    }

    /// <inheritdoc />
    public ValueTask<Stream> OpenReadAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        byte[] content;

        lock (_gate)
        {
            var node = FindNode(path)
                ?? throw new VfsNotFoundException($"File not found: '{path}'.");

            if (node is not FileNode file)
            {
                throw new VfsNotDirectoryException($"'{path}' is not a file.");
            }

            // Copy so the reader observes a stable snapshot even if the file is
            // replaced while the stream is open.
            content = (byte[])file.Content.Clone();
        }

        return new(new MemoryStream(content, writable: false));
    }

    /// <inheritdoc />
    public ValueTask<Stream> OpenWriteAsync(
        FsPath path,
        FileWriteMode mode = FileWriteMode.Create,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        if (path.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot open the root directory for writing.");
        }

        byte[] initial;

        lock (_gate)
        {
            var parent = GetOrCreateParentDirectory(path);
            var name = path.GetFileName()!;
            parent.Children.TryGetValue(name, out var existing);

            switch (mode)
            {
                case FileWriteMode.Create:
                    if (existing is DirectoryNode)
                    {
                        throw new VfsNotDirectoryException($"'{path}' is a directory.");
                    }

                    initial = [];
                    EnsureEntry(parent, name, new FileNode([], DateTimeOffset.UtcNow));
                    break;

                case FileWriteMode.CreateExclusive:
                    if (existing is not null)
                    {
                        throw new VfsNotDirectoryException($"'{path}' already exists.");
                    }

                    EnsureEntry(parent, name, new FileNode([], DateTimeOffset.UtcNow));
                    initial = [];
                    break;

                case FileWriteMode.Append:
                    if (existing is DirectoryNode)
                    {
                        throw new VfsNotDirectoryException($"'{path}' is a directory.");
                    }

                    initial = existing is FileNode af ? (byte[])af.Content.Clone() : [];

                    if (existing is null)
                    {
                        EnsureEntry(parent, name, new FileNode([], DateTimeOffset.UtcNow));
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown write mode.");
            }
        }

        var buffer = new MemoryStream();
        buffer.Write(initial, 0, initial.Length);

        buffer.Position = buffer.Length;

        return new(new CommitOnDisposeStream(buffer, bytes => CommitWrite(path, bytes)));
    }

    /// <inheritdoc />
    public ValueTask CreateDirectoryAsync(FsPath path, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        if (path.IsRoot)
        {
            // The root always exists. Honor DirectoryExistsBehavior.
            if (_options.DirectoryExistsBehavior == DirectoryExistsBehavior.Throw)
            {
                throw new VfsNotDirectoryException("Root directory already exists.");
            }

            return default;
        }

        lock (_gate)
        {
            var parent = GetOrCreateParentDirectory(path);
            var name = path.GetFileName()!;

            if (parent.Children.TryGetValue(name, out var existing))
            {
                if (existing is DirectoryNode)
                {
                    if (_options.DirectoryExistsBehavior == DirectoryExistsBehavior.Throw)
                    {
                        throw new VfsNotDirectoryException($"Directory already exists: '{path}'.");
                    }

                    return default;
                }

                throw new VfsNotDirectoryException($"'{path}' already exists and is not a directory.");
            }

            parent.Children[name] = DirectoryNode.Create();
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(FsPath path, bool recursive = false, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        if (path.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot delete the root directory.");
        }

        lock (_gate)
        {
            var parent = GetParentDirectoryOrThrow(path);
            var name = path.GetFileName()!;

            if (!parent.Children.TryGetValue(name, out var node))
            {
                if (_options.DeleteBehavior == DeleteBehavior.Throw)
                {
                    throw new VfsNotFoundException($"Path not found: '{path}'.");
                }

                return default;
            }

            if (node is DirectoryNode dir && dir.Children.Count > 0 && !recursive)
            {
                throw new VfsNotDirectoryException(
                    $"Directory '{path}' is not empty. Pass recursive: true to delete it.");
            }

            parent.Children.Remove(name);
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask MoveAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(source);
        ValidatePath(destination);
        ct.ThrowIfCancellationRequested();

        if (source.IsRoot || destination.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot move or overwrite the root directory.");
        }

        if (source == destination)
        {
            return default;
        }

        lock (_gate)
        {
            var sourceParent = GetParentDirectoryOrThrow(source);
            var sourceName = source.GetFileName()!;

            if (!sourceParent.Children.TryGetValue(sourceName, out var sourceNode))
            {
                throw new VfsNotFoundException($"Source not found: '{source}'.");
            }

            // Prevent moving a directory into itself or its own subtree.
            if (sourceNode is DirectoryNode && destination.StartsWith(source))
            {
                throw new VfsNotDirectoryException(
                    $"Cannot move '{source}' into its own subtree '{destination}'.");
            }

            var destinationParent = GetParentDirectoryOrThrow(destination);
            var destinationName = destination.GetFileName()!;

            sourceParent.Children.Remove(sourceName);
            destinationParent.Children[destinationName] = sourceNode;
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask CopyAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(source);
        ValidatePath(destination);
        ct.ThrowIfCancellationRequested();

        if (source.IsRoot || destination.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot copy or overwrite the root directory.");
        }

        if (source == destination)
        {
            return default;
        }

        lock (_gate)
        {
            var sourceNode = FindNode(source)
                ?? throw new VfsNotFoundException($"Source not found: '{source}'.");

            // Prevent copying a directory into its own subtree.
            if (sourceNode is DirectoryNode && destination.StartsWith(source))
            {
                throw new VfsNotDirectoryException(
                    $"Cannot copy '{source}' into its own subtree '{destination}'.");
            }

            var destinationParent = GetParentDirectoryOrThrow(destination);
            var destinationName = destination.GetFileName()!;

            destinationParent.Children[destinationName] = CloneNode(sourceNode);
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask CreateSymbolicLinkAsync(FsPath path, RelativePath target, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidatePath(path);
        ct.ThrowIfCancellationRequested();

        if (target.IsDefault)
        {
            throw new ArgumentException("Target must be initialized.", nameof(target));
        }

        if (path.IsRoot)
        {
            throw new VfsNotDirectoryException("Cannot create a symbolic link at the root.");
        }

        lock (_gate)
        {
            var parent = GetOrCreateParentDirectory(path);
            var name = path.GetFileName()!;

            if (parent.Children.ContainsKey(name))
            {
                throw new VfsNotDirectoryException($"'{path}' already exists.");
            }

            parent.Children[name] = new SymlinkNode(target, DateTimeOffset.UtcNow);
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return default;
    }

    private void CommitWrite(FsPath path, byte[] content)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // If the file was deleted while the stream was open, recreate it under
            // its parent if the parent still exists. If the parent is gone, drop
            // the write silently — the file's location no longer exists.
            var parent = TryGetParentDirectory(path);
            if (parent is null)
            {
                return;
            }

            var name = path.GetFileName()!;
            parent.Children[name] = new FileNode(content, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Returns the parent directory, or <see langword="null"/> if it does not exist.
    /// Never creates.
    /// </summary>
    private DirectoryNode? TryGetParentDirectory(FsPath path)
    {
        if (path.IsRoot)
        {
            return null;
        }

        var parentPath = path.GetParent()!.Value;
        return FindNode(parentPath) as DirectoryNode;
    }

    /// <summary>
    /// Returns the parent directory, or throws when it is missing. Never creates.
    /// Used by operations (delete, move, copy) that must not implicitly create parents.
    /// </summary>
    private DirectoryNode GetParentDirectoryOrThrow(FsPath path)
    {
        return TryGetParentDirectory(path)
            ?? throw new VfsNotFoundException($"Parent directory not found for '{path}'.");
    }

    /// <summary>
    /// Returns the parent directory, creating the full ancestor chain when
    /// <see cref="VfsBehaviorOptions.CreateParentOnWrite"/> is enabled. Throws when
    /// the parent is missing and creation is disabled.
    /// </summary>
    private DirectoryNode GetOrCreateParentDirectory(FsPath path)
    {
        if (path.IsRoot)
        {
            throw new VfsNotDirectoryException("Root has no parent directory.");
        }

        var parentPath = path.GetParent()!.Value;
        var existing = FindNode(parentPath) as DirectoryNode;
        if (existing is not null)
        {
            return existing;
        }

        if (!_options.CreateParentOnWrite)
        {
            throw new VfsNotFoundException($"Parent directory not found for '{path}'.");
        }

        return CreateDirectoryChain(parentPath);
    }

    /// <summary>Ensures every segment of <paramref name="path"/> exists as a directory.</summary>
    private DirectoryNode CreateDirectoryChain(FsPath path)
    {
        if (path.IsRoot)
        {
            return _root;
        }

        var parentPath = path.GetParent()!.Value;
        var parent = FindNode(parentPath) as DirectoryNode ?? CreateDirectoryChain(parentPath);

        var name = path.GetFileName()!;
        if (parent.Children.TryGetValue(name, out var existing))
        {
            if (existing is DirectoryNode dir)
            {
                return dir;
            }

            throw new VfsNotDirectoryException(
                $"Cannot create directory '{path}': an entry already exists at that path.");
        }

        var created = DirectoryNode.Create();
        parent.Children[name] = created;
        return created;
    }

    private Node? FindNode(FsPath path)
    {
        Node current = _root;

        foreach (var segment in path.Segments)
        {
            if (current is not DirectoryNode dir)
            {
                return null;
            }

            if (!dir.Children.TryGetValue(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private FileSystemEntry Materialize(FsPath path, Node node)
    {
        return node switch
        {
            FileNode file => new FileEntry(path)
            {
                Size = file.Content.Length,
                LastModified = file.LastModified,
                Created = file.LastModified,
                Accessed = file.LastModified,
                Attributes = FileAttributes.Normal,
            },
            DirectoryNode => new DirectoryEntry(path)
            {
                Size = 0,
                LastModified = _root.LastModified,
                Created = _root.LastModified,
                Accessed = _root.LastModified,
                Attributes = FileAttributes.Directory,
            },
            SymlinkNode link => new SymbolicLink(path, link.Target)
            {
                LastModified = link.LastModified,
                Created = link.LastModified,
                Accessed = link.LastModified,
            },
            _ => throw new InvalidOperationException($"Unknown node type: {node.GetType().Name}."),
        };
    }

    private static Node CloneNode(Node node)
    {
        return node switch
        {
            FileNode file => new FileNode((byte[])file.Content.Clone(), DateTimeOffset.UtcNow),
            DirectoryNode dir => CloneDirectory(dir),
            SymlinkNode link => new SymlinkNode(link.Target, DateTimeOffset.UtcNow),
            _ => throw new InvalidOperationException($"Unknown node type: {node.GetType().Name}."),
        };
    }

    private static DirectoryNode CloneDirectory(DirectoryNode source)
    {
        var clone = DirectoryNode.Create();
        foreach (var (name, child) in source.Children)
        {
            clone.Children[name] = CloneNode(child);
        }

        return clone;
    }

    private static void EnsureEntry(DirectoryNode parent, string name, Node node)
    {
        parent.Children[name] = node;
    }

    private static FsPath Combine(FsPath parent, string name)
    {
        // Segment-only construction via SubPath would require more plumbing; use
        // FsPath.Parse which is cheap enough for enumeration.
        return FsPath.Parse(parent.IsRoot ? "/" + name : parent + "/" + name);
    }

    private static void ValidatePath(FsPath path)
    {
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private abstract class Node;

    private sealed class FileNode : Node
    {
        public FileNode(byte[] content, DateTimeOffset lastModified)
        {
            Content = content;
            LastModified = lastModified;
        }

        public byte[] Content { get; }

        public DateTimeOffset LastModified { get; }
    }

    private sealed class DirectoryNode : Node
    {
        private DirectoryNode(DateTimeOffset lastModified)
        {
            LastModified = lastModified;
        }

        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);

        public DateTimeOffset LastModified { get; }

        public static DirectoryNode Create() => new(DateTimeOffset.UtcNow);

        public static DirectoryNode CreateRoot() => new(DateTimeOffset.UtcNow);
    }

    private sealed class SymlinkNode : Node
    {
        public SymlinkNode(RelativePath target, DateTimeOffset lastModified)
        {
            Target = target;
            LastModified = lastModified;
        }

        public RelativePath Target { get; }

        public DateTimeOffset LastModified { get; }
    }

    /// <summary>
    /// A write-only stream that buffers into a <see cref="MemoryStream"/> and invokes
    /// a commit callback when disposed.
    /// </summary>
    private sealed class CommitOnDisposeStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly Action<byte[]> _commit;
        private bool _committed;

        public CommitOnDisposeStream(MemoryStream inner, Action<byte[]> commit)
        {
            _inner = inner;
            _commit = commit;
        }

        public override bool CanRead => false;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => true;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException("This stream is write-only.");

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
            => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_committed)
            {
                _committed = true;
                var bytes = _inner.ToArray();
                _inner.Dispose();
                _commit(bytes);
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            Dispose(true);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}