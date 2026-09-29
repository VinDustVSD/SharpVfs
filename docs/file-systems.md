# File systems

## `MemoryFileSystem`

`VinDust.SharpVfs.FileSystems.Memory`

In-memory tree, no persistence. Content and metadata live in managed memory for the lifetime of the instance.

**Constructor:** `new MemoryFileSystem(MemoryFileSystemOptions? options = null)`.

**Options:** `MemoryFileSystemOptions` inherits `VfsBehaviorOptions`:

| Option | Default | Meaning |
|---|---|---|
| `CreateParentOnWrite` | `false` | Create missing ancestors on write |
| `DirectoryExistsBehavior` | `Ok` | `Ok` / `Throw` |
| `DeleteBehavior` | `MissingOk` | `MissingOk` / `Throw` |

**Capabilities:** Read, Write, CreateDirectory, Delete, Move, Copy, Symlinks, Seekable.

**Notes:**

- Read streams are snapshots. Modifying the file after opening a read stream does not affect the already-open stream.
- Write streams commit on `Dispose`. Content written to a stream that is never disposed is lost.
- Concurrent writers to the same file: last-dispose-wins.
- `OpenWriteAsync(path, FileWriteMode.Create)` truncates.

## `PhysicalFileSystem`

`VinDust.SharpVfs.FileSystems.Physical`

A view over a directory subtree on the local disk.

**Constructor:** `new PhysicalFileSystem(string rootPath, PhysicalFileSystemOptions? options = null)`.

`rootPath` must be absolute. If the directory does not exist, the constructor throws unless `CreateRootIfMissing` is set.

**Options:**

| Option | Default | Meaning |
|---|---|---|
| `CreateRootIfMissing` | `false` | Create the root directory if it does not exist |
| `CreateParentOnWrite` | `false` | Create missing ancestors on write |
| `DirectoryExistsBehavior` | `Ok` | `Ok` / `Throw` |
| `DeleteBehavior` | `MissingOk` | `MissingOk` / `Throw` |

**Capabilities:** Read, Write, CreateDirectory, Delete, Move, Copy, Symlinks, Seekable.

**Notes:**

- Segments of `FsPath` cannot be `..`, so mapping to a real path cannot escape the root. Following symbolic links that point outside the root is permitted (this is a view, not a sandbox).
- Symbolic links are surfaced as `SymbolicLink` entries. Read and write operations follow links transparently.
- If the environment does not allow creating symbolic links, `CreateSymbolicLinkAsync` throws `UnauthorizedAccessException` or `IOException`.

## `ZipArchiveFileSystem`

`VinDust.SharpVfs.FileSystems.Zip`

A file system backed by a ZIP archive, with read and write support. Implements `IPatchableContainerFileSystem`.

**Constructors:**

```csharp
new ZipArchiveFileSystem(string path);                          // open read-write
new ZipArchiveFileSystem(string path, ZipFileSystemOptions o);
new ZipArchiveFileSystem(Stream stream);                        // ownership of stream
new ZipArchiveFileSystem(Stream stream, ZipFileSystemOptions o);
new ZipArchiveFileSystem(Stream stream, bool leaveOpen, ZipFileSystemOptions o);
```

**Options:** `ZipFileSystemOptions` inherits `VfsBehaviorOptions`. Currently empty; fields will be added as the writer matures.

**Capabilities:**

- Read-only stream: Read, Seekable.
- Read-write stream: Read, Write, CreateDirectory, Delete, Move, Copy, Seekable, PatchableContainer.

**Behavior:**

- The whole archive is loaded into memory at construction. Reads are seekable.
- Writes are a full repack of the in-memory content back to the underlying stream. This is honest: `ZipArchive` in .NET does not expose in-place updates. In-place is a follow-up.
- Use `BeginWriteSession` to batch multiple mutations into one repack.
- `OpenWriteAsync` writes to a session overlay when a session is active; otherwise the change is applied immediately.
- Unchanged entries' bytes are not preserved on disk in v1 — a full repack rewrites everything. The contract of `IPatchableContainerFileSystem` ("do not overwrite unchanged entry bytes when the format allows it") is satisfied for ZIP in a future in-place implementation.

**Raw access:**

- `GetRawEntryNames()` — returns all entry names as stored in the central directory.
- `OpenRawEntry(string fullName)` — opens an entry by exact name, bypassing `FsPath` validation. Useful for archives that contain invalid names (absolute paths, `..` segments, control characters).

**Not supported:**

- Symbolic links (`NotSupportedException`).
- Moving or copying non-empty directories (`NotSupportedException`).

## Capability matrix

| Capability | Memory | Physical | ZIP (rw) | ZIP (ro) |
|---|---|---|---|---|
| `Read` | ✓ | ✓ | ✓ | ✓ |
| `Write` | ✓ | ✓ | ✓ | |
| `CreateDirectory` | ✓ | ✓ | ✓ | |
| `Delete` | ✓ | ✓ | ✓ | |
| `Move` | ✓ | ✓ | ✓ | |
| `Copy` | ✓ | ✓ | ✓ | |
| `Symlinks` | ✓ | ✓ | | |
| `PatchableContainer` | | | ✓ | |
| `Seekable` | ✓ | ✓ | ✓ | ✓ |

## Choosing a file system

- **Ephemeral, fast, no disk:** `MemoryFileSystem`.
- **Real files:** `PhysicalFileSystem`.
- **Archives, read-only distribution, mods:** `ZipArchiveFileSystem` with a read-only stream.
- **Bundling and later patching:** `ZipArchiveFileSystem` with a read-write stream, using write sessions.