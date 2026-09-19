namespace VinDust.SharpVfs.Abstractions;

/// <summary>Provides data for the <c>MountTableChanged</c> event.</summary>
public sealed class MountTableChangedEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="MountTableChangedEventArgs"/> class.</summary>
    /// <param name="kind">The kind of change that occurred.</param>
    /// <param name="point">The mount point affected by the change.</param>
    /// <param name="previousPoint">For <see cref="MountChangeKind.Replaced"/>, the previous mount point; otherwise <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="point"/> is <see langword="null"/>.</exception>
    public MountTableChangedEventArgs(MountChangeKind kind, MountPoint point, MountPoint? previousPoint = null)
    {
        ArgumentNullException.ThrowIfNull(point);

        Kind = kind;
        Point = point;
        PreviousPoint = previousPoint;
    }

    /// <summary>Gets the kind of change that occurred.</summary>
    public MountChangeKind Kind { get; }

    /// <summary>Gets the mount point affected by the change.</summary>
    public MountPoint Point { get; }

    /// <summary>
    /// Gets the previous mount point when <see cref="Kind"/> is
    /// <see cref="MountChangeKind.Replaced"/>; otherwise <see langword="null"/>.
    /// </summary>
    public MountPoint? PreviousPoint { get; }
}