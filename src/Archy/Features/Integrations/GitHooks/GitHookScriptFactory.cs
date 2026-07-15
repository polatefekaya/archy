using System.Text;

namespace Archy.Features.Integrations.GitHooks;

/// <summary>Creates portable POSIX wrappers that evaluate exactly the staged or pushed Git tree in disposable state.</summary>
internal static class GitHookScriptFactory
{
    internal const string ManagedHeader = "#!/bin/sh\n# archy-managed-hook: v1\n";

    public static string Create(GitHookKind kind, ArchyHookInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var body = kind switch
        {
            GitHookKind.PreCommit => PreCommitBody,
            GitHookKind.PrePush => PrePushBody,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported Git hook kind."),
        };
        return ManagedHeader + CommonBody
            .Replace("__ARCHY_EXECUTABLE__", Quote(invocation.ExecutablePath), StringComparison.Ordinal)
            .Replace("__ARCHY_MANAGED_ASSEMBLY__", invocation.ManagedAssemblyPath is null ? string.Empty : Quote(invocation.ManagedAssemblyPath), StringComparison.Ordinal)
            .Replace("__HAS_MANAGED_ASSEMBLY__", invocation.ManagedAssemblyPath is null ? "false" : "true", StringComparison.Ordinal)
            .Replace("__LEGACY_FILE__", Quote(LegacyFileName(kind)), StringComparison.Ordinal) + body;
    }

    public static string FileName(GitHookKind kind) => kind switch
    {
        GitHookKind.PreCommit => "pre-commit",
        GitHookKind.PrePush => "pre-push",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported Git hook kind."),
    };

    public static string LegacyFileName(GitHookKind kind) => $"{FileName(kind)}.archy-legacy";

    public static bool IsManaged(string content) => content.StartsWith(ManagedHeader, StringComparison.Ordinal);

    private static string Quote(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return $"'{value.Replace("'", "'\\\"'\\\"'", StringComparison.Ordinal)}'";
    }

    private const string CommonBody = """
    set -u

    hook_dir=$(CDPATH= cd "$(dirname "$0")" && pwd)
    legacy_hook="$hook_dir"/__LEGACY_FILE__
    archy_executable=__ARCHY_EXECUTABLE__
    archy_managed_assembly=__ARCHY_MANAGED_ASSEMBLY__
    has_managed_assembly=__HAS_MANAGED_ASSEMBLY__
    snapshot_directory=""
    push_input=""

    cleanup() {
      if [ -n "$snapshot_directory" ]; then
        rm -rf "$snapshot_directory"
      fi
      if [ -n "$push_input" ]; then
        rm -f "$push_input"
      fi
    }

    trap cleanup 0 HUP INT TERM

    run_legacy_hook() {
      if [ -x "$legacy_hook" ]; then
        "$legacy_hook" "$@"
        return $?
      fi
      return 0
    }

    run_archy() {
      if [ "$has_managed_assembly" = "true" ]; then
        "$archy_executable" "$archy_managed_assembly" "$@"
      else
        "$archy_executable" "$@"
      fi
    }

    verify_tree() {
      tree_id="$1"
      snapshot_directory=$(mktemp -d "${TMPDIR:-/tmp}/archy-hook.XXXXXX") || return 2
      if ! git init --quiet "$snapshot_directory"; then
        cleanup
        snapshot_directory=""
        return 2
      fi
      if ! git archive --format=tar "$tree_id" | tar -x -C "$snapshot_directory"; then
        cleanup
        snapshot_directory=""
        return 2
      fi
      if ! run_archy workspace init --path "$snapshot_directory" --state-root "$snapshot_directory/.archy-state"; then
        cleanup
        snapshot_directory=""
        return 2
      fi
      run_archy verify --path "$snapshot_directory" --state-root "$snapshot_directory/.archy-state"
      status=$?
      cleanup
      snapshot_directory=""
      return "$status"
    }

    is_zero_oid() {
      case "$1" in
        ""|*[!0]*) return 1 ;;
        *) return 0 ;;
      esac
    }

    """;

    private const string PreCommitBody = """
    run_legacy_hook "$@" || exit $?

    if git diff --cached --quiet; then
      exit 0
    fi

    staged_tree=$(git write-tree) || exit 2
    verify_tree "$staged_tree"
    exit $?
    """;

    private const string PrePushBody = """
    push_input=$(mktemp "${TMPDIR:-/tmp}/archy-pre-push.XXXXXX") || exit 2
    cat > "$push_input" || exit 2
    run_legacy_hook "$@" < "$push_input" || exit $?

    verified_oids="|"
    while IFS=' ' read -r local_ref local_oid remote_ref remote_oid; do
      if [ -z "$local_oid" ] || is_zero_oid "$local_oid"; then
        continue
      fi

      if ! is_zero_oid "$remote_oid" &&
         git cat-file -e "$remote_oid^{commit}" 2>/dev/null &&
         git diff --quiet "$remote_oid" "$local_oid" --; then
        continue
      fi

      case "$verified_oids" in
        *"|$local_oid|"*) continue ;;
      esac
      verified_oids="${verified_oids}${local_oid}|"

      pushed_tree=$(git rev-parse "$local_oid^{tree}") || exit 2
      verify_tree "$pushed_tree" || exit $?
    done < "$push_input"

    exit 0
    """;
}
