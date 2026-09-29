# Coding conventions

## Language and style

- Code, identifiers, comments, XML-doc, exception messages, tests, commits: **English**.
- All public API members have XML-doc (`<summary>`, `<param>`, `<returns>`, `<exception>`, `<remarks>`). `CS1591` is an error.
- `sealed` by default. Inheritance requires justification.
- Nullable reference types enabled everywhere.
- Target framework: `net10.0`. No `netstandard2.0`.
- File-scoped namespaces.

## Where does a type go

Ask these questions in order.

1. **Is it a contract or a piece of machinery?** Contracts (interfaces, abstract classes, path types, enums, exceptions, DTOs) go in `Abstractions`. Machinery (stateful classes, algorithms, internal utilities) goes in `Core` or a profile package.

2. **Does a third party need it to implement `IFileSystem` or call `VfsRoot`?** If yes, `Abstractions`. If no, `Core`.

3. **Does it depend on Core-level concepts?** `IResolveTracer` is an interface, but it exists for the resolver, not for third-party file systems. It lives in `Core`.

4. **Is it specific to a file system format?** Then it belongs in a `FileSystems.*` package.

5. **Is it a decorator, policy, or composition rule?** `Composition`, `Policies`, `Hosting`, `Diagnostics` packages respectively.

### Examples

| Type | Where | Why |
|---|---|---|
| `VfsUri`, `FsPath`, `RelativePath`, `VfsScheme` | `Abstractions` | Contracts, used by everyone |
| `IFileSystem`, `IMountableFileSystem`, `IPatchableContainerFileSystem` | `Abstractions` | Interfaces |
| `FileSystemEntry`, `FileEntry`, `DirectoryEntry`, `SymbolicLink` | `Abstractions` | Abstract classes / data |
| `VfsException` and descendants | `Abstractions` | Public exception hierarchy |
| `MountOptions`, `VfsBehaviorOptions`, `ContainerPatchOptions` | `Abstractions` | DTOs |
| `IWritePolicy`, `WriteContext`, `PrimaryWritePolicy` | `Abstractions.Policies` | Contract + trivial default |
| `PathTrie`, `MountTable`, `MountedFileSystem`, `PathResolver` | `Core` | Machinery |
| `VfsRoot`, `VfsEntry`, `VfsWalker`, `SchemeDispatcher` | `Core` | User-facing machinery |
| `MemoryFileSystem`, `MemoryFileSystemOptions` | `FileSystems.Memory` | Implementation |
| `UnionFileSystem`, `ReadOnlyFileSystem` | `Composition` | Decorators |
| `VfsBuilder`, `ServiceCollectionExtensions` | `Hosting` | DI integration |
| `ConsoleResolveTracer`, `ChromeTraceResolveTracer` | `Diagnostics` | Exporters |

If it takes more than a few seconds to decide, the API is probably unclear. Discuss before guessing.

## XML documentation

Follow BCL style: concise, `<para>` for paragraphs, `<see cref="..."/>` for cross-references, `<see langword="null"/>` for keywords.

Every public member documents:

- What it does in one sentence.
- Parameters with `<param>`.
- Return value with `<returns>` (including for `ValueTask`-returning methods — StyleCop treats them as returning).
- Exceptions with `<exception cref="...">`.
- `<remarks>` for non-obvious behavior.

## Async patterns

- `ValueTask<T>` for single results.
- `IAsyncEnumerable<T>` for streams of values.
- `CancellationToken ct = default` on every public async method.
- `ConfigureAwait(false)` in library code.
- `[EnumeratorCancellation]` on the cancellation token parameter in async iterators only (not on non-iterator methods that return `IAsyncEnumerable`).
- Call `ct.ThrowIfCancellationRequested()` at entry and after long-running awaits.

## Tests

- xUnit + FluentAssertions.
- Test class per public type.
- Test names: `Method_Scenario_ExpectedResult`.
- `[Theory]` for parameterized inputs.
- Async tests use `async Task`, not `async void`.
- Fakes live in `Fakes/` and are minimal.

## StyleCop / Roslynator

A strict subset is enabled; see `.editorconfig`. Notable disabled rules:

- `SA1200`, `SA1201`–`SA1204` (ordering) — we order for readability.
- `SA1309` (field naming) — `_camelCase` is standard.
- `SA1101` (`this.` prefix) — we prefer no prefix.
- `SA1600`–`SA1602` (docs on non-public members) — `CS1591` covers public only.
- `SA1633` (file header) — no headers.

If a new StyleCop rule fights the modern .NET style, disable it in `.editorconfig` with a one-line comment explaining why.

## Exceptions

- All library errors derive from `VfsException` except `OperationCanceledException` and `NotSupportedException`.
- Do not throw raw `IOException`, `UnauthorizedAccessException`, or other BCL I/O exceptions across the public API. Wrap them in a `VfsException` subclass if the underlying cause is relevant.
- `NotSupportedException` signals "this file system does not implement this operation"; it is not a `VfsException` subclass, matching BCL conventions.