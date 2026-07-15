namespace Archy.Features.Integrations.GitHooks;

/// <summary>Captures one hook file so a two-hook lifecycle operation can restore an all-or-nothing outcome.</summary>
internal sealed record GitHookFileSnapshot(
    string Path,
    bool Exists,
    byte[]? Content,
    UnixFileMode? UnixFileMode)
{
    public static GitHookFileSnapshot Capture(string path)
    {
        if (Directory.Exists(path))
        {
            throw new IOException($"'{path}' is a directory, not a Git hook file.");
        }

        if (!File.Exists(path))
        {
            return new GitHookFileSnapshot(path, false, null, null);
        }

        return new GitHookFileSnapshot(
            path,
            true,
            File.ReadAllBytes(path),
            OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(path));
    }

    public void Restore()
    {
        if (!Exists)
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }

            return;
        }

        File.WriteAllBytes(Path, Content!);
        if (!OperatingSystem.IsWindows() && UnixFileMode is not null)
        {
            File.SetUnixFileMode(Path, UnixFileMode.Value);
        }
    }
}
