using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.FileSystems.Physical;
using VinDust.SharpVfs.FileSystems.Zip;

namespace DeltaBuilder.Sample;

/// <summary>
/// Applies a diff to a v1 tree in place, using <see cref="IPatchableContainerFileSystem"/>
/// to patch archive entries without rewriting untouched archives.
/// </summary>
internal static class PatchApplier
{
    public static async Task<int> ApplyAsync(
        string v1Root,
        string v2Root,
        DiffResult diff,
        CancellationToken ct = default)
    {
        var applied = 0;

        foreach (var path in diff.Added)
        {
            ct.ThrowIfCancellationRequested();
            if (await TryAddAsync(v1Root, v2Root, path, ct))
            {
                applied++;
            }
        }

        foreach (var path in diff.Modified)
        {
            ct.ThrowIfCancellationRequested();

            // Skip the archive file itself: its content is reconstructed by patching
            // its inner entries. Copying the whole archive here would defeat the
            // purpose of IPatchableContainerFileSystem.
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (await TryModifyAsync(v1Root, v2Root, path, ct))
            {
                applied++;
            }
        }

        foreach (var path in diff.Removed)
        {
            ct.ThrowIfCancellationRequested();
            if (TryRemove(v1Root, path))
            {
                applied++;
            }
        }

        return applied;
    }

    // ----- Path classification -------------------------------------------------------------

    private static ArchiveSplit? SplitArchivePath(string vfsPath, string schemePrefix)
    {
        // vfsPath looks like: file:///mods/mod.zip/config.json
        if (!vfsPath.StartsWith(schemePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var afterScheme = vfsPath[schemePrefix.Length..];   // "/mods/mod.zip/config.json"
        var parts = afterScheme.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var archiveRelative = string.Join('/', parts[..(i + 1)]);
                var innerRelative = string.Join('/', parts[(i + 1)..]);
                return new ArchiveSplit(archiveRelative, innerRelative);
            }
        }

        return null;
    }

    private sealed record ArchiveSplit(string ArchiveRelative, string InnerRelative);

    // ----- Add -----------------------------------------------------------------------------

    private static async Task<bool> TryAddAsync(string v1Root, string v2Root, string path, CancellationToken ct)
    {
        const string schemePrefix = "file:///";

        if (!path.StartsWith(schemePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (SplitArchivePath(path, schemePrefix) is { } split)
        {
            if (split.InnerRelative.Length == 0)
            {
                // Adding a whole archive: copy the raw bytes.
                return CopyRawFile(v1Root, v2Root, split.ArchiveRelative);
            }

            return await AddInsideArchiveAsync(v1Root, v2Root, split, ct);
        }

        return CopyRawFile(v1Root, v2Root, path[schemePrefix.Length..]);
    }

    private static bool CopyRawFile(string v1Root, string v2Root, string relativePath)
    {
        var src = Path.Combine(v2Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var dst = Path.Combine(v1Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(src))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Copy(src, dst, overwrite: true);
        return true;
    }

    private static async Task<bool> AddInsideArchiveAsync(
    string v1Root,
    string v2Root,
    ArchiveSplit split,
    CancellationToken ct)
    {
        var archiveOnDiskV1 = Path.Combine(v1Root, split.ArchiveRelative.Replace('/', Path.DirectorySeparatorChar));
        var archiveOnDiskV2 = Path.Combine(v2Root, split.ArchiveRelative.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(archiveOnDiskV1) || !File.Exists(archiveOnDiskV2))
        {
            return false;
        }

        // Read content from v2's archive.
        byte[] content;
        await using (var v2Stream = new FileStream(archiveOnDiskV2, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var v2Zip = new ZipArchiveFileSystem(v2Stream, leaveOpen: true, ZipFileSystemOptions.Default))
        {
            await using var readStream = await v2Zip.OpenReadAsync(FsPath.Parse("/" + split.InnerRelative), ct);
            using var buffer = new MemoryStream();
            await readStream.CopyToAsync(buffer, ct);
            content = buffer.ToArray();
        }

        // Open v1's archive read-write and add/replace the entry.
        await using var v1Stream = new FileStream(archiveOnDiskV1, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await using var v1Zip = new ZipArchiveFileSystem(v1Stream, leaveOpen: true, ZipFileSystemOptions.Default);

        await using var writeStream = await v1Zip.OpenWriteAsync(
            FsPath.Parse("/" + split.InnerRelative),
            FileWriteMode.Create,
            ct);
        await writeStream.WriteAsync(content, ct);

        return true;
    }

    // ----- Modify --------------------------------------------------------------------------

    private static async Task<bool> ModifyInsideArchiveAsync(
        string v1Root,
        string v2Root,
        ArchiveSplit split,
        CancellationToken ct)
    {
        var archiveOnDiskV1 = Path.Combine(v1Root, split.ArchiveRelative.Replace('/', Path.DirectorySeparatorChar));
        var archiveOnDiskV2 = Path.Combine(v2Root, split.ArchiveRelative.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(archiveOnDiskV1) || !File.Exists(archiveOnDiskV2))
        {
            return false;
        }

        byte[] content;
        await using (var v2Stream = new FileStream(archiveOnDiskV2, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var v2Zip = new ZipArchiveFileSystem(v2Stream, leaveOpen: true, ZipFileSystemOptions.Default))
        {
            await using var readStream = await v2Zip.OpenReadAsync(FsPath.Parse("/" + split.InnerRelative), ct);
            using var buffer = new MemoryStream();
            await readStream.CopyToAsync(buffer, ct);
            content = buffer.ToArray();
        }

        await using var v1Stream = new FileStream(archiveOnDiskV1, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await using var v1Zip = new ZipArchiveFileSystem(v1Stream, leaveOpen: true, ZipFileSystemOptions.Default);
        var patchable = v1Zip;

        using var newContent = new MemoryStream(content);
        await patchable.ReplaceEntryAsync(
            FsPath.Parse("/" + split.InnerRelative),
            newContent,
            ContainerPatchOptions.Default,
            ct);

        return true;
    }

    private static async Task<bool> TryModifyAsync(string v1Root, string v2Root, string path, CancellationToken ct)
    {
        const string schemePrefix = "file:///";

        if (SplitArchivePath(path, schemePrefix) is { } split && split.InnerRelative.Length > 0)
        {
            return await ModifyInsideArchiveAsync(v1Root, v2Root, split, ct);
        }

        return CopyRawFile(v1Root, v2Root, path[schemePrefix.Length..]);
    }

    // ----- Remove --------------------------------------------------------------------------

    private static bool TryRemove(string v1Root, string path)
    {
        const string schemePrefix = "file:///";

        if (!path.StartsWith(schemePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (SplitArchivePath(path, schemePrefix) is not null)
        {
            // Entry-level removal inside a ZIP is not demonstrated here; skip.
            return false;
        }

        var relative = path[schemePrefix.Length..];
        var onDisk = Path.Combine(v1Root, relative.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(onDisk))
        {
            File.Delete(onDisk);
            return true;
        }

        if (Directory.Exists(onDisk))
        {
            Directory.Delete(onDisk, recursive: true);
            return true;
        }

        return false;
    }
}