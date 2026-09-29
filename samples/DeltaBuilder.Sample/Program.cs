using System.Text;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Paths;
using VinDust.SharpVfs.Core;
using VinDust.SharpVfs.FileSystems.Physical;

namespace DeltaBuilder.Sample;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "deltabuilder-sample-" + Guid.NewGuid().ToString("N"));
        var v1Root = Path.Combine(workDir, "v1");
        var v2Root = Path.Combine(workDir, "v2");

        try
        {
            Console.WriteLine($"Working directory: {workDir}");
            Console.WriteLine();

            await SampleTrees.CreateAsync(v1Root, v2Root);

            Console.WriteLine("=== Manifest v1 ===");
            var manifestV1 = await BuildManifestForAsync(v1Root);
            PrintManifest(manifestV1);

            Console.WriteLine();
            Console.WriteLine("=== Manifest v2 ===");
            var manifestV2 = await BuildManifestForAsync(v2Root);
            PrintManifest(manifestV2);

            var diff = DiffResult.Compute(manifestV1, manifestV2);

            Console.WriteLine();
            Console.WriteLine("=== Diff ===");
            PrintDiff(diff);

            Console.WriteLine();
            Console.WriteLine("=== Applying patch to v1 ===");
            var applied = await PatchApplier.ApplyAsync(v1Root, v2Root, diff);
            Console.WriteLine($"Applied {applied} operation(s).");

            Console.WriteLine();
            Console.WriteLine("=== Manifest v1 after patch ===");
            var manifestV1After = await BuildManifestForAsync(v1Root);
            PrintManifest(manifestV1After);

            var consistent = ManifestEquals(manifestV2, manifestV1After);
            Console.WriteLine();
            Console.WriteLine(consistent
                ? "OK: v1 after patch matches v2."
                : "MISMATCH: v1 after patch differs from v2.");

            return consistent ? 0 : 1;
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static async Task<Dictionary<string, ManifestEntry>> BuildManifestForAsync(string physicalRoot)
    {
        var physical = new PhysicalFileSystem(physicalRoot, new PhysicalFileSystemOptions
        {
            CreateParentOnWrite = true,
        });

        var registry = new DefaultSchemeRegistry();
        registry.Register(VfsScheme.Parse("file"), physical);

        await using var root = new VfsRoot(registry);
        return await ManifestBuilder.BuildAsync(root, VfsUri.Parse("file:///"));
    }

    private static void PrintManifest(Dictionary<string, ManifestEntry> manifest)
    {
        foreach (var (path, entry) in manifest.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (entry.IsDirectory)
            {
                Console.WriteLine($"  [dir]  {path}");
            }
            else
            {
                var shortHash = entry.Hash is null ? "-" : entry.Hash[..Math.Min(entry.Hash.Length, 19)];
                Console.WriteLine($"  [file] {path}  size={entry.Size,6}  {shortHash}");
            }
        }
    }

    private static void PrintDiff(DiffResult diff)
    {
        Console.WriteLine($"  added:    {diff.Added.Count}");
        foreach (var path in diff.Added)
        {
            Console.WriteLine($"    + {path}");
        }

        Console.WriteLine($"  removed:  {diff.Removed.Count}");
        foreach (var path in diff.Removed)
        {
            Console.WriteLine($"    - {path}");
        }

        Console.WriteLine($"  modified: {diff.Modified.Count}");
        foreach (var path in diff.Modified)
        {
            Console.WriteLine($"    M {path}");
        }
    }

#pragma warning disable CA1859 // Use concrete types when possible for improved performance
    private static bool ManifestEquals(
        IReadOnlyDictionary<string, ManifestEntry> a,
        IReadOnlyDictionary<string, ManifestEntry> b)
    {
#pragma warning restore CA1859 // Use concrete types when possible for improved performance
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, valueA) in a)
        {
            if (!b.TryGetValue(key, out var valueB))
            {
                return false;
            }

            if (valueA.IsDirectory != valueB.IsDirectory)
            {
                return false;
            }

            if (!string.Equals(valueA.Hash, valueB.Hash, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}