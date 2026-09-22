using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core;
using VinDust.SharpVfs.FileSystems.Memory;
using VinDust.SharpVfs.FileSystems.Physical;
using Xunit;

namespace VinDust.SharpVfs.Integration.Tests;

/// <summary>
/// End-to-end tests that exercise <see cref="VfsRoot"/> with a real disk-backed
/// <see cref="PhysicalFileSystem"/>, including mounts into its tree.
/// </summary>
public sealed class PhysicalRootIntegrationTests : IDisposable
{
    private readonly string _tempRoot;

    public PhysicalRootIntegrationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "vfs-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private VfsRoot CreateRoot(out PhysicalFileSystem physical)
    {
        physical = new PhysicalFileSystem(_tempRoot, new PhysicalFileSystemOptions
        {
            CreateParentOnWrite = true,
        });

        var registry = new DefaultSchemeRegistry();
        registry.Register(VfsScheme.Parse("file"), physical);
        return new VfsRoot(registry);
    }

    [Fact]
    public async Task WriteThenRead_RoundTrips_ToDisk()
    {
        var root = CreateRoot(out _);

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("file:///file.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("physical"));
        }

        // Verify the file actually exists on disk.
        File.Exists(Path.Combine(_tempRoot, "file.txt")).Should().BeTrue();

        await using var r = await root.OpenReadAsync(VfsUri.Parse("file:///file.txt"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("physical");
    }

    [Fact]
    public async Task GetEntry_ReportsMetadataFromDisk()
    {
        var root = CreateRoot(out _);
        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("file:///a/b/c.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("xyz"));
        }

        var entry = await root.GetEntryAsync(VfsUri.Parse("file:///a/b/c.txt"));

        entry.Should().NotBeNull();
        entry!.Size.Should().Be(3);
        entry.IsFile.Should().BeTrue();
    }

    [Fact]
    public async Task Enumerate_ListsDiskDirectory()
    {
        var root = CreateRoot(out _);
        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("file:///dir/a.txt"))) { }
        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("file:///dir/b.txt"))) { }

        var names = new System.Collections.Generic.List<string>();
        await foreach (var entry in root.EnumerateAsync(VfsUri.Parse("file:///dir")))
        {
            names.Add(entry.Path.GetFileName()!);
        }

        names.Should().BeEquivalentTo(["a.txt", "b.txt"]);
    }

    [Fact]
    public async Task Mount_MemoryIntoDisk()
    {
        var root = CreateRoot(out _);
        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("file:///data/base.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("base"));
        }

        var mem = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });
        await using (var w = await mem.OpenWriteAsync(FsPath.Parse("/from-mem.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("mem"));
        }

        await root.MountAsync(
            VfsUri.Parse("file:///data"),
            mem,
            new MountOptions { OverlapBehavior = MountOverlapBehavior.Replace });

        // Under Replace, base content beneath /data is hidden.
        (await root.ExistsAsync(VfsUri.Parse("file:///data/base.txt"))).Should().BeFalse();
        (await root.ExistsAsync(VfsUri.Parse("file:///data/from-mem.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task Mount_OverlayIntoDisk()
    {
        var root = CreateRoot(out _);
        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("file:///data/base.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("base"));
        }

        var mem = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });
        await using (var w = await mem.OpenWriteAsync(FsPath.Parse("/target.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("target"));
        }

        await root.MountAsync(
            VfsUri.Parse("file:///data"),
            mem,
            new MountOptions { OverlapBehavior = MountOverlapBehavior.Overlay });

        // Both visible under overlay.
        (await root.ExistsAsync(VfsUri.Parse("file:///data/base.txt"))).Should().BeTrue();
        (await root.ExistsAsync(VfsUri.Parse("file:///data/target.txt"))).Should().BeTrue();

        // Reads are routed correctly.
        await using var r = await root.OpenReadAsync(VfsUri.Parse("file:///data/base.txt"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("base");
    }

    [Fact]
    public async Task Unmount_ReturnsDiskContent()
    {
        var root = CreateRoot(out _);
        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("file:///data/base.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("base"));
        }

        var mem = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });

        await root.MountAsync(VfsUri.Parse("file:///data"), mem, new MountOptions());
        (await root.ExistsAsync(VfsUri.Parse("file:///data/base.txt"))).Should().BeFalse();

        await root.UnmountAsync(VfsUri.Parse("file:///data"));
        (await root.ExistsAsync(VfsUri.Parse("file:///data/base.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task ReadOnlyFileSystem_OverPhysical_BlocksWrites()
    {
        var physical = new PhysicalFileSystem(_tempRoot, new PhysicalFileSystemOptions
        {
            CreateParentOnWrite = true,
        });

        var readOnly = new Composition.ReadOnlyFileSystem(physical);
        var registry = new DefaultSchemeRegistry();
        registry.Register(VfsScheme.Parse("file"), readOnly);
        var root = new VfsRoot(registry);

        var act = async () => await root.OpenWriteAsync(VfsUri.Parse("file:///nope.txt"));

        await act.Should().ThrowAsync<Abstractions.Exceptions.VfsReadOnlyException>();
    }
}