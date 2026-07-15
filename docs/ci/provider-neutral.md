# Provider-neutral CI integration

Use [the CI entrypoint](../../scripts/ci/verify.sh) from any CI system that can run a POSIX shell on macOS arm64. It is intentionally provider-neutral: the script initializes disposable Archy state, runs the one canonical enforcement command, writes SARIF 2.1.0, and returns Archy's verification exit code.

Linux is a later platform deliverable. Until its Native AOT release is published and verified, the entrypoint rejects Linux and every other unsupported host instead of downloading an untested binary.

## Release artifact contract

For released version `X.Y.Z`, publish this signed/reviewed release asset and its independently communicated SHA-256 digest:

```text
https://github.com/polatefekaya/archy/releases/download/vX.Y.Z/archy-X.Y.Z-osx-arm64.tar.gz
```

The archive must contain exactly the executable `archy` at its root. The CI caller must pin both `ARCHY_VERSION=X.Y.Z` and `ARCHY_SHA256=<64 lowercase-or-uppercase hexadecimal characters>`; the script refuses an unpinned download and verifies the digest before extraction. It then confirms that `archy --version` reports the requested version. This keeps a mutable release URL from becoming a source of unreviewed CI code.

Create that asset with `sh scripts/release/package-osx-arm64.sh X.Y.Z`. The packaging script Native-AOT publishes Archy, verifies the embedded product version before archiving, and writes the matching `.sha256` file. Upload both files to the matching release tag; the release workflow must record the digest in reviewed CI configuration rather than fetching an untrusted checksum at runtime. A release environment that already restored the exact project may set `ARCHY_RELEASE_NO_RESTORE=true`; otherwise the script performs the ordinary package restore, including its configured audit policy.

For a hermetic/internal build, set `ARCHY_BIN` to an absolute or relative executable path instead. That bypasses downloading only; the same disposable-state initialization, SARIF output, and exit semantics still apply.

## Invocation

```sh
ARCHY_VERSION=0.1.0 \
ARCHY_SHA256='<release-sha256>' \
ARCHY_SARIF_OUTPUT=artifacts/archy.sarif \
sh scripts/ci/verify.sh
```

Optional inputs:

| Variable | Default | Meaning |
|---|---|---|
| `ARCHY_REPOSITORY_ROOT` | current directory | Git checkout to analyze. |
| `ARCHY_SARIF_OUTPUT` | `artifacts/archy.sarif` under the checkout | SARIF output path; CI should upload this file even on a nonzero verify result. |
| `ARCHY_STATE_ROOT` | a disposable temporary directory | Explicit state location; use a CI job-local path only. |
| `ARCHY_BIN` | unset | A trusted, executable Archy binary; skips release download. |
| `ARCHY_VERSION` / `ARCHY_SHA256` | required without `ARCHY_BIN` | Pinned release identity and expected digest. |
| `ARCHY_RELEASE_BASE_URL` | Archy's GitHub Releases download endpoint | Mirror root with the same release-asset layout. |

The script runs `archy workspace init` only against the configured disposable state root, then invokes `archy verify --sarif --output`. It does not create a baseline, exception, or any source-controlled policy file. Exit `0` means no introduced deterministic finding, `1` means the required check should fail for an introduced finding, `2` indicates Archy/configuration/analysis failure, and `64` is an invalid CI setup or unsupported platform. SARIF is still produced for `verify` failures whenever Archy can begin verification.
