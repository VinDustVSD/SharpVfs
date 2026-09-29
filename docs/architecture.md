# Architecture

## Layers

SharpVfs has three layers, stacked from bottom to top:

1. **Abstractions** (`VinDust.SharpVfs.Abstractions`) — contracts, path types, exceptions, options, entry hierarchy. Everything a file system implementation or a consumer needs to know.
2. **Core** (`VinDust.SharpVfs.Core`) — the machinery: `PathTrie`, `MountTable`, `MountedFileSystem`, `PathResolver`, `VfsRoot`, `VfsWalker`, `DirectoryMerger`.
3. **Implementations and composition** — concrete file systems (`MemoryFileSystem`, `PhysicalFileSystem`, `ZipArchiveFileSystem`), decorators (`UnionFileSystem`, `ReadOnlyFileSystem`), policies, hosting, diagnostics.

Code in a lower layer never references a higher one.

## The three path types

Paths are split by role, not by representation. See the table in the README. The important consequences:

- `VfsUri` is the only type a user ever constructs by hand at the top level.
- `IFileSystem` methods take and return `FsPath` exclusively. A file system never knows about schemes or mount points.
- `RelativePath` is a *navigation operation*, not an identifier. It can contain `..` and is resolved against a base via `ResolveAgainst`.
- All three are `readonly struct`, `IEquatable<T>`, `IComparable<T>`, `IComparable`, with operators `==`, `!=`, `<`, `<=`, `>`, `>=`.
- All three store **segments** (`string[]`), not just a canonical string. `GetParent`, `GetFileName`, `SubPath`, `StartsWith` are all O(depth). `Parse` allocates one array.
- `StartsWith` compares segment by segment. `"/foo"` does **not** match `"/foobar"`.
- `CompareTo` uses segment-wise ordinal ordering, not string ordinal. `"/a/b"` sorts before `"/a-c"`.

## Levels of API

### `IFileSystem`

The low-level contract. Methods take `FsPath`, never strings, never `VfsUri`. Read, write, enumerate, and metadata. `IAsyncDisposable`. `Capabilities` advertises which operations are supported.

```csharp
public interface IFileSystem : IAsyncDisposable
{
    FileSystemCapabilities Capabilities { get; }
    ValueTask<bool> ExistsAsync(FsPath path, CancellationToken ct = default);
    ValueTask<FileSystemEntry?> GetEntryAsync(FsPath path, CancellationToken ct = default);
    IAsyncEnumerable<FileSystemEntry> EnumerateAsync(FsPath path, CancellationToken ct = default);
    ValueTask<Stream> OpenReadAsync(FsPath path, CancellationToken ct = default);
    ValueTask<Stream> OpenWriteAsync(FsPath path, FileWriteMode mode = FileWriteMode.Create, CancellationToken ct = default);
    ValueTask CreateDirectoryAsync(FsPath path, CancellationToken ct = default);
    ValueTask DeleteAsync(FsPath path, bool recursive = false, CancellationToken ct = default);
    ValueTask MoveAsync(FsPath source, FsPath destination, CancellationToken ct = default);
    ValueTask CopyAsync(FsPath source, FsPath destination, CancellationToken ct = default);
    ValueTask CreateSymbolicLinkAsync(FsPath path, RelativePath target, CancellationToken ct = default);
}
```

### `IMountableFileSystem`

Adds mount operations. A file system does not have to be mountable to be useful; it must only be mountable to host mount points. Non-mountable instances are automatically wrapped by `MountedFileSystem` when needed.

```csharp
public interface IMountableFileSystem : IFileSystem
{
    ValueTask<MountPoint> MountAsync(FsPath path, IFileSystem target, MountOptions options, CancellationToken ct = default);
    ValueTask UnmountAsync(FsPath path, CancellationToken ct = default);
    IReadOnlyCollection<MountPoint> GetMounts();
    MountPoint? TryResolveMount(FsPath path, out FsPath remaining);
    event EventHandler<MountTableChangedEventArgs>? MountTableChanged;
}
```

### `IPatchableContainerFileSystem`

Container-specific extension for efficient entry replacement without rewriting unchanged entries. See [delta-builder-guide.md](delta-builder-guide.md) for the full story.

### `VfsRoot`

The only user-facing entry point. Holds a scheme registry, a dispatcher, and a cache of mountable file systems (one per scheme). Accepts `VfsUri`; returns `VfsEntry` from lookups.

## Mounting model

### Mount points are local

`MountedFileSystem` holds a `MountTable` — a `PathTrie<MountEntry>` — with the mount points registered on that specific instance. Mount points do not propagate to nested file systems: a mount inside `A` is invisible to `B` unless `B` is mounted into `A`.

### Resolution is one level at a time

When a call arrives at `MountedFileSystem`, it:

1. Asks its `MountTable` for the longest matching mount point.
2. If none, delegates to its wrapped file system.
3. If a match, forwards to the target with the remaining path. The target may itself be a `MountedFileSystem`, and the process repeats.

This is a recursive dispatch, not a jump to the deepest target. Every decorator in the chain gets a chance to act. It is what makes `Overlay` (fall-through to a base layer) implementable in a decorator without special support in the resolver.

### `OverlapBehavior`

- **Replace** (default): the mount point shadows the base under that path. `Exists("/m/base.txt")` returns `false` if `/m` is mounted and `base.txt` only lives in the base file system.
- **Overlay**: reads fall through to the base when the target has no entry; directory listings merge top-down; writes go to the target.

### Cycle detection

`MountedFileSystem` uses an `AsyncLocal` scope (`MountScope`) to track the file systems currently on the dispatch stack. Every operation enters the scope; cycles throw `VfsMountCycleException`. Because it is `AsyncLocal`, the scope survives arbitrary `await` boundaries and covers the whole recursive chain.

`VfsChainResolver.Resolve` (used by `VfsRoot.GetResolutionAsync` and `VfsWalker`) keeps its own visited set — it walks all the way to the deepest target in one call and needs to detect cycles across multiple hops.

## Schemes

Schemes route between VFS worlds. `VfsRoot` holds an `ISchemeRegistry` that maps a `VfsScheme` to an `ISchemeHandler`. Handlers construct file systems without parameters derived from the URI — no `zip://path/to/archive.zip` constructors.

```csharp
registry.Register(VfsScheme.Parse("mem"), memoryFs);              // fixed instance
registry.Register(VfsScheme.Parse("zip"), new MyZipHandler());    // factory
```

A URI without a scheme is rejected. There is no implicit `file://`.

## Entry types

`FileSystemEntry` is an abstract class with `FileEntry`, `DirectoryEntry`, and `SymbolicLink` as subclasses. `Path` is an `FsPath`; metadata (size, times, attributes, `ContentType`, `ContentHash`) lives on the base class.

`VfsEntry` wraps a `FileSystemEntry` and adds `VfsUri`. This is what consumers see from `VfsRoot`.

## Composition vs mounting

Mounting adds one point of redirection. Composition (via `UnionFileSystem` and friends) presents multiple file systems as one, with its own rules. They solve different problems:

- **Mount** is right when you know exactly where one tree attaches to another and want the attachment to be transparent.
- **Union** is right when multiple sources contribute to a single namespace and you need to control reads and writes across all of them.

You can combine them. See [composition.md](composition.md).

## Diagnostics

`IResolveTracer` lives in `Core` because resolution knows where to emit events. The `Diagnostics` package will provide concrete tracers and decorators. Logging frameworks are deliberately not referenced by `Core`.