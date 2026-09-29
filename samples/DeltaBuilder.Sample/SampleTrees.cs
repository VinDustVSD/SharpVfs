using System.IO.Compression;
using System.Text;

namespace DeltaBuilder.Sample;

/// <summary>Generates the v1 and v2 sample trees used by <see cref="Program"/>.</summary>
internal static class SampleTrees
{
    public static async Task CreateAsync(string v1Root, string v2Root)
    {
        Directory.CreateDirectory(v1Root);
        Directory.CreateDirectory(v2Root);

        // Shared (unchanged) file.
        await File.WriteAllTextAsync(Path.Combine(v1Root, "readme.txt"), "readme v1\n");
        await File.WriteAllTextAsync(Path.Combine(v2Root, "readme.txt"), "readme v1\n");

        // Modified file at top level.
        await File.WriteAllTextAsync(Path.Combine(v1Root, "config.json"), "{\"version\":1}\n");
        await File.WriteAllTextAsync(Path.Combine(v2Root, "config.json"), "{\"version\":2}\n");

        // Added file only in v2.
        await File.WriteAllTextAsync(Path.Combine(v2Root, "changelog.txt"), "new in v2\n");

        // Removed file only in v1.
        await File.WriteAllTextAsync(Path.Combine(v1Root, "old-notes.md"), "to be removed\n");

        // Archives: v1 has mod.zip with two files; v2 modifies one and adds another.
        var v1Mods = Path.Combine(v1Root, "mods");
        var v2Mods = Path.Combine(v2Root, "mods");
        Directory.CreateDirectory(v1Mods);
        Directory.CreateDirectory(v2Mods);

        await CreateZipAsync(Path.Combine(v1Mods, "mod.zip"), new Dictionary<string, string>
        {
            ["config.json"] = "{\"enabled\":true}\n",
            ["textures/skin.png"] = "PNG-v1\n",
        });

        await CreateZipAsync(Path.Combine(v2Mods, "mod.zip"), new Dictionary<string, string>
        {
            ["config.json"] = "{\"enabled\":true,\"version\":2}\n",   // modified
            ["textures/skin.png"] = "PNG-v1\n",                       // unchanged
            ["textures/glow.png"] = "PNG-glow\n",                     // added
        });
    }

    private static async Task CreateZipAsync(string path, IReadOnlyDictionary<string, string> entries)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            await entryStream.WriteAsync(bytes);
        }
    }
}