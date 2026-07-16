namespace Archy.Features.Memory.ComposeSessionChangelog;
public interface ISessionChangelogComposer { string Compose(IReadOnlyList<SessionChangelogEntry> entries); }
