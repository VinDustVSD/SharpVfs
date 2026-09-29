namespace DeltaBuilder.Sample;

/// <summary>A single entry in a tree manifest.</summary>
/// <param name="Path">The absolute VFS URI of the entry.</param>
/// <param name="IsDirectory">Whether the entry is a directory.</param>
/// <param name="Size">The size in bytes, or zero for directories.</param>
/// <param name="Hash">The content hash, or <see langword="null"/> for directories.</param>
internal sealed record ManifestEntry(string Path, bool IsDirectory, long Size, string? Hash);