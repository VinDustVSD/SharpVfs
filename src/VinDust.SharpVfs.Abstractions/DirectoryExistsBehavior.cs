namespace VinDust.SharpVfs.Abstractions;

/// <summary>Specifies the behavior when a directory already exists.</summary>
public enum DirectoryExistsBehavior
{
    /// <summary>The operation succeeds and the existing directory is used as-is.</summary>
    Ok,

    /// <summary>The operation throws.</summary>
    Throw,
}
