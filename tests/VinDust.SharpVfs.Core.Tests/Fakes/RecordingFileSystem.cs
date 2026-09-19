using System.Runtime.CompilerServices;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core.Tests.Fakes;

/// <summary>
/// An <see cref="IFileSystem"/> that records the paths of operations invoked on it.
/// Used to verify dispatch behavior without real I/O.
/// </summary>
internal sealed class RecordingFileSystem : IFileSystem
{
    private readonly string _name;
    private readonly HashSet<string> _existingEntries;

    public RecordingFileSystem(string name, params string[] existingPaths)
    {
        _name = name;
        _existingEntries = new HashSet<string>(existingPaths, StringComparer.Ordinal);
    }

    public string Name => _name;

    public List<(string Op, FsPath Path)> Calls { get; } = new();

    public FileSystemCapabilities Capabilities => FileSystemCapabilities.Read | FileSystemCapabilities.Write;

    public ValueTask<bool> ExistsAsync(FsPath path, CancellationToken ct = default)
    {
        Calls.Add(("Exists", path));
        return new(_existingEntries.Contains(path.ToString()));
    }

    public ValueTask<FileSystemEntry?> GetEntryAsync(FsPath path, CancellationToken ct = default)
    {
        Calls.Add(("GetEntry", path));
        if (!_existingEntries.Contains(path.ToString()))
        {
            return new((FileSystemEntry?)null);
        }

        return new(new FileEntry(path));
    }

    public async IAsyncEnumerable<FileSystemEntry> EnumerateAsync(
        FsPath path,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Calls.Add(("Enumerate", path));
        await Task.CompletedTask;
        yield break;
    }

    public ValueTask<Stream> OpenReadAsync(FsPath path, CancellationToken ct = default)
    {
        Calls.Add(("OpenRead", path));
        return new(new MemoryStream(Array.Empty<byte>()));
    }

    public ValueTask<Stream> OpenWriteAsync(FsPath path, FileWriteMode mode = FileWriteMode.Create, CancellationToken ct = default)
    {
        Calls.Add(("OpenWrite", path));
        return new(new MemoryStream());
    }

    public ValueTask CreateDirectoryAsync(FsPath path, CancellationToken ct = default)
    {
        Calls.Add(("CreateDirectory", path));
        return default;
    }

    public ValueTask DeleteAsync(FsPath path, bool recursive = false, CancellationToken ct = default)
    {
        Calls.Add(("Delete", path));
        return default;
    }

    public ValueTask MoveAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        Calls.Add(("Move", source));
        Calls.Add(("Move", destination));
        return default;
    }

    public ValueTask CopyAsync(FsPath source, FsPath destination, CancellationToken ct = default)
    {
        Calls.Add(("Copy", source));
        Calls.Add(("Copy", destination));
        return default;
    }

    public ValueTask CreateSymbolicLinkAsync(FsPath path, RelativePath target, CancellationToken ct = default)
    {
        Calls.Add(("CreateSymlink", path));
        return default;
    }

    public ValueTask DisposeAsync() => default;

    public override string ToString() => _name;
}