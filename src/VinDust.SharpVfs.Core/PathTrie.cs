using System.Collections.Immutable;
using VinDust.SharpVfs.Abstractions.Paths;

namespace VinDust.SharpVfs.Core;

/// <summary>
/// A prefix tree mapping paths to values, with lock-free reads and snapshot-based writes.
/// </summary>
/// <typeparam name="TValue">The type of value stored at each trie node. Must be a reference type.</typeparam>
/// <remarks>
/// <para>
/// The trie stores a value at an arbitrary path prefix. Resolution finds the value
/// associated with the longest prefix of a query path, which is what mount-point lookup
/// requires: a mount at <c>/a/b</c> shadows a mount at <c>/a</c> for paths under <c>/a/b</c>.
/// </para>
/// <para>
/// Reads are lock-free. Writes take an internal lock and publish a new immutable snapshot;
/// in-flight readers continue to observe the previous snapshot without blocking. No result
/// cache is maintained: resolution is O(depth) and always reflects the current snapshot.
/// </para>
/// <para>
/// This type is not thread-safe with respect to concurrent writers in the sense that writes
/// are serialized, but reads are never blocked. This is the RCU-style (read-copy-update)
/// pattern used throughout the library.
/// </para>
/// </remarks>
public sealed class PathTrie<TValue>
    where TValue : class
{
    private readonly Lock _writeLock = new();
    private Node _root = Node.Empty;
    private int _count;
    private long _version;

    /// <summary>Initializes a new instance of the <see cref="PathTrie{TValue}"/> class.</summary>
    public PathTrie()
    {
    }

    /// <summary>Gets the number of values currently stored in the trie.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Gets a monotonically increasing version number incremented on every successful mutation.</summary>
    /// <remarks>
    /// The version is intended for diagnostics and for callers that need a cheap way to
    /// detect "has anything changed since I last looked?". It is not used internally.
    /// </remarks>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Attempts to find the value at the exact path specified.</summary>
    /// <param name="path">The path to look up.</param>
    /// <param name="value">When this method returns, contains the value, or <see langword="null"/> if no exact match exists.</param>
    /// <returns><see langword="true"/> if an exact match was found; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is the default value.</exception>
    public bool TryGet(FsPath path, out TValue? value)
    {
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        var node = Volatile.Read(ref _root);
        var segments = path.Segments;

        for (int i = 0; i < segments.Length; i++)
        {
            if (!node.Children.TryGetValue(segments[i], out var child))
            {
                value = null;
                return false;
            }

            node = child;
        }

        if (node.HasValue)
        {
            value = node.Value;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Finds the value at the longest prefix of <paramref name="path"/> that exists in the trie.</summary>
    /// <param name="path">The path to resolve.</param>
    /// <param name="value">When this method returns, contains the value, or <see langword="null"/> if no prefix matched.</param>
    /// <param name="remaining">
    /// When this method returns, contains the remainder of <paramref name="path"/> after the
    /// matched prefix. <see cref="FsPath.Root"/> when the match is exact.
    /// </param>
    /// <returns><see langword="true"/> if any prefix matched; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is the default value.</exception>
    public bool TryResolve(FsPath path, out TValue? value, out FsPath remaining)
    {
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        var node = Volatile.Read(ref _root);
        var segments = path.Segments;

        var bestValue = default(TValue);
        var bestDepth = -1;

        if (node.HasValue)
        {
            bestValue = node.Value;
            bestDepth = 0;
        }

        for (int i = 0; i < segments.Length; i++)
        {
            if (!node.Children.TryGetValue(segments[i], out var child))
            {
                break;
            }

            node = child;

            if (node.HasValue)
            {
                bestValue = node.Value;
                bestDepth = i + 1;
            }
        }

        if (bestDepth < 0)
        {
            value = null;
            remaining = path;
            return false;
        }

        value = bestValue;
        remaining = bestDepth == segments.Length
            ? FsPath.Root
            : path.SubPath(bestDepth, segments.Length - bestDepth);

        return true;
    }

    /// <summary>Associates a value with the given path, replacing any existing value.</summary>
    /// <param name="path">The path at which to store the value.</param>
    /// <param name="value">The value to store.</param>
    /// <returns><see langword="true"/> if an existing value at <paramref name="path"/> was replaced; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is the default value.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    public bool Set(FsPath path, TValue value)
    {
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        ArgumentNullException.ThrowIfNull(value);

        lock (_writeLock)
        {
            var oldRoot = _root;
            var replaced = FindExact(oldRoot, path.Segments);
            var newRoot = Insert(oldRoot, path.Segments, value);

            _root = newRoot;
            _version++;
            if (!replaced)
            {
                _count++;
            }

            return replaced;
        }
    }

    /// <summary>Removes the value at the specified path, if any.</summary>
    /// <param name="path">The path to remove.</param>
    /// <returns><see langword="true"/> if a value was present and has been removed; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path"/> is the default value.</exception>
    public bool Remove(FsPath path)
    {
        if (path.IsDefault)
        {
            throw new ArgumentException("Path must be initialized.", nameof(path));
        }

        lock (_writeLock)
        {
            var oldRoot = _root;
            var (newRoot, removed) = RemoveCore(oldRoot, path.Segments);

            if (!removed)
            {
                return false;
            }

            _root = newRoot ?? Node.Empty;
            _count--;
            _version++;
            return true;
        }
    }

    /// <summary>Enumerates a snapshot of all stored path/value pairs.</summary>
    /// <returns>An enumerable over the current contents. The snapshot is consistent with respect to concurrent writes.</returns>
    public IEnumerable<KeyValuePair<FsPath, TValue>> Enumerate()
    {
        var snapshot = Volatile.Read(ref _root);
        return EnumerateSnapshot(snapshot);
    }

    private static bool FindExact(Node node, ReadOnlySpan<string> segments)
    {
        for (int i = 0; i < segments.Length; i++)
        {
            if (!node.Children.TryGetValue(segments[i], out var child))
            {
                return false;
            }

            node = child;
        }

        return node.HasValue;
    }

    private static Node Insert(Node node, ReadOnlySpan<string> segments, TValue value)
    {
        if (segments.IsEmpty)
        {
            return new Node(node.Children, value, hasValue: true);
        }

        var head = segments[0];
        var tail = segments[1..];
        var child = node.Children.TryGetValue(head, out var existing) ? existing : Node.Empty;
        var newChild = Insert(child, tail, value);
        var newChildren = node.Children.SetItem(head, newChild);

        return new Node(newChildren, node.Value, node.HasValue);
    }

    private static (Node? Node, bool Removed) RemoveCore(Node node, ReadOnlySpan<string> segments)
    {
        if (segments.IsEmpty)
        {
            if (!node.HasValue)
            {
                return (node, false);
            }

            var cleared = new Node(node.Children, null, hasValue: false);
            return (cleared.IsEmpty ? null : cleared, true);
        }

        var head = segments[0];
        if (!node.Children.TryGetValue(head, out var child))
        {
            return (node, false);
        }

        var (newChild, removed) = RemoveCore(child, segments[1..]);
        if (!removed)
        {
            return (node, false);
        }

        var newChildren = newChild is null
            ? node.Children.Remove(head)
            : node.Children.SetItem(head, newChild);

        var newNode = new Node(newChildren, node.Value, node.HasValue);
        return (newNode.IsEmpty ? null : newNode, true);
    }

    private static IEnumerable<KeyValuePair<FsPath, TValue>> EnumerateSnapshot(Node root)
    {
        var stack = new Stack<(Node Node, string[] Path)>();
        stack.Push((root, Array.Empty<string>()));

        while (stack.Count > 0)
        {
            var (node, path) = stack.Pop();

            if (node.HasValue && node.Value is not null)
            {
                yield return new KeyValuePair<FsPath, TValue>(FsPath.FromSegments(path), node.Value);
            }

            foreach (var pair in node.Children)
            {
                var childPath = new string[path.Length + 1];
                Array.Copy(path, childPath, path.Length);
                childPath[path.Length] = pair.Key;
                stack.Push((pair.Value, childPath));
            }
        }
    }

    /// <summary>
    /// An immutable trie node. Structural sharing between snapshots means that a write
    /// copies only the nodes along the affected path, not the whole tree.
    /// </summary>
    private sealed class Node
    {
        public static readonly Node Empty = new(ImmutableDictionary<string, Node>.Empty, null, hasValue: false);

        public Node(ImmutableDictionary<string, Node> children, TValue? value, bool hasValue)
        {
            Children = children;
            Value = value;
            HasValue = hasValue;
        }

        public ImmutableDictionary<string, Node> Children { get; }

        public TValue? Value { get; }

        public bool HasValue { get; }

        public bool IsEmpty => !HasValue && Children.IsEmpty;
    }
}