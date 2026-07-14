# Workspace state and locking

Every Archy graph belongs to exactly one Git repository or monorepo root. Its mutable state lives outside that worktree by default:

```text
~/.archy/workspaces/<sha256(canonical-repository-root)>/
  workspace.json
  locks/
    workspace.lock
```

`workspace init` resolves configuration, derives this location, obtains an exclusive workspace lock, and then creates or reads the manifest. A user or explicitly selected configuration may redirect the state root; repository-owned `archy.toml` cannot, so cloning an untrusted repository cannot redirect local writes.

On macOS, Archy uses an advisory `flock` lease over `workspace.lock`:

- Read operations may hold the lease concurrently.
- Write operations are exclusive against both readers and writers.
- The lease is tied to the open file handle and is released by disposal or process exit; Archy does not delete lock files to recover from a presumed stale lock.
- Acquisition is cancellation-aware. The current initialization path waits up to 30 seconds for its exclusive lease and returns a conflict if it cannot obtain one.

This primitive is the required coordination boundary for future scan, watcher, MCP, hook, migration, and UI write operations. Linux support will be an explicit platform adapter task; it must preserve these read/write semantics rather than substituting an uncoordinated lock file.
