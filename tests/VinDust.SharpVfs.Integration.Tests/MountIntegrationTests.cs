using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core;
using VinDust.SharpVfs.FileSystems.Memory;
using Xunit;

namespace VinDust.SharpVfs.Integration.Tests;

/// <summary>
/// Tests that mount points installed through <see cref="VfsRoot"/> correctly redirect
/// to their targets and interact with the base file system.
/// </summary>
public sealed class MountIntegrationTests
{
    private static VfsRoot CreateRootWithBase(
        out MemoryFileSystem baseFs,
        MemoryFileSystemOptions? options = null)
    {
        baseFs = new MemoryFileSystem(options ?? new MemoryFileSystemOptions { CreateParentOnWrite = true });
        var registry = new DefaultSchemeRegistry();
        registry.Register(VfsScheme.Parse("mem"), baseFs);
        return new VfsRoot(registry);
    }

    private static MemoryFileSystem CreateTarget(params (string Path, string Content)[] files)
    {
        var fs = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });
        foreach (var (path, content) in files)
        {
            using var w = fs.OpenWriteAsync(FsPath.Parse(path)).AsTask().GetAwaiter().GetResult();
            var bytes = Encoding.UTF8.GetBytes(content);
            w.Write(bytes, 0, bytes.Length);
        }

        return fs;
    }

    [Fact]
    public async Task Mount_RedirectsReadsToTarget()
    {
        var root = CreateRootWithBase(out _);
        var target = CreateTarget(("/file.txt", "target-content"));

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            new MountOptions { CreateIfMissing = true });

        await using var r = await root.OpenReadAsync(VfsUri.Parse("mem:///m/file.txt"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("target-content");
    }

    [Fact]
    public async Task Mount_ReplacesBaseContentUnderMountPoint()
    {
        var root = CreateRootWithBase(out var baseFs);

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///m/base.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("base"));
        }

        var target = CreateTarget(("/other.txt", "other"));

        await root.MountAsync(
            VfsUri.Parse("mem:///m"),
            target,
            new MountOptions { OverlapBehavior = MountOverlapBehavior.Replace });

        // Base content under mount point must be hidden.
        (await root.ExistsAsync(VfsUri.Parse("mem:///m/base.txt"))).Should().BeFalse();

        // Target content is visible.
        (await root.ExistsAsync(VfsUri.Parse("mem:///m/other.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task Unmount_ReturnsControlToBase()
    {
        var root = CreateRootWithBase(out _);
        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///m/base.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("base"));
        }

        var target = CreateTarget(("/file.txt", "target"));

        await root.MountAsync(VfsUri.Parse("mem:///m"), target, new MountOptions());
        (await root.ExistsAsync(VfsUri.Parse("mem:///m/base.txt"))).Should().BeFalse();

        await root.UnmountAsync(VfsUri.Parse("mem:///m"));
        (await root.ExistsAsync(VfsUri.Parse("mem:///m/base.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task NestedMounts_ResolveThroughChain()
    {
        var root = CreateRootWithBase(out _);

        var middle = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });
        var leaf = CreateTarget(("/deep/file.txt", "leaf-content"));

        // Mount leaf inside middle at /sub. Middle is mountable via default decorator.
        await root.MountAsync(VfsUri.Parse("mem:///outer"), middle, new MountOptions());
        await root.MountAsync(VfsUri.Parse("mem:///outer/sub"), leaf, new MountOptions());

        await using var r = await root.OpenReadAsync(VfsUri.Parse("mem:///outer/sub/deep/file.txt"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("leaf-content");
    }

    [Fact]
    public async Task ReadOnlyMount_BlocksWrites()
    {
        var root = CreateRootWithBase(out _);
        var target = CreateTarget(("/file.txt", "content"));

        await root.MountAsync(
            VfsUri.Parse("mem:///ro"),
            target,
            new MountOptions { ReadOnly = true });

        var act = async () => await root.OpenWriteAsync(VfsUri.Parse("mem:///ro/file.txt"));

        await act.Should().ThrowAsync<Abstractions.Exceptions.VfsReadOnlyException>();
    }

    [Fact]
    public async Task ReadOnlyMount_AllowsReads()
    {
        var root = CreateRootWithBase(out _);
        var target = CreateTarget(("/file.txt", "content"));

        await root.MountAsync(
            VfsUri.Parse("mem:///ro"),
            target,
            new MountOptions { ReadOnly = true });

        await using var r = await root.OpenReadAsync(VfsUri.Parse("mem:///ro/file.txt"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("content");
    }

    [Fact]
    public async Task Mount_Unmount_RefCountsTarget()
    {
        var root = CreateRootWithBase(out _);
        var target = CreateTarget(("/file.txt", "content"));

        await root.MountAsync(VfsUri.Parse("mem:///a"), target, new MountOptions());
        await root.MountAsync(VfsUri.Parse("mem:///b"), target, new MountOptions());

        // After unmounting one, target must still be readable at the other.
        await root.UnmountAsync(VfsUri.Parse("mem:///a"));
        await using var r = await root.OpenReadAsync(VfsUri.Parse("mem:///b/file.txt"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("content");
    }

    [Fact]
    public async Task GetMounts_ReflectsInstalledPoints()
    {
        var root = CreateRootWithBase(out _);
        var target = CreateTarget();

        await root.MountAsync(VfsUri.Parse("mem:///x"), target, new MountOptions());

        var mounts = await root.GetMountsAsync(VfsUri.Parse("mem:///"));

        mounts.Should().ContainSingle();
        mounts.Should().ContainSingle().Which.Path.Should().Be(FsPath.Parse("/x"));
    }
}