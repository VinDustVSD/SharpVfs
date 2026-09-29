using FluentAssertions;
using System.Text;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core;
using VinDust.SharpVfs.FileSystems.Memory;
using Xunit;

namespace VinDust.SharpVfs.Integration.Tests;

/// <summary>
/// Tests for <see cref="MountOverlapBehavior.Overlay"/>: reads fall through to the
/// base when the target does not provide an entry; writes go to the target.
/// </summary>
public sealed class OverlayMountTests
{
    private static VfsRoot CreateRootWithBase(out MemoryFileSystem baseFs)
    {
        baseFs = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });
        var registry = new DefaultSchemeRegistry();
        registry.Register(VfsScheme.Parse("mem"), baseFs);
        return new VfsRoot(registry);
    }

    private static async Task<MemoryFileSystem> CreateTargetAsync(params (string Path, string Content)[] files)
    {
        var fs = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });
        foreach (var (path, content) in files)
        {
            await using var w = await fs.OpenWriteAsync(FsPath.Parse(path));
            await w.WriteAsync(Encoding.UTF8.GetBytes(content));
        }

        return fs;
    }

    [Fact]
    public async Task Overlay_ReadsFallThroughToBase_WhenTargetMissing()
    {
        var root = CreateRootWithBase(out _);

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///m/base.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("base"));
        }

        var target = await CreateTargetAsync(("/target.txt", "target"));

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            new MountOptions { OverlapBehavior = MountOverlapBehavior.Overlay });

        // Target file: served from target.
        await using (var r = await root.OpenReadAsync(VfsUri.Parse("mem:///m/target.txt")))
        using (var reader = new StreamReader(r))
        {
            (await reader.ReadToEndAsync()).Should().Be("target");
        }

        // Base file: visible through the overlay.
        await using (var r = await root.OpenReadAsync(VfsUri.Parse("mem:///m/base.txt")))
        using (var reader = new StreamReader(r))
        {
            (await reader.ReadToEndAsync()).Should().Be("base");
        }
    }

    [Fact]
    public async Task Overlay_TargetWinsOnNameConflict()
    {
        var root = CreateRootWithBase(out _);

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///m/file.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("from-base"));
        }

        var target = await CreateTargetAsync(("/file.txt", "from-target"));

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            new MountOptions { OverlapBehavior = MountOverlapBehavior.Overlay });

        await using var r = await root.OpenReadAsync(VfsUri.Parse("mem:///m/file.txt"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("from-target");
    }

    [Fact]
    public async Task Overlay_Enumerate_ReturnsUnion()
    {
        var root = CreateRootWithBase(out _);

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///m/a.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("a"));
        }

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///m/conflict.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("base"));
        }

        var target = await CreateTargetAsync(
            ("/b.txt", "b"),
            ("/conflict.txt", "target"));

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            new MountOptions { OverlapBehavior = MountOverlapBehavior.Overlay });

        var names = new List<string>();
        await foreach (var entry in root.EnumerateAsync(VfsUri.Parse("mem:///m")))
        {
            names.Add(entry.Path.GetFileName()!);
        }

        names.Should().BeEquivalentTo(["a.txt", "b.txt", "conflict.txt"]);
    }

    [Fact]
    public async Task Overlay_WritesGoToTarget()
    {
        var root = CreateRootWithBase(out _);

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///m/file.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("base"));
        }

        var target = await CreateTargetAsync();

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            new MountOptions { OverlapBehavior = MountOverlapBehavior.Overlay });

        // Write to /m/new.txt — should land in target, not base.
        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///m/new.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("new"));
        }

        (await target.ExistsAsync(FsPath.Parse("/new.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task Overlay_Delete_RemovesFromLayerThatHasEntry()
    {
        var root = CreateRootWithBase(out _);

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///m/base-only.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("b"));
        }

        var target = await CreateTargetAsync(("/target-only.txt", "t"));

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            new MountOptions { OverlapBehavior = MountOverlapBehavior.Overlay });

        // Delete base-only entry: falls through to inner.
        await root.DeleteAsync(VfsUri.Parse("mem:///m/base-only.txt"));
        (await root.ExistsAsync(VfsUri.Parse("mem:///m/base-only.txt"))).Should().BeFalse();

        // Delete target-only entry: goes to target.
        await root.DeleteAsync(VfsUri.Parse("mem:///m/target-only.txt"));
        (await root.ExistsAsync(VfsUri.Parse("mem:///m/target-only.txt"))).Should().BeFalse();
    }

    [Fact]
    public async Task Overlay_NestedDirectory_UnionsRecursively()
    {
        var root = CreateRootWithBase(out _);

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///m/sub/base.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("b"));
        }

        var target = await CreateTargetAsync(("/sub/target.txt", "t"));

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            new MountOptions { OverlapBehavior = MountOverlapBehavior.Overlay });

        var names = new List<string>();
        await foreach (var entry in root.EnumerateAsync(VfsUri.Parse("mem:///m/sub")))
        {
            names.Add(entry.Path.GetFileName()!);
        }

        names.Should().BeEquivalentTo(["base.txt", "target.txt"]);
    }

    [Fact]
    public async Task Replace_HidesBaseContent_AsBefore()
    {
        var root = CreateRootWithBase(out _);

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///m/visible.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("x"));
        }

        var target = await CreateTargetAsync(("/other.txt", "y"));

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            new MountOptions { OverlapBehavior = MountOverlapBehavior.Replace });

        // Replace: base content under mount point is hidden.
        (await root.ExistsAsync(VfsUri.Parse("mem:///m/visible.txt"))).Should().BeFalse();
        (await root.ExistsAsync(VfsUri.Parse("mem:///m/other.txt"))).Should().BeTrue();
    }
}