namespace Archy.Features.Configuration.LoadEffectiveConfiguration;

public sealed class UserConfigurationPathProvider : IUserConfigurationPathProvider
{
    public string? GetPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? null
            : Path.Combine(userProfile, ".archy", "config.toml");
    }
}
