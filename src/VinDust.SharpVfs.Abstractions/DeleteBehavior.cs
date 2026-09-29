namespace VinDust.SharpVfs.Abstractions;

/// <summary>Specifies the behavior when deleting a path that does not exist.</summary>
public enum DeleteBehavior
{
    /// <summary>The operation succeeds silently if the path does not exist.</summary>
    MissingOk,

    /// <summary>The operation throws if the path does not exist.</summary>
    Throw,
}