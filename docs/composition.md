# Composition

Decorators in `VinDust.SharpVfs.Composition` wrap other file systems and change their behavior.

## `ReadOnlyFileSystem`

Presents a read-only view of an inner file system. All mutating operations throw `VfsReadOnlyException`. Capabilities drop write-related flags.

```csharp
await using var fs = new ReadOnlyFileSystem(new PhysicalFileSystem("/data"));
```

- Does not implement `IMountableFileSystem`. To mount into a read-only view, wrap the read-only view in `MountedFileSystem`.
- Owns the inner file system; disposing the read-only view disposes the inner.

## `UnionFileSystem`

Presents a list of file systems as one. Layers are ordered top-priority first.

```csharp
await using var union = new UnionFileSystem([overlay, baseLayer]);
```

### Read semantics

Lookups walk layers top to bottom. The first layer that has the entry wins. Directory listings merge across layers: entries from higher layers win on name conflicts.

### Write semantics

Writes go to the first writable layer (by default; see write policies below). If no layer is writable, `VfsReadOnlyException`.

### Delete semantics

Delete removes the entry from every writable layer that has it. Entries that exist only in read-only lower layers remain visible; there are no whiteouts in v1.

### Move / Copy

If source and destination resolve to the same writable layer, the operation is delegated. For copy across layers, a stream-based fallback is used. For move across layers, `NotSupportedException` is thrown.

### Ordering matters

The layer order is fixed at construction and never changes. If you need different priorities for different paths, construct two unions or use a write policy that reads the path.

## Write policies

`IWritePolicy` selects the target layer for a write operation.

```csharp
public interface IWritePolicy
{
    ValueTask<IFileSystem> SelectAsync(WriteContext context, CancellationToken ct = default);
}
```

`WriteContext` carries the path being written and the list of candidates (writable layers, ordered top-priority first).

### Built-in policies

- **`PrimaryWritePolicy`** (default) — always picks the first candidate (topmost writable layer).

### Planned

- `MostFreeSpacePolicy` — picks the candidate with the most free space.
- `RoundRobinPolicy` — round-robins writes across candidates.
- `LambdaPolicy` — delegates selection to a `Func<WriteContext, IFileSystem>`.
- `CopyOnWritePolicy` — writes to the top layer; if a file exists only in a lower layer, copies it up first.

## Composition recipes

### Overlay a ZIP archive with a writable in-memory layer

```csharp
// Base archive (read-only).
var zipStream = File.OpenRead("/mods/mod.zip");
var zipFs = new ZipArchiveFileSystem(zipStream, leaveOpen: false, ZipFileSystemOptions.Default);
var zipMounted = new MountedFileSystem(zipFs);

// Writable overlay.
var overlay = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });

// Present the overlay on top of the archive.
var union = new UnionFileSystem([overlay, zipMounted]);
await using var root = new VfsRoot();
await root.MountAsync(VfsUri.Parse("mem:///mod.zip"), union, MountOptions.Default);
```

Reads of `/mod.zip/settings.json` fall through to the archive if the overlay does not have it. Writes go to the overlay.

### Nest a file system inside an archive

```csharp
var zipMounted = new MountedFileSystem(zipFs);
var innerFs = new MemoryFileSystem();

// Mount innerFs at /sub inside the archive's tree.
await zipMounted.MountAsync(FsPath.Parse("/sub"), innerFs, MountOptions.Default);
```

`/mod.zip/sub/file` resolves: outer → `zipMounted` → mount point `/sub` → `innerFs`.

### Overlay a subdirectory of an archive

Because `MountedFileSystem` uses longest-prefix matching, a mount at `/sub` shadows a mount at `/` for paths under `/sub`. If you need both, order is: mount the more specific path first, or use `UnionFileSystem` on the parent.

## When to use composition vs mounting

- **Mounting** — when one tree attaches to another at a specific location, and the attachment should be transparent to readers.
- **Union** — when multiple sources contribute to a single namespace and you need policy-driven writes across all of them.
- **Read-only view** — when you need to expose a file system safely, e.g. to a plugin.

You can combine all three.