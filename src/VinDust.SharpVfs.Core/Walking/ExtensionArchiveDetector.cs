using System;
using System.Collections.Generic;
using VinDust.SharpVfs.Abstractions;
using VinDust.SharpVfs.Abstractions.Entries;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core.Walking;

/// <summary>
/// An <see cref="IArchiveDetector"/> that matches against a configured set of file
/// extensions. Extensions are compared case-insensitively.
/// </summary>
public sealed class ExtensionArchiveDetector : IArchiveDetector
{
    private readonly HashSet<string> _extensions;

    /// <summary>Initializes a new instance of the <see cref="ExtensionArchiveDetector"/> class.</summary>
    /// <param name="extensions">Extensions including the leading dot, e.g. <c>".zip"</c>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="extensions"/> is <see langword="null"/>.</exception>
    public ExtensionArchiveDetector(IEnumerable<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        _extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in extensions)
        {
            _extensions.Add(ext.StartsWith('.') ? ext : "." + ext);
        }
    }

    /// <summary>Gets a detector that matches <c>.zip</c> files.</summary>
    public static ExtensionArchiveDetector Zip { get; } = new([".zip"]);

    /// <inheritdoc />
    public bool IsArchive(VfsUri uri, FileSystemEntry entry)
    {
        var name = uri.Path.GetFileName();
        if (name is null)
        {
            return false;
        }

        foreach (var ext in _extensions)
        {
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}