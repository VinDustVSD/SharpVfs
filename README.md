# SharpVfs

A virtual file system library for .NET with first-class support for **writable union mounts**.

## Why another VFS

Three things differentiate SharpVfs from Zio, A·VFS, and TrimKit.VirtualFileSystem:

- **Writable union mounts.** Zio's `AggregateFileSystem` is read-only. SharpVfs routes writes through a union to a selected target using a pluggable `IWritePolicy`.
- **Three distinct path types.** `VfsUri` (identity), `FsPath` (within a single file system), `RelativePath` (navigation). Folding all three into one type creates half-valid states and subtle bugs; separating them makes the invalid unrepresentable.
- **Async from the first line.** Every public API is `ValueTask` / `IAsyncEnumerable` / `CancellationToken`, not retrofitted.

## Status

Pre-1.0. The core is stable; composition, hosting, and tooling are in active development. Breaking changes are expected until 1.0.

## Installation

```bash
dotnet add package VinDust.SharpVfs.Abstractions
dotnet add package VinDust.SharpVfs.Core
dotnet add package VinDust.SharpVfs.FileSystems.Memory
dotnet add package VinDust.SharpVfs.FileSystems.Physical
dotnet add package VinDust.SharpVfs.FileSystems.Zip
dotnet add package VinDust.SharpVfs.Composition
```

Target framework: `net10.0`.

## Quick start

```csharp
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core;
using VinDust.SharpVfs.FileSystems.Memory;

// Create a file system.
await using var memFs = new MemoryFileSystem(new MemoryFileSystemOptions
{
    CreateParentOnWrite = true,
});

// Register it under a scheme.
var registry = new DefaultSchemeRegistry();
registry.Register(VfsScheme.Parse("mem"), memFs);

// Open a root and use it.
await using var root = new VfsRoot(registry);

await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///hello.txt")))
{
    await w.WriteAsync("world"u8.ToArray());
}

await using var r = await root.OpenReadAsync(VfsUri.Parse("mem:///hello.txt"));
using var reader = new StreamReader(r);
Console.WriteLine(await reader.ReadToEndAsync());   // world
```

`VfsRoot` is the single user-facing entry point. Do not use `IFileSystem` directly except when writing tests for a specific implementation or building decorators.

## Mounting

Any file system can host mount points once wrapped in `MountedFileSystem`, or automatically when registered with a `VfsRoot` (via `DefaultMountDecoratorFactory`).

```csharp
var baseFs = new MemoryFileSystem();
var overlay = new MemoryFileSystem();

var registry = new DefaultSchemeRegistry();
registry.Register(VfsScheme.Parse("mem"), baseFs);
await using var root = new VfsRoot(registry);

// Mount overlay at /mods. Under Replace, content of baseFs under /mods is hidden.
await root.MountAsync(
    VfsUri.Parse("mem:///mods"),
    overlay,
    new MountOptions { OverlapBehavior = MountOverlapBehavior.Replace });

// Under Overlay, base content stays visible and target wins on conflicts.
await root.MountAsync(
    VfsUri.Parse("mem:///assets"),
    new MemoryFileSystem(),
    new MountOptions { OverlapBehavior = MountOverlapBehavior.Overlay });
```

Mount points are stored locally in each `MountedFileSystem`. Nested mounts resolve recursively: a mount inside a mount inside a mount is walked one level at a time. Cycles are detected and rejected.

## ZIP archives

```csharp
// Read-only archive from a file.
await using var zipFs = new ZipArchiveFileSystem("/path/to/mod.zip");

// Writable archive. The stream must be writable and seekable.
await using var stream = new FileStream("/path/to/mod.zip", FileMode.Open, FileAccess.ReadWrite);
await using var writableZip = new ZipArchiveFileSystem(stream, leaveOpen: true, ZipFileSystemOptions.Default);

// Patch a single entry without rewriting the whole archive (in v1, a full
// repack under the hood — in-place is a follow-up).
var patchable = (IPatchableContainerFileSystem)writableZip;
await using (var newContent = File.OpenRead("/path/to/new-hero.png"))
{
    await patchable.ReplaceEntryAsync(
        FsPath.Parse("/textures/hero.png"),
        newContent,
        ContainerPatchOptions.Default);
}

// Batch multiple mutations into a single repack.
await using (var session = patchable.BeginWriteSession())
{
    await using (var w = await writableZip.OpenWriteAsync(FsPath.Parse("/config.json")))
    {
        await w.WriteAsync(newConfigBytes);
    }

    await writableZip.DeleteAsync(FsPath.Parse("/obsolete.txt"));

    await session.CommitAsync();
}
```

## The three path types

| Type | Purpose | Example | Scheme | `..` |
|------|---------|---------|--------|------|
| `VfsUri` | Identifies a node in the whole tree | `mem:///mods/config.json` | required | rejected |
| `FsPath` | Identifies a path within a single file system | `/mods/config.json` | none | rejected |
| `RelativePath` | Navigation, not identity | `../assets/foo.png` | none | allowed |

`string` never appears in public API signatures. String overloads exist only as extension methods (`"mem:///a".ToVfsUri()`, `"/a".ToFsPath()`, `"../a".ToRelativePath()`).

## Traversal

`VfsWalker` recursively walks a tree and, optionally, descends into archives detected by extension:

```csharp
var walker = new VfsWalker(
    root,
    visitor,
    ExtensionArchiveDetector.Zip,
    new ZipArchiveOpener());

await walker.WalkAsync(VfsUri.Parse("file:///"));
```

## Documentation

- [Architecture](docs/architecture.md) — how the pieces fit together.
- [Design decisions](docs/design-decisions.md) — why the library is shaped this way.
- [File systems](docs/file-systems.md) — built-in implementations.
- [Composition](docs/composition.md) — union, read-only, and other decorators.
- [Coding conventions](docs/coding-conventions.md) — for contributors.
- [Delta builder guide](docs/delta-builder-guide.md) — building delta tools on top of SharpVfs.
