namespace DeltaBuilder.Sample;

/// <summary>The result of comparing two manifests.</summary>
/// <param name="Added">Paths present only in v2.</param>
/// <param name="Removed">Paths present only in v1.</param>
/// <param name="Modified">Paths present in both but with different content hashes.</param>
internal sealed record DiffResult(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Modified)
{
    public static DiffResult Compute(
        IReadOnlyDictionary<string, ManifestEntry> v1,
        IReadOnlyDictionary<string, ManifestEntry> v2)
    {
        var added = new List<string>();
        var removed = new List<string>();
        var modified = new List<string>();

        foreach (var (key, v2Entry) in v2)
        {
            if (!v1.TryGetValue(key, out var v1Entry))
            {
                added.Add(key);
                continue;
            }

            if (v1Entry.IsDirectory != v2Entry.IsDirectory)
            {
                // Type changed: treat as remove + add.
                removed.Add(key);
                added.Add(key);
                continue;
            }

            if (!v1Entry.IsDirectory
                && !string.Equals(v1Entry.Hash, v2Entry.Hash, StringComparison.Ordinal))
            {
                modified.Add(key);
            }
        }

        foreach (var key in v1.Keys)
        {
            if (!v2.ContainsKey(key))
            {
                removed.Add(key);
            }
        }

        added.Sort(StringComparer.Ordinal);
        removed.Sort(StringComparer.Ordinal);
        modified.Sort(StringComparer.Ordinal);

        return new DiffResult(added, removed, modified);
    }
}