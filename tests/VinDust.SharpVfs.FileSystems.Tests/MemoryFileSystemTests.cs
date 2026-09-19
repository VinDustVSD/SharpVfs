using FluentAssertions;
using System.Text;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.FileSystems.Memory;

namespace VinDust.SharpVfs.FileSystems.Tests;

public sealed class MemoryFileSystemTests
{
    private static FsPath P(string s) => FsPath.Parse(s);

    [Fact]
    public async Task EmptyFileSystem_RootExists()
    {
        await using var fs = new MemoryFileSystem();

        (await fs.ExistsAsync(P("/"))).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAndRead_RoundTrips()
    {
        await using var fs = new MemoryFileSystem();

        await using (var w = await fs.OpenWriteAsync(P("/file.txt")))
        {
            var data = Encoding.UTF8.GetBytes("hello");
            await w.WriteAsync(data);
        }

        await using var r = await fs.OpenReadAsync(P("/file.txt"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("hello");
    }

    [Fact]
    public async Task Write_CreateTruncatesExisting()
    {
        await using var fs = new MemoryFileSystem();

        await using (var w = await fs.OpenWriteAsync(P("/f")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("original"));
        }

        await using (var w = await fs.OpenWriteAsync(P("/f")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("new"));
        }

        await using var r = await fs.OpenReadAsync(P("/f"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("new");
    }

    [Fact]
    public async Task Append_AddsToEnd()
    {
        await using var fs = new MemoryFileSystem();

        await using (var w = await fs.OpenWriteAsync(P("/f")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("a"));
        }

        await using (var w = await fs.OpenWriteAsync(P("/f"), FileWriteMode.Append))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("b"));
        }

        await using var r = await fs.OpenReadAsync(P("/f"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("ab");
    }

    [Fact]
    public async Task CreateNew_ThrowsIfExists()
    {
        await using var fs = new MemoryFileSystem();
        await using (var w = await fs.OpenWriteAsync(P("/f")))
        {
        }

        var act = async () => await fs.OpenWriteAsync(P("/f"), FileWriteMode.CreateExclusive);

        await act.Should().ThrowAsync<VfsNotDirectoryException>();
    }

    [Fact]
    public async Task GetEntry_FileEntry_HasSizeAndLastModified()
    {
        await using var fs = new MemoryFileSystem();
        await using (var w = await fs.OpenWriteAsync(P("/f")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("12345"));
        }

        var entry = await fs.GetEntryAsync(P("/f"));

        entry.Should().BeOfType<FileEntry>();
        entry!.Size.Should().Be(5);
        entry.LastModified.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    private static readonly string[] expectation = ["a", "b"];

    [Fact]
    public async Task CreateDirectory_ThenEnumerate()
    {
        await using var fs = new MemoryFileSystem();
        await fs.CreateDirectoryAsync(P("/dir"));
        await using (var w = await fs.OpenWriteAsync(P("/dir/a")))
        {
        }

        await using (var w = await fs.OpenWriteAsync(P("/dir/b")))
        {
        }

        var names = new System.Collections.Generic.List<string>();
        await foreach (var e in fs.EnumerateAsync(P("/dir")))
        {
            names.Add(e.Path.GetFileName()!);
        }

        names.Should().BeEquivalentTo(expectation);
    }

    [Fact]
    public async Task CreateDirectory_Duplicate_Ok()
    {
        await using var fs = new MemoryFileSystem();
        await fs.CreateDirectoryAsync(P("/d"));
        await fs.CreateDirectoryAsync(P("/d"));  // Ok by default
    }

    [Fact]
    public async Task CreateDirectory_Duplicate_Throw()
    {
        await using var fs = new MemoryFileSystem(new MemoryFileSystemOptions
        {
            DirectoryExistsBehavior = DirectoryExistsBehavior.Throw,
        });
        await fs.CreateDirectoryAsync(P("/d"));

        var act = async () => await fs.CreateDirectoryAsync(P("/d"));

        await act.Should().ThrowAsync<VfsNotDirectoryException>();
    }

    [Fact]
    public async Task Delete_File_Works()
    {
        await using var fs = new MemoryFileSystem();
        await using (var w = await fs.OpenWriteAsync(P("/f")))
        {
        }

        await fs.DeleteAsync(P("/f"));

        (await fs.ExistsAsync(P("/f"))).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_Missing_MissingOkByDefault()
    {
        await using var fs = new MemoryFileSystem();

        await fs.DeleteAsync(P("/missing"));
    }

    [Fact]
    public async Task Delete_Missing_Throw()
    {
        await using var fs = new MemoryFileSystem(new MemoryFileSystemOptions
        {
            DeleteBehavior = DeleteBehavior.Throw,
        });

        var act = async () => await fs.DeleteAsync(P("/missing"));

        await act.Should().ThrowAsync<VfsNotFoundException>();
    }

    [Fact]
    public async Task Delete_NonEmptyDirectory_RequiresRecursive()
    {
        await using var fs = new MemoryFileSystem();
        await fs.CreateDirectoryAsync(P("/dir"));
        await using (var w = await fs.OpenWriteAsync(P("/dir/f")))
        {
        }

        var act = async () => await fs.DeleteAsync(P("/dir"));
        await act.Should().ThrowAsync<VfsNotDirectoryException>();

        await fs.DeleteAsync(P("/dir"), recursive: true);
        (await fs.ExistsAsync(P("/dir"))).Should().BeFalse();
    }

    [Fact]
    public async Task Move_RenamesFile()
    {
        await using var fs = new MemoryFileSystem();
        await using (var w = await fs.OpenWriteAsync(P("/a")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("x"));
        }

        await fs.MoveAsync(P("/a"), P("/b"));

        (await fs.ExistsAsync(P("/a"))).Should().BeFalse();
        await using var r = await fs.OpenReadAsync(P("/b"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("x");
    }

    [Fact]
    public async Task Move_DirectoryIntoOwnSubtree_Throws()
    {
        await using var fs = new MemoryFileSystem();
        await fs.CreateDirectoryAsync(P("/a"));
        await fs.CreateDirectoryAsync(P("/a/b"));

        var act = async () => await fs.MoveAsync(P("/a"), P("/a/b/c"));

        await act.Should().ThrowAsync<VfsNotDirectoryException>();
    }

    [Fact]
    public async Task Copy_File_ClonesContent()
    {
        await using var fs = new MemoryFileSystem();
        await using (var w = await fs.OpenWriteAsync(P("/a")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("abc"));
        }

        await fs.CopyAsync(P("/a"), P("/b"));

        await using var r = await fs.OpenReadAsync(P("/b"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("abc");
        (await fs.ExistsAsync(P("/a"))).Should().BeTrue();
    }

    [Fact]
    public async Task Copy_Directory_Recursive()
    {
        await using var fs = new MemoryFileSystem();
        await fs.CreateDirectoryAsync(P("/src"));
        await using (var w = await fs.OpenWriteAsync(P("/src/a")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("x"));
        }

        await fs.CopyAsync(P("/src"), P("/dst"));

        (await fs.ExistsAsync(P("/dst/a"))).Should().BeTrue();
    }

    [Fact]
    public async Task CreateSymbolicLink_Works()
    {
        await using var fs = new MemoryFileSystem();
        await fs.CreateSymbolicLinkAsync(P("/link"), RelativePath.Parse("target"));

        var entry = await fs.GetEntryAsync(P("/link"));

        entry.Should().BeOfType<SymbolicLink>();
        ((SymbolicLink)entry!).Target.ToString().Should().Be("target");
    }

    [Fact]
    public async Task OpenRead_Missing_Throws()
    {
        await using var fs = new MemoryFileSystem();

        var act = async () => await fs.OpenReadAsync(P("/missing"));

        await act.Should().ThrowAsync<VfsNotFoundException>();
    }

    [Fact]
    public async Task OpenRead_Directory_Throws()
    {
        await using var fs = new MemoryFileSystem();
        await fs.CreateDirectoryAsync(P("/dir"));

        var act = async () => await fs.OpenReadAsync(P("/dir"));

        await act.Should().ThrowAsync<VfsNotDirectoryException>();
    }

    [Fact]
    public async Task Exists_DirectoryRoot_ReturnsTrue()
    {
        await using var fs = new MemoryFileSystem();

        (await fs.ExistsAsync(P("/"))).Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_ThenUse_Throws()
    {
        var fs = new MemoryFileSystem();
        await fs.DisposeAsync();

        var act = async () => await fs.ExistsAsync(P("/"));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Capabilities_IncludeExpectedFlags()
    {
        await using var fs = new MemoryFileSystem();

        fs.Capabilities.Should().HaveFlag(FileSystemCapabilities.Read);
        fs.Capabilities.Should().HaveFlag(FileSystemCapabilities.Write);
        fs.Capabilities.Should().HaveFlag(FileSystemCapabilities.Symlinks);
        fs.Capabilities.Should().HaveFlag(FileSystemCapabilities.Seekable);
    }

    [Fact]
    public async Task ReadStream_IsSeekable()
    {
        await using var fs = new MemoryFileSystem();
        await using (var w = await fs.OpenWriteAsync(P("/f")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("abcdef"));
        }

        await using var r = await fs.OpenReadAsync(P("/f"));
        r.Seek(3, SeekOrigin.Begin);
        var buffer = new byte[3];
        var read = await r.ReadAsync(buffer.AsMemory());
        read.Should().Be(3);
        Encoding.UTF8.GetString(buffer).Should().Be("def");
    }
}