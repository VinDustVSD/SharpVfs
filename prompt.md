# SharpVfs — Reference for AI assistants

This document is a self-contained reference for an AI assistant working with the
SharpVfs library. It describes the public API, the design principles that shape it,
and the constraints under which consumers must operate. Read this in full before
generating code that uses SharpVfs.

---

## 1. What SharpVfs is

SharpVfs is a virtual file system library for .NET 10. Its distinguishing features
compared to Zio, A·VFS, and TrimKit.VirtualFileSystem:

1. **Writable union mounts.** Layers of file systems can be presented as one, with
   reads falling through and writes routed by a pluggable `IWritePolicy`. Zio's
   `AggregateFileSystem` is read-only; SharpVfs is not.
2. **Three distinct path types.** Identity (`VfsUri`), local path (`FsPath`), and
   navigation (`RelativePath`) are separate types. The invalid cannot be represented.
3. **Async from the ground up.** Every public API is `ValueTask` / `IAsyncEnumerable`
   / `CancellationToken`. `IFileSystem : IAsyncDisposable`.

Status: pre-1.0. Breaking changes possible until 1.0.

---

## 2. Package map

| Package | Purpose |
|---|---|
| `VinDust.SharpVfs.Abstractions` | Contracts, path types, exceptions, options, entry hierarchy, policies |
| `VinDust.SharpVfs.Core` | `VfsRoot`, `MountedFileSystem`, `PathTrie`, `MountTable`, `VfsWalker` |
| `VinDust.SharpVfs.FileSystems.Memory` | In-memory file system |
| `VinDust.SharpVfs.FileSystems.Physical` | Disk-backed file system |
| `VinDust.SharpVfs.FileSystems.Zip` | ZIP archive file system + `IPatchableContainerFileSystem` |
| `VinDust.SharpVfs.Composition` | `UnionFileSystem`, `ReadOnlyFileSystem`, future `CopyOnWriteFileSystem` |
| `VinDust.SharpVfs.Hosting` | DI integration, `VfsBuilder` (planned) |
| `VinDust.SharpVfs.Sync` | Sync-over-async extensions (planned) |
| `VinDust.SharpVfs.Diagnostics` | Tracers, decorators (planned) |

Namespaces follow package names: `VinDust.SharpVfs.Abstractions`, `VinDust.SharpVfs.Core`, etc.

---

## 3. The three path types

| Type | Purpose | Example | Scheme | `..` | `string` in API |
|---|---|---|---|---|---|
| `VfsUri` | Node identity across the whole tree | `mem:///mods/config.json` | required | rejected | never |
| `FsPath` | Path within one file system | `/mods/config.json` | none | rejected | never |
| `RelativePath` | Navigation | `../assets/foo.png` | none | allowed | never |

All three are `readonly struct`, implement `IEquatable<T>`, `IComparable<T>`, and
have operators `==`, `!=`, `<`, `<=`, `>`, `>=`. All store **segments** (`string[]`),
not just a canonical string; `GetParent`, `GetFileName`, `SubPath`, `StartsWith` are
O(depth). `Parse` allocates one array.

### Rules

- `VfsUri.Parse` requires three slashes after the scheme: `mem:///foo`, not `mem://foo`.
- `FsPath` requires a leading `/`.
- Control characters (`U+0000..U+001F`, `U+007F`), NUL, empty segments, and trailing
  slashes are rejected.
- `..` is rejected in `VfsUri` and `FsPath`; allowed in `RelativePath`.
- `.` segments are dropped when parsing `RelativePath`; preserved nowhere else.
- `StartsWith` compares segment-by-segment: `"/foo"` does not match `"/foobar"`.
- `CompareTo` is segment-wise ordinal. `"/a/b"` sorts before `"/a-c"`.

### String overloads

Never accepted in public interface signatures. Extension methods only:

```csharp
"mem:///a".ToVfsUri();     // VfsUri
"/a/b".ToFsPath();         // FsPath
"../a".ToRelativePath();   // RelativePath
```

### Construction

```csharp
VfsUri.Parse("mem:///mods/config.json");
VfsUri.Parse("mem:///");                     // root of "mem" world
FsPath.Parse("/mods/config.json");
FsPath.Root;                                 // singleton "/"
RelativePath.Current;                        // singleton "."
RelativePath.Parse("../assets");
```

---

## 4. Level 1: `IFileSystem`

Low-level contract. Operates exclusively on `FsPath`. Knows nothing about schemes or
mount points.

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

`FileWriteMode`:

- `Create` — create or truncate.
- `CreateExclusive` — create; throw if exists.
- `Append` — open for append; create if missing.

End users **never** interact with `IFileSystem` directly. Use `VfsRoot`. The only
exceptions: writing tests for a specific file system, and building decorators.

### `FileSystemCapabilities`

Flags enum. `Read`, `Write`, `CreateDirectory`, `Delete`, `Move`, `Copy`, `Symlinks`,
`PatchableContainer`, `Seekable`. Advisory: check them before invoking optional
operations. When an operation is unsupported, the implementation throws
`NotSupportedException`.

### Entry hierarchy

```csharp
public abstract class FileSystemEntry
{
    public FsPath Path { get; }
    public long Size { get; init; }
    public DateTimeOffset LastModified { get; init; }
    public DateTimeOffset Created { get; init; }
    public DateTimeOffset Accessed { get; init; }
    public FileAttributes Attributes { get; init; }
    public string? ContentType { get; init; }
    public string? ContentHash { get; init; }   // opaque, "<algorithm>:<hex>"
}

public sealed class FileEntry : FileSystemEntry { }
public sealed class DirectoryEntry : FileSystemEntry { }
public sealed class SymbolicLink : FileSystemEntry
{
    public RelativePath Target { get; }
}
```

Pattern-match on the concrete type to determine entry kind:
```csharp
if (entry is FileEntry) { /* file */ }
if (entry is DirectoryEntry) { /* directory */ }
if (entry is SymbolicLink link) { /* use link.Target */ }
```

`SymbolicLink` is neither file nor directory. It is its own node type.
`ContentHash` format is opaque; do not parse the algorithm out of it without
checking. It is populated eagerly by file systems that can compute it cheaply
(ZIP: CRC32 from the central directory; Memory: not currently; Physical: not
currently). To compute on demand, use `VfsRoot.GetContentHashAsync`.

---

## 5. Level 2: `IMountableFileSystem`

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

`MountPoint` is a record: `(FsPath Path, IFileSystem Target, MountOptions Options, DateTimeOffset MountedAt)`.

`MountOptions`:

- `CreateIfMissing` (default `true`) — create the mount directory in the base FS.
- `OverlapBehavior` — `Replace` (default) or `Overlay`.
  - `Replace` — the mount point fully shadows the base under that path.
  - `Overlay` — reads fall through to the base; listings merge top-down; writes
    go to the target.
- `ReadOnly` — write operations through the mount throw `VfsReadOnlyException`.
- `SymlinkScope` — `SameFs` (default) or `SameRoot`. Currently advisory; symlink
  resolution is not yet implemented.
- `WritePolicy` — used only with `OverlapBehavior.Overlay`. Currently not consumed
  by `MountedFileSystem`; reserved for future `CopyOnWrite` composition.

### `MountedFileSystem`

A decorator that adds mount capability to any `IFileSystem`. `DefaultMountDecoratorFactory`
wraps non-mountable file systems automatically when a scheme is registered in `VfsRoot`.

Mount points are stored **locally** in each `MountedFileSystem`. They do not propagate
to nested file systems.

Dispatch is **one level at a time**: `MountedFileSystem` finds the longest matching
mount point for a path, forwards to the target with the remaining path, and the
process repeats at the target if it is itself a `MountedFileSystem`. This is what
makes `Overlay` implementable: each decorator in the chain gets a chance to act.

Cycle detection uses an `AsyncLocal` scope; cycles throw `VfsMountCycleException`.

---

## 6. Level 3: `VfsRoot` — the user-facing entry point

```csharp
var registry = new DefaultSchemeRegistry();
registry.Register(VfsScheme.Parse("file"), physicalFs);   // fixed instance
registry.Register(VfsScheme.Parse("mem"),  memoryFs);

await using var root = new VfsRoot(
    registry,
    decoratorFactory: null,      // DefaultMountDecoratorFactory.Instance
    hashProvider: null);         // optional IContentHashProvider
```

`VfsRoot` methods, all taking `VfsUri`:

```csharp
ValueTask<bool> ExistsAsync(VfsUri uri, CancellationToken ct = default);
ValueTask<VfsEntry?> GetEntryAsync(VfsUri uri, CancellationToken ct = default);
IAsyncEnumerable<VfsEntry> EnumerateAsync(VfsUri uri, CancellationToken ct = default);
ValueTask<Stream> OpenReadAsync(VfsUri uri, CancellationToken ct = default);
ValueTask<Stream> OpenWriteAsync(VfsUri uri, FileWriteMode mode = FileWriteMode.Create, CancellationToken ct = default);
ValueTask CreateDirectoryAsync(VfsUri uri, CancellationToken ct = default);
ValueTask DeleteAsync(VfsUri uri, bool recursive = false, CancellationToken ct = default);
ValueTask CreateSymbolicLinkAsync(VfsUri uri, RelativePath target, CancellationToken ct = default);
ValueTask<string?> GetContentHashAsync(VfsUri uri, CancellationToken ct = default);
ValueTask<MountPoint> MountAsync(VfsUri mountPoint, IFileSystem target, MountOptions options, CancellationToken ct = default);
ValueTask UnmountAsync(VfsUri mountPoint, CancellationToken ct = default);
ValueTask<IReadOnlyCollection<MountPoint>> GetMountsAsync(VfsUri uri, CancellationToken ct = default);
ValueTask<VfsResolution> GetResolutionAsync(VfsUri uri, CancellationToken ct = default);
```

`VfsEntry` wraps a `FileSystemEntry` and adds `Uri`. Properties forward to the entry:
`Path`, `Size`, `LastModified`, `Created`, `Accessed`, `Attributes`, `ContentType`,
`ContentHash`, `IsFile`, `IsDirectory`, `IsSymbolicLink`.

`VfsResolution` describes where a URI resolves: `FileSystem`, `Path`, `MountChain`
(ordered outermost first), `ReadOnly`, `CrossedMounts`.

`VfsRoot` caches one decorated, mountable file system per registered scheme for the
lifetime of the root. Disposing the root disposes all cached file systems.

### Schemes

Schemes route between VFS worlds. They map 1:1 to file system instances or to
parameterless factories. **No schemes-as-constructors**: `zip://path/to/archive.zip`
is not supported. `file:///foo` maps to whatever `IFileSystem` is registered under
`file`, and the path portion `/foo` is resolved within it.

A URI without a scheme is rejected. There is no implicit `file://`.

`ISchemeRegistry`:

```csharp
IReadOnlyCollection<VfsScheme> RegisteredSchemes { get; }
ISchemeHandler? GetHandler(VfsScheme scheme);
void Register(VfsScheme scheme, ISchemeHandler handler);   // throws if already registered
bool Unregister(VfsScheme scheme);
```

Extension: `registry.Register("mem".ToVfsScheme(), fs)` registers a `FixedSchemeHandler`
for a fixed instance.

---

## 7. File systems

### `MemoryFileSystem`

In-memory tree. Content and metadata live in managed memory for the instance's lifetime.

Constructor: `new MemoryFileSystem(MemoryFileSystemOptions? options = null)`.

Options (`MemoryFileSystemOptions : VfsBehaviorOptions`):

- `CreateParentOnWrite` (default `false`) — create missing ancestors on write.
- `DirectoryExistsBehavior` — `Ok` (default) or `Throw`.
- `DeleteBehavior` — `MissingOk` (default) or `Throw`.

Behavior:

- Read streams are snapshots.
- Write streams commit on `Dispose`; content in an undisposed stream is lost.
- Concurrent writers to the same file: last-dispose-wins.
- `FileWriteMode.Create` truncates.
- Symbolic links are stored and surfaced but not followed on read.

### `PhysicalFileSystem`

A view over a directory subtree on the local disk.

Constructor: `new PhysicalFileSystem(string rootPath, PhysicalFileSystemOptions? options = null)`.

`rootPath` must be absolute. If the directory does not exist, the constructor throws
unless `PhysicalFileSystemOptions.CreateRootIfMissing` is set.

Behavior:

- `FsPath` segments cannot be `..`, so the mapping to a real path cannot escape the
  root. Following symlinks that point outside the root is permitted.
- Symbolic links are surfaced as `SymbolicLink` entries; read/write follow them.
- `CreateSymbolicLinkAsync` may throw `UnauthorizedAccessException` on Windows
  without developer mode.

### `ZipArchiveFileSystem`

ZIP archive, read or read-write. Implements `IPatchableContainerFileSystem`.

Constructors:

```csharp
new ZipArchiveFileSystem(string path);                             // open read-write
new ZipArchiveFileSystem(string path, ZipFileSystemOptions options);
new ZipArchiveFileSystem(Stream stream);                           // takes ownership
new ZipArchiveFileSystem(Stream stream, ZipFileSystemOptions options);
new ZipArchiveFileSystem(Stream stream, bool leaveOpen, ZipFileSystemOptions options);
```

Behavior:

- The whole archive is loaded into memory at construction.
- Read streams are seekable (`MemoryStream`).
- Write operations trigger a full repack of the archive. This is honest about cost:
  `System.IO.Compression.ZipArchive` does not expose in-place updates.
- When the source stream is not writable, the file system is read-only and write
  operations throw `VfsReadOnlyException`.
- Use `BeginWriteSession()` to batch multiple mutations into a single repack.

Raw access:

- `GetRawEntryNames()` — all entry names as stored in the central directory.
- `OpenRawEntry(string fullName)` — open an entry by exact name, bypassing `FsPath`
  validation. Use this for archives containing invalid names (absolute paths,
  `..`, control characters).

Not supported: symbolic links, moving or copying non-empty directories
(`NotSupportedException`).

Capabilities:

- Read-only stream: `Read | Seekable`.
- Read-write stream: `Read | Write | CreateDirectory | Delete | Move | Copy | Seekable | PatchableContainer`.

---

## 8. `IPatchableContainerFileSystem`

Container-specific contract for replacing a single entry's content.

```csharp
public interface IPatchableContainerFileSystem : IFileSystem
{
    ValueTask ReplaceEntryAsync(
        FsPath path,
        Stream newContent,
        ContainerPatchOptions options,
        CancellationToken ct = default);

    IContainerWriteSession BeginWriteSession();
}
```

Contract: *do not overwrite the bytes of unchanged entries when the underlying format
allows it.* When in-place update is not feasible, the implementation falls back to a
full repack. In ZIP's v1 implementation, all writes are repacks — the contract will
be honored for ZIP in a follow-up.

`ReplaceEntryAsync` requires the entry to exist. For new entries, use
`OpenWriteAsync(path, FileWriteMode.Create)`.

`ContainerPatchOptions`:

- `PreferInPlace` (default `true`) — try in-place when the format allows.
- `CompressionLevel` — `CompressionLevel?` for recompression.

### Write sessions

```csharp
public interface IContainerWriteSession : IAsyncDisposable
{
    ValueTask CommitAsync(CancellationToken ct = default);
    void Rollback();
}
```

Rules:

- One session per file system. Nested `BeginWriteSession` throws `InvalidOperationException`.
- Reads inside a session see the overlay: read-your-writes.
- All write streams must be closed before `CommitAsync`. Otherwise `InvalidOperationException`.
- `DisposeAsync` without commit performs a rollback.
- `Rollback()` discards all changes and closes the session.
- After commit or rollback, the session is spent.

Use sessions for batch mutations. A session applies all changes in one repack;
without a session, each mutation triggers its own repack.

---

## 9. Composition

### `ReadOnlyFileSystem`

Presents a read-only view of an inner file system.

```csharp
await using var fs = new ReadOnlyFileSystem(new PhysicalFileSystem("/data"));
```

- Every mutating operation throws `VfsReadOnlyException`.
- Capabilities drop write-related flags.
- Owns the inner; disposing disposes the inner.
- Does **not** implement `IMountableFileSystem`. To mount into a read-only view,
  wrap in `MountedFileSystem`.

### `UnionFileSystem`

Presents a list of file systems as one. Layers ordered top-priority first.

```csharp
await using var union = new UnionFileSystem([overlay, baseLayer]);
await using var union = new UnionFileSystem([top, middle, bottom], writePolicy);
```

Read semantics:

- Lookups walk layers top to bottom. First layer with the entry wins.
- Directory listings merge across layers; on name conflicts, higher layers win.

Write semantics:

- Writes go to the first writable layer (or per `IWritePolicy`).
- If no layer is writable, `VfsReadOnlyException`.

Delete semantics:

- Deletes from every writable layer that has the entry.
- Entries existing only in read-only lower layers remain visible (no whiteouts in v1).

Move / Copy:

- Same-layer transfers delegate to the target layer.
- Cross-layer copy falls back to stream-based copy.
- Cross-layer move throws `NotSupportedException`.

`UnionFileSystem.Layers` and `WritePolicy` are exposed. Disposing the union disposes
every layer.

### `IWritePolicy`

```csharp
public interface IWritePolicy
{
    ValueTask<IFileSystem> SelectAsync(WriteContext context, CancellationToken ct = default);
}

public sealed class WriteContext
{
    public FsPath Path { get; }
    public IReadOnlyList<IFileSystem> Candidates { get; }   // top-priority first
}
```

Built-in: `PrimaryWritePolicy.Instance` — always picks the first candidate.

Planned: `MostFreeSpacePolicy`, `RoundRobinPolicy`, `LambdaPolicy`, `CopyOnWritePolicy`.

---

## 10. Walking

`VfsWalker` recursively traverses a tree, optionally descending into archives.

```csharp
public interface IVfsWalkerVisitor
{
    ValueTask OnDirectoryAsync(VfsUri uri, CancellationToken ct);
    ValueTask OnFileAsync(VfsUri uri, FileSystemEntry entry, CancellationToken ct);
    ValueTask OnErrorAsync(VfsUri uri, Exception exception, CancellationToken ct)
        => throw exception;
}

public interface IArchiveDetector
{
    bool IsArchive(VfsUri uri, FileSystemEntry entry);
}

public interface IArchiveOpener
{
    ValueTask<IFileSystem?> OpenAsync(VfsUri uri, Stream content, CancellationToken ct);
}

public sealed class VfsWalker
{
    public VfsWalker(
        VfsRoot root,
        IVfsWalkerVisitor visitor,
        IArchiveDetector? archiveDetector = null,
        IArchiveOpener? archiveOpener = null);

    public ValueTask WalkAsync(VfsUri start, CancellationToken ct = default);
}
```

- `archiveDetector` and `archiveOpener` must be provided together or not at all.
- The opener **takes ownership of the stream** it is given.
- The walker disposes the archive's file system when it moves past it.
- The walker does not modify permanent mount state.

Built-in detector: `ExtensionArchiveDetector.Zip` (matches `.zip`).

---

## 11. Exceptions

All exceptions from the public API derive from `VfsException`, with two exceptions:

- `OperationCanceledException` — cancellation is standard .NET; never wrapped.
- `NotSupportedException` — signals "this file system does not implement the operation".

Hierarchy:

- `VfsException` — base.
- `VfsInvalidPathException` — malformed path string.
- `VfsNotFoundException` — entry or mount point missing.
- `VfsNotDirectoryException` — operation requires a directory, or a directory operation received a file.
- `VfsReadOnlyException` — write attempted against a read-only file system or mount.
- `VfsSchemeNotFoundException` — URI scheme is not registered.
- `VfsMountCycleException` — mount chain would form a cycle.
- `VfsPathEscapeException` — resolved path escapes its file system's boundary (reserved for symlinks; not currently thrown).

Never throw or expect raw `IOException`, `UnauthorizedAccessException`, or other
BCL I/O exceptions across the public API.

---

## 12. Content hashing

`ContentHash` on entries is opaque: `"<algorithm>:<hex>"`, e.g. `"sha256:..."` or
`"crc32:..."`. Do not parse the algorithm for identity purposes; compare strings
literally.

`IContentHashProvider`:

```csharp
public interface IContentHashProvider
{
    ValueTask<string?> ComputeAsync(Stream content, CancellationToken ct = default);
}
```

`VfsRoot.GetContentHashAsync(uri)`:

1. If the entry has `ContentHash` populated, return it without I/O.
2. Otherwise, if the root has a configured provider, open the content and call it.
3. Otherwise, return `null`.

---

## 13. Patterns and recipes

### Opening a root

```csharp
var physical = new PhysicalFileSystem("/builds/v1", new PhysicalFileSystemOptions
{
    CreateParentOnWrite = true,
});

var registry = new DefaultSchemeRegistry();
registry.Register(VfsScheme.Parse("file"), physical);

await using var root = new VfsRoot(registry);
```

### Write-then-read

```csharp
await using (var w = await root.OpenWriteAsync(VfsUri.Parse("file:///a/b/c.txt")))
{
    await w.WriteAsync(bytes);
}
// File is committed when the stream is disposed.

await using var r = await root.OpenReadAsync(VfsUri.Parse("file:///a/b/c.txt"));
```

**Always dispose write streams before checking their effect.** Content in an open
stream is not visible to other operations.

### Mount with Replace

```csharp
await root.MountAsync(
    VfsUri.Parse("file:///mods"),
    overlayFs,
    new MountOptions { OverlapBehavior = MountOverlapBehavior.Replace });
```

Content of the base under `/mods` is hidden.

### Mount with Overlay

```csharp
await root.MountAsync(
    VfsUri.Parse("file:///mods"),
    overlayFs,
    new MountOptions { OverlapBehavior = MountOverlapBehavior.Overlay });
```

Reads fall through to the base; writes go to `overlayFs`.

### Read-only mount

```csharp
await root.MountAsync(
    VfsUri.Parse("file:///readonly"),
    otherFs,
    new MountOptions { ReadOnly = true });
```

Writes through `/readonly/**` throw `VfsReadOnlyException`. Reads pass through.

### Nested mounts

```csharp
var middleMounted = new MountedFileSystem(new MemoryFileSystem());
await middleMounted.MountAsync(FsPath.Parse("/sub"), leaf, MountOptions.Default);

await root.MountAsync(VfsUri.Parse("mem:///outer"), middleMounted, MountOptions.Default);

// mem:///outer/sub/file resolves through both mounts.
```

### Auto-mounting archives during traversal

```csharp
public sealed class ZipOpener : IArchiveOpener
{
    public ValueTask<IFileSystem?> OpenAsync(VfsUri uri, Stream content, CancellationToken ct)
    {
        IFileSystem fs = new ZipArchiveFileSystem(content, leaveOpen: false, ZipFileSystemOptions.Default);
        return new(fs);
    }
}

var visitor = new MyVisitor();
var walker = new VfsWalker(root, visitor, ExtensionArchiveDetector.Zip, new ZipOpener());
await walker.WalkAsync(VfsUri.Parse("file:///"));
```

### Distinguishing inner-archive entries from disk entries

```csharp
var resolution = await root.GetResolutionAsync(uri);
if (resolution.CrossedMounts)
{
    // uri resolves inside a container; resolution.MountChain[^1].Path is the container.
    // resolution.Path is the path within resolution.FileSystem.
}
```

Do **not** parse URIs by hand to detect archive boundaries. Use `GetResolutionAsync`.

### Patching a single entry inside an archive

```csharp
await using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
await using var zip = new ZipArchiveFileSystem(stream, leaveOpen: true, ZipFileSystemOptions.Default);
var patchable = (IPatchableContainerFileSystem)zip;

await using (var newContent = File.OpenRead("/path/to/new-content"))
{
    await patchable.ReplaceEntryAsync(
        FsPath.Parse("/config.json"),
        newContent,
        ContainerPatchOptions.Default);
}
```

`ReplaceEntryAsync` requires the entry to already exist. For new entries:

```csharp
await using (var w = await zip.OpenWriteAsync(FsPath.Parse("/new.txt"), FileWriteMode.Create))
{
    await w.WriteAsync(bytes);
}
```

### Batching container mutations

```csharp
var patchable = (IPatchableContainerFileSystem)zip;
await using var session = patchable.BeginWriteSession();

await using (var w = await zip.OpenWriteAsync(FsPath.Parse("/a.txt"), FileWriteMode.Create))
{
    await w.WriteAsync(aBytes);
}

await using (var w = await zip.OpenWriteAsync(FsPath.Parse("/b.txt"), FileWriteMode.Create))
{
    await w.WriteAsync(bBytes);
}

await zip.DeleteAsync(FsPath.Parse("/obsolete.txt"));

await session.CommitAsync();   // one repack
```

### Layering a writable overlay over a read-only archive

Do **not** try to mount a writable file system on top of a mount point. Longest-prefix
matching will route directly to the deeper mount. Instead, use `UnionFileSystem`:

```csharp
var zipStream = File.OpenRead("/mods/mod.zip");
var zip = new ZipArchiveFileSystem(zipStream, leaveOpen: false, ZipFileSystemOptions.Default);
var zipMounted = new MountedFileSystem(zip);

var overlay = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });

var union = new UnionFileSystem([overlay, zipMounted]);
await root.MountAsync(VfsUri.Parse("mem:///mod.zip"), union, MountOptions.Default);
```

Reads fall through to the archive when the overlay has no entry. Writes go to the
overlay.

### Nesting a file system inside an archive

```csharp
var zipMounted = new MountedFileSystem(zip);
var innerFs = new MemoryFileSystem();
await zipMounted.MountAsync(FsPath.Parse("/sub"), innerFs, MountOptions.Default);
```

`mem:///mod.zip/sub/file` resolves through both: the outer mount lands on `zipMounted`,
its `/sub` mount forwards to `innerFs`.

---

## 14. Design principles

Follow these when writing code that uses or extends SharpVfs.

1. **Use `VfsRoot` at the top level.** Do not touch `IFileSystem` from application
   code. The only reasons to hold an `IFileSystem` are tests for a specific
   implementation and building decorators.

2. **Never pass strings where path types are expected.** `VfsUri`, `FsPath`,
   `RelativePath` in signatures; extension methods at the edges.

3. **Dispose everything.** `IFileSystem`, `VfsRoot`, `IContainerWriteSession` are
   `IAsyncDisposable`. Write streams must be disposed to commit content.

4. **Respect longest-prefix mount semantics.** A mount at `/a/b` wins over a mount
   at `/a` for paths under `/a/b`. Do not attempt to stack mounts at the same or
   nested paths to "layer" behavior — use `UnionFileSystem`.

5. **One level of dispatch at a time.** `MountedFileSystem` forwards to its target
   one hop at a time. Each decorator in the chain gets a chance to act. Do not
   try to jump to the deepest target from user code.

6. **Prefer `GetResolutionAsync` over parsing URIs.** Mount points are not encoded
   in URIs; only the resolution can tell you where a URI really lands.

7. **Batch container mutations with `BeginWriteSession`.** A session repacks once.
   Without a session, each mutation repacks separately.

8. **`ReplaceEntryAsync` for existing entries, `OpenWriteAsync(Create)` for new
   entries.** They are not interchangeable.

9. **Advisory capabilities.** Check `Capabilities` before invoking optional
   operations. Do not assume a file system supports `Symlinks` or `Move`.

10. **No `..` in `VfsUri` or `FsPath`.** Use `RelativePath` for navigation.

11. **Catch `VfsException` and its descendants, not `IOException`.** The public API
    normalizes I/O errors.

12. **Never wrap `OperationCanceledException`.** Let it propagate.

---

## 15. Limitations (current, v0.x)

These are known gaps. Do not attempt to work around them by hand — request an
improvement or use a different design.

- **Symbolic links are not followed on read.** `SymbolicLink` entries are surfaced,
  and `SymlinkScope` is declared in `MountOptions`, but the resolver does not
  dereference. `OpenReadAsync` on a symlink path treats it as a missing file.
  Depth limit (Linux-style 40) is planned.

- **No in-place ZIP patching.** `ReplaceEntryAsync` and `OpenWriteAsync` on ZIP
  trigger a full repack. The contract of `IPatchableContainerFileSystem` is not yet
  satisfied byte-for-byte for ZIP. A hand-written ZIP writer is a follow-up.

- **No whiteouts in `UnionFileSystem`.** Deletes only affect writable layers. An
  entry that exists in a read-only lower layer remains visible after deletion.

- **Cross-file-system move and copy are not supported by `MountedFileSystem`** —
  `NotSupportedException`. `UnionFileSystem` supports cross-layer copy via streams.

- **`MountedFileSystem` does not support moving or copying non-empty directories
  inside ZIP.**

- **No `IFileSystemWatcher`.** Removed from v1; will return in v1.1+.

- **`PathResolver` is not exposed**; the resolution logic lives in
  `VfsChainResolver` (internal static) and `MountedFileSystem`. Public surface is
  `VfsRoot.GetResolutionAsync`.

- **No `TAR` or `7z` support yet.** Contract is format-agnostic; implementations
  are follow-ups.

- **`MountOptions.WritePolicy` is not yet consumed by `MountedFileSystem`.** Reserved
  for a future `CopyOnWrite` composition.

- **`IResolveTracer` is not actively wired.** The interface exists; the diagnostics
  package will provide concrete tracers, and `MountedFileSystem` will be updated to
  emit events.

---

## 16. Conventions the library follows

When extending SharpVfs, match these.

- **Target framework:** `net10.0`. Nullable enabled. LangVersion latest.
- **Code, identifiers, comments, XML-doc, exception messages, tests, commits:**
  English only.
- **XML-doc required** on every public member (`<summary>`, `<param>`, `<returns>`,
  `<exception>`, `<remarks>`). `CS1591` is an error.
- **`sealed` by default.** Inheritance requires justification.
- **File-scoped namespaces.**
- **Collection expressions** (`[]`) where target type allows.
- **Prefer `ObjectDisposedException.ThrowIf(_disposed, this)`** over manual throw.
- **`ConfigureAwait(false)`** in library code.
- **`[EnumeratorCancellation]`** on `ct` parameter in async iterators only; not on
  interface methods returning `IAsyncEnumerable`.
- **Segment-wise comparison** for paths, not string ordinal.
- **No `string` in public interface signatures.**

### Where a type goes

| Contract / data / interface | `Abstractions` |
| Machinery (stateful, algorithm) | `Core` |
| Format-specific | `FileSystems.<Format>` |
| Decorator | `Composition` |
| Policy | `Policies` or `Abstractions.Policies` (for the interface) |
| DI integration | `Hosting` |
| Tracing exporter | `Diagnostics` |

---

## 17. Glossary

- **Scheme** — short identifier (`mem`, `file`, `zip`) mapping to a file system or
  factory. Registered on `VfsRoot`.
- **Mount point** — a path inside one file system where another file system is
  attached.
- **Overlay** — mount behavior where the base layer stays visible and the target
  wins on conflicts.
- **Replace** — mount behavior where the target fully shadows the base under the
  mount path.
- **Union** — a composition presenting multiple file systems as one, without
  mount points.
- **Container** — a file that itself contains a hierarchy (ZIP, TAR, etc.).
- **Patchable container** — a container format that supports replacing a single
  entry's content without rewriting the whole file.
- **Write session** — a batching scope for container mutations, committed in a
  single repack.
- **Resolution chain** — the ordered list of mount points crossed while resolving
  a URI to its owning file system.

---

## 18. What to do when the API does not fit

If you find yourself:

- Parsing URI strings to detect archive boundaries → use `GetResolutionAsync`.
- Mounting at nested paths to "layer" file systems → use `UnionFileSystem`.
- Rewriting a whole archive after a single-entry change → check whether the format
  allows in-place; if not, batch with `BeginWriteSession`.
- Copying files across file systems by hand → check whether both ends share a
  writable layer; if not, use `UnionFileSystem` for stream-based copy.
- Following symbolic links manually → not implemented; request it or model the
  target as a separate file system.

If none of the above applies and the API still does not fit, that is a signal
the library should grow. Open an issue with a minimal reproduction.