# GitHub Actions integration

Use the repository's **Archy verification** workflow template, or copy [the template](../../.github/workflow-templates/archy-verify.yml) to `.github/workflows/archy-verify.yml`. It invokes the published, versioned [Archy composite action](../../actions/archy-verify/action.yml) at `polatefekaya/archy/actions/archy-verify@v0.1.0`, retains `artifacts/archy.sarif` on every result, uploads SARIF to GitHub code scanning when enabled, and makes the job named **`Archy / verify`** fail for any nonzero canonical verification result.

Keep the action reference on an Archy release tag and deliberately update it with each reviewed action release. The action version controls the CI glue; `ARCHY_VERSION` and `ARCHY_SHA256` independently control the Native AOT binary that executes verification.

The template targets `macos-14`, which GitHub currently documents as an arm64 standard macOS runner; it matches the only released Native AOT runtime identifier, `osx-arm64`. Do not change the runner to Linux until Archy's corresponding Linux artifact and support claim are published. GitHub documents `github/codeql-action/upload-sarif@v4` as the SARIF upload action and requires `security-events: write` for its token. [GitHub runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners), [SARIF upload guidance](https://docs.github.com/en/code-security/how-tos/find-and-fix-code-vulnerabilities/integrate-with-existing-tools/upload-sarif-file)

## Required configuration

Before enabling the workflow, set these repository or organization Actions variables to a reviewed release pair:

| Variable | Required value |
|---|---|
| `ARCHY_VERSION` | Exact release version, for example `0.1.0`. |
| `ARCHY_SHA256` | The 64-character SHA-256 digest published for `archy-<version>-osx-arm64.tar.gz`. |

The action uses those values only to call the provider-neutral entrypoint; see [its release contract](provider-neutral.md). An empty, malformed, unavailable, or digest-mismatched release is a configuration failure, not a skipped check. Internal consumers may replace the download with the composite action's trusted `binary-path` input, but must still use a macOS-arm64 Archy executable.

The workflow deliberately uses `pull_request`, not `pull_request_target`: it does not grant a write token or trusted-base context to code from a fork. GitHub can restrict write permissions for fork pull requests, so the SARIF upload step is allowed to fail without changing the enforcement result. The required `Archy / verify` job still runs and fails correctly; trusted pushes and same-repository pull requests receive code-scanning annotations when GitHub code scanning is enabled.

## Baseline and exception governance

The checkout's committed `archy.baseline.json`, `archy.exceptions.json`, and `archy.toml` are the only policy artifacts the job reads. The workflow never runs `archy baseline accept` or `archy exception accept`; it neither writes nor “heals” policy during CI. A pull request can therefore change a baseline only through an ordinary reviewed source change.

Protect those artifacts from self-approval. Add them, the workflow, the composite action, and `CODEOWNERS` itself to architecture-owner ownership, then enable **Require review from Code Owners** and dismiss stale approvals. The same rule must protect the default branch and the `Archy / verify` status check must be required and strict (up to date with the base branch). GitHub warns that required job names must be unique across workflows, so do not reuse `Archy / verify` for another job. [Branch protection](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/managing-a-branch-protection-rule), [Code owners](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners)

Suggested `CODEOWNERS` entries, replacing `@your-architecture-team` with a real user or team:

```text
/archy.toml                 @your-architecture-team
/archy.baseline.json        @your-architecture-team
/archy.exceptions.json      @your-architecture-team
/.github/                   @your-architecture-team
/actions/archy-verify/      @your-architecture-team
/scripts/ci/verify.sh       @your-architecture-team
/.github/CODEOWNERS         @your-architecture-team
```

## Branch-protection checklist

1. Enable the workflow on the default branch and allow one run so GitHub discovers the `Archy / verify` check.
2. Create a branch protection rule or ruleset for the default branch.
3. Require pull requests, at least one review, stale-approval dismissal, and code-owner review.
4. Require the GitHub Actions **`Archy / verify`** check, select strict/up-to-date behavior, and restrict direct pushes and bypass permissions.
5. Upload or retain the `archy-sarif` artifact for every run. Enable GitHub code scanning for the repository if PR annotations are desired.

Branch protection is the mandatory merge boundary. Local Git hooks accelerate feedback but can be bypassed; a workflow that is present without being made a required status check does not enforce delivery.
