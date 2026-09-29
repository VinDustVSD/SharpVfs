# Design decisions

Numbered, with rationale. Use these to understand *why* the code is shaped this way before proposing changes.

## 1. Three path types

`VfsUri`, `FsPath`, `RelativePath` are distinct types. They have different semantics:

- `VfsUri` identifies a node in the whole tree. It has a scheme. `..` makes no sense.
- `FsPath` identifies a path within one file system. It has no scheme. `..` makes no sense.
- `RelativePath` is a navigation operation. `..` is the whole point.

Folding all three into one `Path` type (as most libraries do) means every API has to validate which variant it received and reject invalid combinations at runtime. Splitting them moves that validation to type boundaries: an API that expects `FsPath` cannot receive a `VfsUri`.

## 2. Paths store segments, not strings

`Parse` allocates one `string[]`. All subsequent operations (`GetParent`, `GetFileName`, `SubPath`, `StartsWith`, `CompareTo`) are O(depth) with no string concatenation. `ToString` reconstructs the canonical form on demand.

Cost: `VfsUri` and `FsPath` are larger than a string-wrapping struct (8 bytes for the array reference, plus the hash-code cache). Benefit: no accidental O(n) operations in the hot path.

## 3. Segment-wise ordering

`StartsWith` and `IComparable` compare segment by segment, not by canonical string. This avoids `"/foo"` matching `"/foobar"` and produces the intuitive ordering `"/a/b" < "/a-c"`.

## 4. Async everywhere

Every public method on `IFileSystem` is async, even for in-memory implementations. Synchronous signatures would force async callers into `.Result` or `.Wait()`. `IFileSystem` is `IAsyncDisposable`.

Sync-over-async helpers are planned for a separate `VinDust.SharpVfs.Sync` package, documented as blocking and deadlock-prone. Not in the core.

`ValueTask<T>` for single results, `IAsyncEnumerable<T>` for listings. Every method takes `CancellationToken ct = default`.

## 5. `MountedFileSystem` is a decorator, not a base class

The original plan had `MountableFileSystemBase`. It was dropped. Reasons:

- **Single inheritance.** In C#, a decorator like `ReadOnlyFileSystem` must inherit from its own decorator base. An `: MountableFileSystemBase` constraint would block composition.
- **Test isolation.** `MountTable` is testable on its own. A base class forces testing through a subclass.
- **Fragile hierarchy.** "I want mount + read-only" would need multiple inheritance (not available) or copy-paste.

The current model:

- `IMountableFileSystem` — interface, the contract.
- `MountTable` — the machinery, composable via field.
- `MountedFileSystem` — a decorator that wraps any `IFileSystem`.
- `DefaultMountDecoratorFactory` — auto-wraps non-mountable file systems when a scheme is registered or a mount is performed.

## 6. One-level dispatch, not jump-to-target

`MountedFileSystem` resolves through **one** mount point and forwards to the target. It does not jump to the deepest file system in a single call. This is what makes Overlay implementable in a decorator: the target gets a chance to look up its own state before falling back.

The trade-off: a chain of N mounts costs N virtual calls. For typical trees (N ≤ 3) this is negligible. For pathological cases (deeply nested archives), it is still O(N), not exponential.

## 7. `PathTrie` without a result cache

`PathTrie` (used by `MountTable`) is an immutable-snapshot RCU structure: readers take no lock, writers swap the root under a write lock. `TryResolve` is O(depth).

A result cache was considered and rejected. Invalidation on nested mount/unmount requires a generation chain; getting it wrong produces silent stale results. `TryResolve` is fast enough that a cache would save little.

## 8. `IResolveTracer` lives in Core

Not in `Diagnostics`. Resolution knows where to emit events; a separate package can't hook in without an interface defined in `Core`. `Diagnostics` will provide concrete tracers and decorators that consume the interface.

## 9. No `IFileSystemWatcher` in v1

A public API with an empty contract is worse than no API. Watcher support needs real prefix-based event propagation, which is a substantial subsystem. It will be designed and implemented deliberately in v1.1+.

## 10. No schemes-as-constructors

`zip://path/to/archive.zip` is not supported. A scheme maps to a file system instance or a parameterless factory. Constructors-from-path look convenient but introduce string parsing into the routing layer and complicate lifetime management.

## 11. No `appsettings.json` configuration

Configuration is code. The `VfsBuilder` (planned) provides a fluent API. Configuration files are a future add-on if there is demand.

## 12. `IPatchableContainerFileSystem` as a distinct contract

`IFileSystem.OpenWriteAsync` is not the right API for "replace one entry in a container without rewriting unchanged bytes". It has no way to express the byte-preservation guarantee. A separate interface with a single `ReplaceEntryAsync` method makes the intent explicit and lets implementations choose the cheapest strategy that satisfies the contract.

For batch operations, `BeginWriteSession` accumulates changes and applies them in a single repack. Without a session, each mutation triggers an immediate repack.

## 13. No whiteout support in `UnionFileSystem` (v1)

Deleting a file that exists only in a read-only lower layer is a no-op. There is no "hide this entry" mechanism. Implementing whiteouts requires per-layer tombstone tracking and a resolution rule that distinguishes "missing" from "deleted", which is out of scope for v1.

## 14. `ContentHash` is opaque

The type is `string?`. The format is `<algorithm>:<hex>` (e.g. `sha256:...`, `crc32:...`). Consumers must not parse the algorithm out of it without checking; they should use `IContentHashProvider` to choose the algorithm.

`FileSystemEntry.ContentHash` is populated eagerly by file systems that can do so cheaply (e.g. ZIP reads CRC32 from the central directory). Otherwise it is `null`, and `VfsRoot.GetContentHashAsync` falls back to a configured provider.

## 15. `NotSupportedException` for missing capabilities

When a file system doesn't implement an operation, it throws `NotSupportedException`, not a `VfsException` subclass. This is separate from "operation failed" (which throws `VfsException`) and matches BCL conventions. `FileSystemCapabilities` advertises up front which operations a file system supports.

## 16. `OperationCanceledException` is never wrapped

Cancellation is signaled via the standard .NET mechanism. Wrapping it in `VfsException` would force callers to unwrap before rethrowing, which is error-prone.