# Building a delta tool on SharpVfs

This guide explains how to use SharpVfs to build a tool that compares two tree states and patches one of them. It is the reference for the DeltaForge project.

## The problem

Given two directories `v1` and `v2` (each possibly containing ZIP archives), compute the difference and apply it to `v1` so it matches `v2`, without rewriting unchanged archives in full.

The pieces SharpVfs provides:

- `PhysicalFileSystem` — access to the disk tree.
- `VfsRoot` — a single handle that lets you address files by `VfsUri`, including files inside archives.
- `VfsWalker` — recursive traversal with optional auto-descent into archives.
- `VfsRoot.GetResolutionAsync` — tells you which file system actually owns a URI, and which mount points were crossed to reach it.
- `IPatchableContainerFileSystem` — replace a single entry inside a container without rewriting unchanged entries.
- `IContainerWriteSession` — batch multiple container mutations into a single commit.

## Reading a tree

```csharp
var physical = new PhysicalFileSystem("/builds/v1", new PhysicalFileSystemOptions
{
    CreateParentOnWrite = true,
});
var registry = new DefaultSchemeRegistry();
registry.Register(VfsScheme.Parse("file"), physical);

await using var root = new VfsRoot(registry);
```

Any file that lives inside an archive has a URI like `file:///mods/mod.zip/config.json`. That URI is a valid identifier even though it spans two file systems; `VfsRoot` resolves it.

## Auto-mounting archives during traversal

`VfsWalker` traverses a tree. When it encounters a file that an `IArchiveDetector` flags as an archive, it opens it with an `IArchiveOpener` and descends into the resulting file system without permanently modifying mount state.

```csharp
public sealed class ZipArchiveOpener : IArchiveOpener
{
    public ValueTask<IFileSystem?> OpenAsync(VfsUri uri, Stream content, CancellationToken ct)
    {
        IFileSystem fs = new ZipArchiveFileSystem(content, leaveOpen: false, ZipFileSystemOptions.Default);
        return new(fs);
    }
}

var visitor = new MyManifestVisitor();
var walker = new VfsWalker(
    root,
    visitor,
    ExtensionArchiveDetector.Zip,
    new ZipArchiveOpener());

await walker.WalkAsync(VfsUri.Parse("file:///"));
```

The walker takes ownership of the stream it receives from `OpenReadAsync`; the opener either consumes it (returns `null`) or passes it to a file system which takes ownership (returns the file system). Either way, the walker disposes the result when it moves past the archive.

## Getting resolution details

Sometimes you need to know which file system owns a URI and what mount points were crossed. `GetResolutionAsync` gives you that.

```csharp
var resolution = await root.GetResolutionAsync(
    VfsUri.Parse("file:///mods/mod.zip/config.json"));

// resolution.FileSystem      → the ZipArchiveFileSystem
// resolution.Path            → /config.json
// resolution.MountChain      → [ /mods/mod.zip ]
// resolution.CrossedMounts   → true
```

Use this when you need to distinguish entries that live inside containers from entries on disk, without parsing URIs by hand.

## Building a manifest

A manifest is a flat list of `(uri, size, hash, isDirectory)` for every reachable entry. Use `VfsWalker` to compute it.

```csharp
internal sealed class ManifestVisitor : IVfsWalkerVisitor
{
    public Dictionary<string, ManifestEntry> Manifest { get; } = new(StringComparer.Ordinal);

    public ValueTask OnDirectoryAsync(VfsUri uri, CancellationToken ct)
    {
        Manifest[uri.ToString()] = new ManifestEntry(uri.ToString(), IsDirectory: true, Size: 0, Hash: null);
        return default;
    }

    public async ValueTask OnFileAsync(VfsUri uri, FileSystemEntry entry, CancellationToken ct)
    {
        var hash = entry.ContentHash;
        Manifest[uri.ToString()] = new ManifestEntry(uri.ToString(), IsDirectory: false, entry.Size, hash);
    }
}
```

## Computing a diff

A diff has three sets:

- **Added** — present in `v2`, absent in `v1`.
- **Removed** — present in `v1`, absent in `v2`.
- **Modified** — present in both, content hash differs.

Because `VfsWalker` includes inner-archive entries, an inner file's modification naturally appears as a separate diff entry. The outer archive file also shows up as modified (its bytes changed) — **skip the outer archive in the diff application step** if you want per-entry patching to be effective. Its content will be reconstructed by patching inner entries.

## Applying a patch

### Direct file changes

For entries that live directly on disk, use `File.Copy`, `File.Delete`, `File.Move` on the physical root, or — if you prefer to stay in VFS — resolve the URI with `GetResolutionAsync` and act on the returned `PhysicalFileSystem`.

### Entry-level archive changes

For entries that live inside an archive, use `IPatchableContainerFileSystem`.

```csharp
// Open the archive read-write.
await using var v1Stream = new FileStream(
    archiveOnDisk,
    FileMode.Open,
    FileAccess.ReadWrite,
    FileShare.None);

await using var v1Zip = new ZipArchiveFileSystem(
    v1Stream,
    leaveOpen: true,
    ZipFileSystemOptions.Default);

var patchable = (IPatchableContainerFileSystem)v1Zip;

// Modify: ReplaceEntryAsync for existing entries.
await using (var newContent = File.OpenRead("/path/to/new-content"))
{
    await patchable.ReplaceEntryAsync(
        FsPath.Parse("/config.json"),
        newContent,
        ContainerPatchOptions.Default);
}

// Add: OpenWriteAsync with FileWriteMode.Create.
await using (var w = await v1Zip.OpenWriteAsync(
    FsPath.Parse("/textures/glow.png"),
    FileWriteMode.Create))
{
    await w.WriteAsync(glowBytes);
}

// Remove.
await v1Zip.DeleteAsync(FsPath.Parse("/obsolete.txt"));
```

## Batching with write sessions

If you need to patch many entries in the same archive, use a write session. All mutations are accumulated in an overlay and applied in one repack on commit. Without a session, each mutation triggers a full repack.

```csharp
var patchable = (IPatchableContainerFileSystem)v1Zip;

await using var session = patchable.BeginWriteSession();

foreach (var (entryPath, newBytes) in pendingChanges)
{
    await using var w = await v1Zip.OpenWriteAsync(FsPath.Parse(entryPath), FileWriteMode.Create);
    await w.WriteAsync(newBytes);
}

foreach (var entryToDelete in pendingDeletes)
{
    await v1Zip.DeleteAsync(FsPath.Parse(entryToDelete));
}

await session.CommitAsync();   // single repack
```

Rules for sessions:

- One session per file system. Nested `BeginWriteSession` throws.
- Reads inside a session see the overlay. This is read-your-writes.
- All write streams must be closed before `CommitAsync`. Otherwise it throws.
- `DisposeAsync` without commit performs a rollback.
- `session.Rollback()` discards all changes.

## Full workflow

```csharp
// 1. Manifest v1.
var v1Manifest = await BuildManifestAsync("/builds/v1");

// 2. Manifest v2.
var v2Manifest = await BuildManifestAsync("/builds/v2");

// 3. Diff.
var diff = DiffResult.Compute(v1Manifest, v2Manifest);

// 4. Apply to v1.
foreach (var uri in diff.Removed)
{
    // Delete the entry. If it lives in an archive, open the archive and delete inside.
}

foreach (var uri in diff.Added)
{
    // Add the entry. If it lives in an archive, open the archive and add inside.
}

foreach (var uri in diff.Modified)
{
    // Skip outer archives: their content will be reconstructed by inner patches.
    if (uri.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

    // Replace the entry. If it lives in an archive, use ReplaceEntryAsync.
}

// 5. Verify by rebuilding the manifest and comparing to v2Manifest.
```

## Rules of thumb

1. **Use `GetResolutionAsync` to distinguish inner-archive entries from disk entries.** Do not parse URIs by hand.
2. **Skip outer archives in the modified set.** Patch inner entries; the outer bytes will follow.
3. **Use `ReplaceEntryAsync` for existing entries, `OpenWriteAsync(Create)` for new ones.** They are not interchangeable: `ReplaceEntryAsync` requires the entry to exist.
4. **Batch with sessions.** A patch operation that touches 50 entries in one archive should be one session, one commit, one repack.
5. **Keep the outer archive open for the duration of the session.** Opening and closing per entry triggers a repack per entry.

## What is not yet implemented

- **In-place ZIP patching.** v1 does a full repack even for a single `ReplaceEntryAsync`. The contract ("do not overwrite unchanged entry bytes when the format allows it") is satisfied for ZIP only in a follow-up. Currently the answer is "ZIP does not allow it via `System.IO.Compression`".
- **Symbolic link resolution inside archives or across mounts.** `SymbolicLink` entries are surfaced, but resolution does not follow them.
- **Cross-file-system move and copy.** Only same-file-system transfers are supported by decorators. Cross-layer copy via streams works in `UnionFileSystem`.
- **TAR and 7z.** Planned; the container contract is format-agnostic.