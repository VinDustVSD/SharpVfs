namespace VinDust.SharpVfs.Abstractions.Entries;

/// <summary>
/// Marker interface for entry types provided by external libraries that cannot inherit
/// from <see cref="FileSystemEntry"/>.
/// </summary>
/// <remarks>
/// This is not the primary contract. Third-party file systems should prefer deriving
/// from <see cref="FileSystemEntry"/>; the marker exists only to allow interop with
/// types that are already defined elsewhere.
/// </remarks>
public interface IExternalEntry;
