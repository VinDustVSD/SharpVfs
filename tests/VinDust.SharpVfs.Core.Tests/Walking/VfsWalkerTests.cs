using FluentAssertions;
using System.Text;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core.Walking;
using VinDust.SharpVfs.FileSystems.Memory;
using VinDust.SharpVfs.FileSystems.Zip;

namespace VinDust.SharpVfs.Core.Tests.Walking;

public sealed class VfsWalkerTests
{
    private static VfsRoot RootWith(MemoryFileSystem fs)
    {
        var registry = new DefaultSchemeRegistry();
        registry.Register(VfsScheme.Parse("mem"), fs);
        return new VfsRoot(registry);
    }

#pragma warning disable CA1859 // Use concrete types when possible for improved performance
    private static async Task WriteAsync(IFileSystem fs, string path, string content)
#pragma warning restore CA1859 // Use concrete types when possible for improved performance
    {
        await using var w = await fs.OpenWriteAsync(FsPath.Parse(path));
        await w.WriteAsync(Encoding.UTF8.GetBytes(content));
    }

    [Fact]
    public async Task Walk_NoArchives_VisitsAllEntries()
    {
        var fs = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });
        await WriteAsync(fs, "/a.txt", "1");
        await WriteAsync(fs, "/dir/b.txt", "2");
        await WriteAsync(fs, "/dir/sub/c.txt", "3");

        var root = RootWith(fs);
        var visitor = new RecordingVisitor();
        var walker = new VfsWalker(root, visitor);

        await walker.WalkAsync(VfsUri.Parse("mem:///"));

        visitor.Directories.Should().BeEquivalentTo(
            ["mem:///", "mem:///dir", "mem:///dir/sub"]);
        visitor.Files.Should().BeEquivalentTo(
            ["mem:///a.txt", "mem:///dir/b.txt", "mem:///dir/sub/c.txt"]);
    }

    [Fact]
    public async Task Walk_ArchiveDescended()
    {
        var fs = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });

        // Build a real ZIP in memory.
        var zipBytes = CreateZipStream(("inner.txt", "zip-content"));
        await using (var w = await fs.OpenWriteAsync(FsPath.Parse("/archive.zip")))
        {
            await w.WriteAsync(zipBytes);
        }

        await WriteAsync(fs, "/outer.txt", "outer");

        var root = RootWith(fs);
        var visitor = new RecordingVisitor();
        var walker = new VfsWalker(
            root,
            visitor,
            ExtensionArchiveDetector.Zip,
            new ZipOpener());

        await walker.WalkAsync(VfsUri.Parse("mem:///"));

        visitor.Files.Should().Contain("mem:///archive.zip");
        visitor.Files.Should().Contain("mem:///archive.zip/inner.txt");
        visitor.Files.Should().Contain("mem:///outer.txt");
    }

    [Fact]
    public async Task Walk_DetectorAndOpenerMismatch_Throws()
    {
        var fs = new MemoryFileSystem();
        var root = RootWith(fs);

        var act = () => new VfsWalker(root, new RecordingVisitor(), ExtensionArchiveDetector.Zip, archiveOpener: null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Walk_VisitorThrows_OnErrorCalled()
    {
        var fs = new MemoryFileSystem(new MemoryFileSystemOptions { CreateParentOnWrite = true });
        await WriteAsync(fs, "/a.txt", "1");

        var root = RootWith(fs);
        var visitor = new ThrowingVisitor();
        var walker = new VfsWalker(root, visitor);

        await walker.WalkAsync(VfsUri.Parse("mem:///"));

        visitor.ErrorCount.Should().BeGreaterThan(0);
    }

    private static byte[] CreateZipStream(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var e = archive.CreateEntry(name);
                using var s = e.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }
        }

        return ms.ToArray();
    }

    private sealed class RecordingVisitor : IVfsWalkerVisitor
    {
        public List<string> Directories { get; } = [];
        public List<string> Files { get; } = [];

        public ValueTask OnDirectoryAsync(VfsUri uri, CancellationToken ct)
        {
            Directories.Add(uri.ToString());
            return default;
        }

        public ValueTask OnFileAsync(VfsUri uri, FileSystemEntry entry, CancellationToken ct)
        {
            Files.Add(uri.ToString());
            return default;
        }
    }

    private sealed class ThrowingVisitor : IVfsWalkerVisitor
    {
        public int ErrorCount { get; private set; }

        public ValueTask OnDirectoryAsync(VfsUri uri, CancellationToken ct) => default;

        public ValueTask OnFileAsync(VfsUri uri, FileSystemEntry entry, CancellationToken ct)
            => throw new InvalidOperationException("boom");

        public ValueTask OnErrorAsync(VfsUri uri, Exception exception, CancellationToken ct)
        {
            ErrorCount++;
            return default;
        }
    }

    /// <summary>Minimal opener that constructs a real ZIP file system over the stream.</summary>
    private sealed class ZipOpener : IArchiveOpener
    {
        public ValueTask<IFileSystem?> OpenAsync(VfsUri uri, Stream content, CancellationToken ct)
        {
            IFileSystem fs = new ZipArchiveFileSystem(content, leaveOpen: false, ZipFileSystemOptions.Default);
            return new(fs);
        }
    }
}