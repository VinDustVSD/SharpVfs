using FluentAssertions;
using System.Text;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Exceptions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.FileSystems.Physical;

namespace VinDust.SharpVfs.FileSystems.Tests;

public sealed class PhysicalFileSystemTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly PhysicalFileSystem _fs;

    public PhysicalFileSystemTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "vfs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _fs = new PhysicalFileSystem(_tempRoot);
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

    private static FsPath P(string s) => FsPath.Parse(s);

    [Fact]
    public void Constructor_RelativePath_Throws()
    {
        var act = () => new PhysicalFileSystem("relative/path");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_MissingRoot_Throws()
    {
        var missing = Path.Combine(_tempRoot, "missing");

        var act = () => new PhysicalFileSystem(missing);

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Constructor_MissingRoot_CreatesWhenConfigured()
    {
        var missing = Path.Combine(_tempRoot, "auto");

        _ = new PhysicalFileSystem(missing, new PhysicalFileSystemOptions { CreateRootIfMissing = true });

        Directory.Exists(missing).Should().BeTrue();
    }

    [Fact]
    public async Task Exists_Root_ReturnsTrue()
    {
        (await _fs.ExistsAsync(P("/"))).Should().BeTrue();
    }

    [Fact]
    public async Task Exists_Missing_ReturnsFalse()
    {
        (await _fs.ExistsAsync(P("/nope"))).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAndRead_RoundTrips()
    {
        await using (var w = await _fs.OpenWriteAsync(P("/file.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("hello"));
        }

        await using var r = await _fs.OpenReadAsync(P("/file.txt"));
        using var reader = new StreamReader(r);

        (await reader.ReadToEndAsync()).Should().Be("hello");
    }

    [Fact]
    public async Task Write_CreateParentOnWrite_CreatesAncestors()
    {
        var fs = new PhysicalFileSystem(_tempRoot, new PhysicalFileSystemOptions
        {
            CreateParentOnWrite = true,
        });

        await using (var w = await fs.OpenWriteAsync(P("/a/b/c/file.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("x"));
        }

        (await fs.ExistsAsync(P("/a/b/c/file.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task Write_MissingParent_NoCreate_Throws()
    {
        var act = async () => await _fs.OpenWriteAsync(P("/missing/file.txt"));

        await act.Should().ThrowAsync<VfsNotFoundException>();
    }

    [Fact]
    public async Task GetEntry_File_HasCorrectMetadata()
    {
        await using (var w = await _fs.OpenWriteAsync(P("/data.bin")))
        {
            await w.WriteAsync(new byte[123]);
        }

        var entry = await _fs.GetEntryAsync(P("/data.bin"));

        entry.Should().BeOfType<FileEntry>();
        entry!.Size.Should().Be(123);
        entry.LastModified.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task GetEntry_Directory_IsDirectoryEntry()
    {
        await _fs.CreateDirectoryAsync(P("/dir"));

        var entry = await _fs.GetEntryAsync(P("/dir"));

        entry.Should().BeOfType<DirectoryEntry>();
        entry!.Attributes.HasFlag(FileAttributes.Directory).Should().BeTrue();
    }

    [Fact]
    public async Task Enumerate_ReturnsChildren()
    {
        await _fs.CreateDirectoryAsync(P("/dir"));
        await using (var w = await _fs.OpenWriteAsync(P("/dir/a.txt"))) { }
        await using (var w = await _fs.OpenWriteAsync(P("/dir/b.txt"))) { }

        var names = new System.Collections.Generic.List<string>();
        await foreach (var entry in _fs.EnumerateAsync(P("/dir")))
        {
            names.Add(entry.Path.GetFileName()!);
        }

        names.Should().BeEquivalentTo(["a.txt", "b.txt"]);
    }

    [Fact]
    public async Task Enumerate_NotDirectory_Throws()
    {
        await using (var w = await _fs.OpenWriteAsync(P("/f.txt"))) { }

        var act = async () =>
        {
            await foreach (var _ in _fs.EnumerateAsync(P("/f.txt"))) { }
        };

        await act.Should().ThrowAsync<VfsNotDirectoryException>();
    }

    [Fact]
    public async Task Delete_File_Works()
    {
        await using (var w = await _fs.OpenWriteAsync(P("/f.txt"))) { }

        await _fs.DeleteAsync(P("/f.txt"));

        (await _fs.ExistsAsync(P("/f.txt"))).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_NonEmptyDirectory_RequiresRecursive()
    {
        await _fs.CreateDirectoryAsync(P("/dir"));
        await using (var w = await _fs.OpenWriteAsync(P("/dir/a.txt"))) { }

        var act = async () => await _fs.DeleteAsync(P("/dir"));
        await act.Should().ThrowAsync<VfsNotDirectoryException>();

        await _fs.DeleteAsync(P("/dir"), recursive: true);
        (await _fs.ExistsAsync(P("/dir"))).Should().BeFalse();
    }

    [Fact]
    public async Task Move_File_Works()
    {
        await using (var w = await _fs.OpenWriteAsync(P("/a.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("x"));
        }

        await _fs.MoveAsync(P("/a.txt"), P("/b.txt"));

        (await _fs.ExistsAsync(P("/a.txt"))).Should().BeFalse();
        (await _fs.ExistsAsync(P("/b.txt"))).Should().BeTrue();
    }

    [Fact]
    public async Task Copy_Directory_Recursive()
    {
        await _fs.CreateDirectoryAsync(P("/src"));
        await _fs.CreateDirectoryAsync(P("/src/sub"));
        await using (var w = await _fs.OpenWriteAsync(P("/src/sub/file.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("y"));
        }

        await _fs.CopyAsync(P("/src"), P("/dst"));

        (await _fs.ExistsAsync(P("/dst/sub/file.txt"))).Should().BeTrue();
        await using var r = await _fs.OpenReadAsync(P("/dst/sub/file.txt"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("y");
    }

    [Fact]
    public async Task Capabilities_IncludeSymlinks()
    {
        _fs.Capabilities.Should().HaveFlag(FileSystemCapabilities.Symlinks);
        _fs.Capabilities.Should().HaveFlag(FileSystemCapabilities.Seekable);
    }

    [Fact]
    public async Task OpenRead_Seekable()
    {
        await using (var w = await _fs.OpenWriteAsync(P("/f")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("abcdef"));
        }

        await using var r = await _fs.OpenReadAsync(P("/f"));
        r.Seek(3, SeekOrigin.Begin);
        var buffer = new byte[3];
        var read = await r.ReadAsync(buffer);
        Encoding.UTF8.GetString(buffer, 0, read).Should().Be("def");
    }

    [Fact]
    public async Task OpenWrite_CreateNew_ThrowsIfExists()
    {
        await using (var w = await _fs.OpenWriteAsync(P("/f"))) { }

        var act = async () => await _fs.OpenWriteAsync(P("/f"), FileWriteMode.CreateExclusive);

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task OpenWrite_Append_AppendsToEnd()
    {
        await using (var w = await _fs.OpenWriteAsync(P("/f")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("a"));
        }

        await using (var w = await _fs.OpenWriteAsync(P("/f"), FileWriteMode.Append))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("b"));
        }

        await using var r = await _fs.OpenReadAsync(P("/f"));
        using var reader = new StreamReader(r);
        (await reader.ReadToEndAsync()).Should().Be("ab");
    }

    [Fact]
    public async Task DisposeAsync_ThenUse_Throws()
    {
        await _fs.DisposeAsync();

        var act = async () => await _fs.ExistsAsync(P("/"));
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    // Symlink tests: skip when the environment does not permit creating symlinks
    // (Windows without developer mode / admin rights).
    [Fact]
    public async Task Symlink_CreateAndReport()
    {
        await using (var w = await _fs.OpenWriteAsync(P("/target.txt")))
        {
            await w.WriteAsync(Encoding.UTF8.GetBytes("t"));
        }

        try
        {
            await _fs.CreateSymbolicLinkAsync(P("/link.txt"), RelativePath.Parse("target.txt"));
        }
        catch (UnauthorizedAccessException)
        {
            return; // Symlink creation requires elevated privileges on this system.
        }
        catch (IOException)
        {
            return;
        }

        var entry = await _fs.GetEntryAsync(P("/link.txt"));

        entry.Should().BeOfType<SymbolicLink>();
        ((SymbolicLink)entry!).Target.ToString().Should().Be("target.txt");
    }
}