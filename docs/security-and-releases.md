# Security and release operations

## Local transports

`archy web serve` accepts only a loopback bind address. Its request body ceiling, request-header timeout, generic error response, no-store cache policy, and browser security headers are applied before routes run. It emits no permissive CORS header. Optional `archy mcp http` likewise binds loopback and requires an explicit bearer token of at least 24 characters; authorization comparison is constant-time.

## Sidecar integrity and offline operation

Release archives contain the sidecar sources, lock files, `checksums.sha256`, and `release-manifest.json`. The installer verifies checksums before any installation write. Node/Python acquisition remains an operator responsibility; this is intentional so Archy does not silently download and execute an unreviewed runtime. For offline installation, transfer the verified archive and the separately approved runtimes/package caches, then use `npm ci --offline` in the required sidecar directory.

## Updates and rollback

Verify the archive checksum, create `archy db backup`, install the new binary, and run `archy db check` before scanning. Database migration catalog checksums and version checks fail closed on a binary/schema mismatch. To roll back after a failed migration, stop Archy, reinstall the prior compatible binary, restore the backup, and run `archy db check`. Never copy a workspace SQLite database over a running process.

## Publishing releases

`eng/Version.props` is the single repository-owned stable version. Run the **Publish Archy release** workflow only from `main`, selecting a patch, minor, or major increment. On the first release it publishes the untagged repository version; later runs increment from the most recently released version and commit that change before packaging.

The workflow runs release quality gates, builds Native AOT archives independently on Apple Silicon and Intel macOS runners, verifies each archive's install/uninstall contract, validates the archive checksums, and only then creates the immutable `vX.Y.Z` tag and GitHub Release. A rerun only publishes artifacts for the same verified commit and refuses a conflicting existing tag.

The workflow uses its `GITHUB_TOKEN` to commit the prepared version, create the tag, and publish the release. Repository Actions settings must therefore allow workflows read/write repository contents; branch protections must allow the GitHub Actions bot to write the release-version commit to `main`.

## Dependency policy

CI runs `scripts/ci/dependency-audit.sh`: it records NuGet transitive vulnerability data and fails for high/critical production npm advisories in the UI and bundled sidecars. A maintainer must either update/remove an affected dependency or document a time-bounded exception with the upstream advisory, affected feature, and compensating control before release.
