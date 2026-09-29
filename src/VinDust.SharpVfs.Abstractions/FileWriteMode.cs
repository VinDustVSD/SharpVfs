namespace VinDust.SharpVfs.Abstractions;

/// <summary>Specifies how a file system should open a file for writing.</summary>
public enum FileWriteMode
{
    /// <summary>Creates a new file or truncates an existing one.</summary>
    Create,

    /// <summary>Creates a new file; throws if the file already exists.</summary>
    CreateExclusive,

    /// <summary>
    /// Opens an existing file for appending, or creates it if missing. The stream is
    /// positioned at the end.
    /// </summary>
    Append,
}
