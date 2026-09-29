using FluentAssertions;
using System.Text;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core;
using VinDust.SharpVfs.FileSystems.Memory;
using Xunit;

namespace VinDust.SharpVfs.Integration.Tests;

/// <summary>
/// End-to-end tests that exercise <see cref="VfsRoot"/> with a real
/// <see cref="MemoryFileSystem"/> behind a scheme.
/// </summary>
public sealed class RootMemoryFileSystemTests
{
    private static VfsRoot CreateRoot(out MemoryFileSystem fs, MemoryFileSystemOptions? options = null)
    {
        fs = new MemoryFileSystem(options ?? MemoryFileSystemOptions.Default);
        var registry = new DefaultSchemeRegistry();
        registry.Register(VfsScheme.Parse("mem"), fs);
        return new VfsRoot(registry);
    }

    [Fact]
    public async Task WriteThenRead_RoundTrips_ThroughRoot()
    {
        var root = CreateRoot(out _);

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///file.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("hello"));
        }

        await using var r = await root.OpenReadAsync(VfsUri.Parse("mem:///file.txt"));
        using var reader = new StreamReader(r);

        (await reader.ReadToEndAsync()).Should().Be("hello");
    }

    [Fact]
    public async Task GetEntry_ReportsMetadata()
    {
        var root = CreateRoot(out _, new MemoryFileSystemOptions { CreateParentOnWrite = true });

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///a/b/c.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("xyz"));
        }

        var entry = await root.GetEntryAsync(VfsUri.Parse("mem:///a/b/c.txt"));

        entry.Should().NotBeNull();
        entry!.Uri.Should().Be(VfsUri.Parse("mem:///a/b/c.txt"));
        entry.Size.Should().Be(3);
        entry.IsFile.Should().BeTrue();
        entry.IsDirectory.Should().BeFalse();
    }

    [Fact]
    public async Task CreateParentOnWrite_CreatesAncestors()
    {
        var root = CreateRoot(out _, new MemoryFileSystemOptions { CreateParentOnWrite = true });

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///deeply/nested/path/file.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("x"));
        }

        (await root.ExistsAsync(VfsUri.Parse("mem:///deeply"))).Should().BeTrue();
        (await root.ExistsAsync(VfsUri.Parse("mem:///deeply/nested"))).Should().BeTrue();
        (await root.ExistsAsync(VfsUri.Parse("mem:///deeply/nested/path"))).Should().BeTrue();
        (await root.ExistsAsync(VfsUri.Parse("mem:///deeply/nested/path/file.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task CreateParentOnWrite_Disabled_MissingParent_Throws()
    {
        var root = CreateRoot(out _);

        var act = async () => await root.OpenWriteAsync(VfsUri.Parse("mem:///missing/file.txt"));

        await act.Should().ThrowAsync<Abstractions.Exceptions.VfsNotFoundException>();
    }

    [Fact]
    public async Task Enumerate_ReturnsChildUris()
    {
        var root = CreateRoot(out _);
        await root.CreateDirectoryAsync(VfsUri.Parse("mem:///dir"));

        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///dir/a.txt"))) { }
        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///dir/b.txt"))) { }

        var uris = new System.Collections.Generic.List<string>();
        await foreach (var entry in root.EnumerateAsync(VfsUri.Parse("mem:///dir")))
        {
            uris.Add(entry.Uri.ToString());
        }

        uris.Should().BeEquivalentTo(["mem:///dir/a.txt", "mem:///dir/b.txt"]);
    }

    [Fact]
    public async Task Delete_RemovesEntry()
    {
        var root = CreateRoot(out _);
        await using (var w = await root.OpenWriteAsync(VfsUri.Parse("mem:///f"))) { }

        await root.DeleteAsync(VfsUri.Parse("mem:///f"));

        (await root.ExistsAsync(VfsUri.Parse("mem:///f"))).Should().BeFalse();
    }

    [Fact]
    public async Task Write_NeverCreatedParentWithoutOption_Throws() { }
}