using System.Security.Cryptography;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core;
using VinDust.SharpVfs.FileSystems.Zip;

namespace DeltaBuilder.Sample;

/// <summary>
/// Walks a VFS tree and produces a manifest, automatically mounting ZIP archives
/// as it encounters them.
/// </summary>
internal static class ManifestBuilder
{
    public static async Task<Dictionary<string, ManifestEntry>> BuildAsync(
        VfsRoot root,
        VfsUri rootUri,
        CancellationToken ct = default)
    {
        var manifest = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
        await WalkAsync(root, rootUri, manifest, ct);
        return manifest;
    }

    private static async Task WalkAsync(
        VfsRoot root,
        VfsUri directory,
        Dictionary<string, ManifestEntry> manifest,
        CancellationToken ct)
    {
        manifest[directory.ToString()] = new ManifestEntry(
            directory.ToString(),
            IsDirectory: true,
            Size: 0,
            Hash: null);

        var children = new List<VfsEntry>();
        await foreach (var entry in root.EnumerateAsync(directory, ct))
        {
            children.Add(entry);
        }

        foreach (var entry in children)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.IsDirectory)
            {
                await WalkAsync(root, entry.Uri, manifest, ct);
                continue;
            }

            if (!entry.IsFile)
            {
                continue;
            }

            var hash = await ComputeHashAsync(root, entry.Uri, ct);
            manifest[entry.Uri.ToString()] = new ManifestEntry(
                entry.Uri.ToString(),
                IsDirectory: false,
                Size: entry.Size,
                Hash: hash);

            if (IsZip(entry.Uri))
            {
                await MountArchiveAsync(root, entry, ct);
                await WalkAsync(root, entry.Uri, manifest, ct);
            }
        }
    }

    private static bool IsZip(VfsUri uri)
    {
        var name = uri.Path.GetFileName();
        return name is not null
            && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task MountArchiveAsync(VfsRoot root, VfsEntry archiveEntry, CancellationToken ct)
    {
        // Open through the VFS so the file is located correctly inside the physical tree.
        var stream = await root.OpenReadAsync(archiveEntry.Uri, ct);

        ZipArchiveFileSystem zipFs;
        try
        {
            zipFs = new ZipArchiveFileSystem(stream, leaveOpen: false, ZipFileSystemOptions.Default);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        await root.MountAsync(
            archiveEntry.Uri,
            zipFs,
            new MountOptions { OverlapBehavior = MountOverlapBehavior.Replace },
            ct);
    }

    private static async Task<string> ComputeHashAsync(VfsRoot root, VfsUri uri, CancellationToken ct)
    {
        await using var stream = await root.OpenReadAsync(uri, ct);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}