using Archy.Features.Analysis.LanguageServerProfiles;
using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.UnitTests.Features.Analysis.LanguageServerProfiles;

public sealed class LanguageServerProfileSelectorTests
{
    [Fact]
    public void SelectsAProfileFromItsConfiguredMarkerAndCommand()
    {
        var repository = Directory.CreateTempSubdirectory("archy-profile-selector-");
        try
        {
            File.WriteAllText(Path.Combine(repository.FullName, "package.json"), "{}");
            var probe = new FakeProbe("typescript-language-server");
            var selection = new LanguageServerProfileSelector(probe).Resolve(repository.FullName, TypeScriptProfile(), hasMatchingDocuments: true);

            Assert.Equal(LanguageServerProfileSelectionState.Selected, selection.State);
            Assert.Equal("typescript-language-server", selection.ExecutablePath);
            Assert.Equal(1, probe.Calls);
        }
        finally
        {
            repository.Delete(recursive: true);
        }
    }

    [Fact]
    public void DoesNotProbeAProfileWithoutMatchingSnapshotDocuments()
    {
        var probe = new FakeProbe("typescript-language-server");
        var selection = new LanguageServerProfileSelector(probe).Resolve("/repo", TypeScriptProfile(), hasMatchingDocuments: false);

        Assert.Equal(LanguageServerProfileSelectionState.NotApplicable, selection.State);
        Assert.Equal(0, probe.Calls);
        Assert.Equal("language_profile_no_matching_documents", Assert.Single(selection.Diagnostics).Code);
    }

    private static LanguageServerProfileConfiguration TypeScriptProfile() => new(
        "typescript",
        "typescript",
        [".ts", ".tsx"],
        ["package.json"],
        "typescript-language-server",
        ["--stdio"],
        "ts",
        [new LanguageServerSymbolKindMapping("type", [5])]);

    private sealed class FakeProbe(string executable) : IExecutablePathProbe
    {
        public int Calls { get; private set; }

        public string? Resolve(string command, string workingDirectory)
        {
            Calls++;
            return string.Equals(command, executable, StringComparison.Ordinal) ? executable : null;
        }
    }
}
