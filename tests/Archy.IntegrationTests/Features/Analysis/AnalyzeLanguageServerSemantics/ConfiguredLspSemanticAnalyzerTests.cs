using System.Security.Cryptography;
using System.Text;
using Archy.Features.Analysis.AnalyzeLanguageServerSemantics;
using Archy.Features.Analysis.ExternalLanguageServerProtocol.EstablishStdioSession;
using Archy.Features.Analysis.LanguageSemanticAdapters;
using Archy.Features.Analysis.LanguageServerProfiles;
using Archy.Features.Configuration.LoadEffectiveConfiguration;

namespace Archy.IntegrationTests.Features.Analysis.AnalyzeLanguageServerSemantics;

public sealed class ConfiguredLspSemanticAnalyzerTests
{
    [Fact]
    public async Task ATypescriptProfileRunsThroughTheGenericHostWithoutLanguageSpecificCode()
    {
        var repository = Directory.CreateTempSubdirectory("archy-typescript-lsp-");
        try
        {
            var relativePath = "src/clock.ts";
            var bytes = Encoding.UTF8.GetBytes("export class Clock {}\n");
            var fullPath = Path.Combine(repository.FullName, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, bytes);
            await File.WriteAllTextAsync(Path.Combine(repository.FullName, "package.json"), "{}");
            var profile = new LanguageServerProfileConfiguration(
                "typescript",
                "typescript",
                [".ts", ".tsx"],
                ["package.json"],
                "dotnet",
                [TestHostAssemblyPath(), "lsp-mock-server", "semantic"],
                "ts",
                [new LanguageServerSymbolKindMapping("type", [5])]);
            var analyzer = new ConfiguredLspSemanticAnalyzer(
                new LanguageServerProfileSelector(new PathExecutablePathProbe()),
                new LanguageServerStdioSessionFactory());

            var result = await analyzer.AnalyzeAsync(
                new ConfiguredLspSemanticAnalysisRequest(
                    new SemanticAnalysisRequest(
                        SemanticAnalysisContract.CurrentSchemaVersion,
                        repository.FullName,
                        "snapshot",
                        [new SemanticDocument(relativePath, Convert.ToHexString(SHA256.HashData(bytes)), "typescript")]),
                    profile,
                    "configuration"),
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
            Assert.Equal("typescript", result.Value.AdapterId);
            Assert.Equal("typescript", result.Value.Language);
            var symbol = Assert.Single(result.Value.Symbols);
            Assert.Equal("ts:Clock@src/clock.ts:2:21", symbol.CanonicalId);
        }
        finally
        {
            repository.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task StandardLspReferencesAndOutgoingCallsAreCollectedAndAttributedThroughScopes()
    {
        var repository = Directory.CreateTempSubdirectory("archy-typescript-lsp-relations-");
        try
        {
            var relativePath = "src/clock.ts";
            var bytes = Encoding.UTF8.GetBytes("export function Caller() {\n  Target();\n}\nexport function Target() {}\n");
            var fullPath = Path.Combine(repository.FullName, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, bytes);
            await File.WriteAllTextAsync(Path.Combine(repository.FullName, "package.json"), "{}");
            var result = await CreateAnalyzer().AnalyzeAsync(
                new ConfiguredLspSemanticAnalysisRequest(
                    new SemanticAnalysisRequest(
                        SemanticAnalysisContract.CurrentSchemaVersion,
                        repository.FullName,
                        "snapshot",
                        [new SemanticDocument(relativePath, Convert.ToHexString(SHA256.HashData(bytes)), "typescript")]),
                    TypeScriptRelationshipProfile(),
                    "configuration"),
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
            var caller = Assert.Single(result.Value.Symbols, static symbol => symbol.DisplayName == "Caller");
            var target = Assert.Single(result.Value.Symbols, static symbol => symbol.DisplayName == "Target");
            Assert.Equal(new SemanticSourceRange(relativePath, 1, 1, 3, 2), caller.ScopeRange);
            Assert.Equal(new SemanticReference(caller.CanonicalId, target.CanonicalId, new SemanticSourceRange(relativePath, 2, 3, 2, 9)), Assert.Single(result.Value.References));
            Assert.Equal(new SemanticCall(caller.CanonicalId, target.CanonicalId, new SemanticSourceRange(relativePath, 2, 3, 2, 9)), Assert.Single(result.Value.Calls));
            Assert.Equal(SemanticCapabilityState.Available, result.Value.Capabilities.Single(static capability => capability.Name == "references").State);
            Assert.Equal(SemanticCapabilityState.Available, result.Value.Capabilities.Single(static capability => capability.Name == "callHierarchy").State);
        }
        finally
        {
            repository.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfiguredQueryLimitDegradesOnlyRelationshipCapabilities()
    {
        var repository = Directory.CreateTempSubdirectory("archy-typescript-lsp-limit-");
        try
        {
            var relativePath = "src/clock.ts";
            var bytes = Encoding.UTF8.GetBytes("export function Caller() {\n  Target();\n}\nexport function Target() {}\n");
            var fullPath = Path.Combine(repository.FullName, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, bytes);
            await File.WriteAllTextAsync(Path.Combine(repository.FullName, "package.json"), "{}");
            var result = await CreateAnalyzer().AnalyzeAsync(
                new ConfiguredLspSemanticAnalysisRequest(
                    new SemanticAnalysisRequest(
                        SemanticAnalysisContract.CurrentSchemaVersion,
                        repository.FullName,
                        "snapshot",
                        [new SemanticDocument(relativePath, Convert.ToHexString(SHA256.HashData(bytes)), "typescript")]),
                    TypeScriptRelationshipProfile(maxSymbolQueries: 1),
                    "configuration"),
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Problem!.Message);
            Assert.Equal(2, result.Value.Symbols.Count);
            Assert.Empty(result.Value.References);
            Assert.Empty(result.Value.Calls);
            Assert.Equal(SemanticCapabilityState.Available, result.Value.Capabilities.Single(static capability => capability.Name == "documentSymbol").State);
            Assert.Equal(SemanticCapabilityState.Degraded, result.Value.Capabilities.Single(static capability => capability.Name == "references").State);
            Assert.Equal(SemanticCapabilityState.Degraded, result.Value.Capabilities.Single(static capability => capability.Name == "callHierarchy").State);
            Assert.Contains(result.Value.Diagnostics, static diagnostic => diagnostic.Code == "language_server_references_query_limit_exceeded");
            Assert.Contains(result.Value.Diagnostics, static diagnostic => diagnostic.Code == "language_server_call_hierarchy_query_limit_exceeded");
        }
        finally
        {
            repository.Delete(recursive: true);
        }
    }

    private static ConfiguredLspSemanticAnalyzer CreateAnalyzer() => new(
        new LanguageServerProfileSelector(new PathExecutablePathProbe()),
        new LanguageServerStdioSessionFactory());

    private static LanguageServerProfileConfiguration TypeScriptRelationshipProfile(int maxSymbolQueries = 10_000) => new(
        "typescript",
        "typescript",
        [".ts"],
        ["package.json"],
        "dotnet",
        [TestHostAssemblyPath(), "lsp-mock-server", "semantic-relations"],
        "ts",
        [new LanguageServerSymbolKindMapping("method", [6])],
        maxSymbolQueries);

    private static string TestHostAssemblyPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Archy.TestHost.dll");
        Assert.True(File.Exists(path), $"The test host assembly was not copied to '{path}'.");
        return path;
    }
}
