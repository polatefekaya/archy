using Archy.Features.Analysis.DiscoverCSharpProjects;
using Archy.UnitTests.Features.Analysis.ExtractCSharpSyntaxFacts;

namespace Archy.UnitTests.Features.Analysis.DiscoverCSharpProjects;

public sealed class CSharpProjectDiscoveryTests
{
    [Fact]
    public async Task DiscoverBuildsADeterministicMultiProjectMap()
    {
        using var fixture = CSharpSyntaxFactFixture.Create();
        await fixture.WriteAsync("Archy.sln", "Microsoft Visual Studio Solution File", Archy.Features.Analysis.InventorySources.SourceLanguage.Unknown);
        await fixture.WriteAsync("src/Core/Core.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net10.0;net9.0</TargetFrameworks><EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles><CompilerGeneratedFilesOutputPath>generated</CompilerGeneratedFilesOutputPath></PropertyGroup>
              <ItemGroup><Compile Include="Code/**/*.cs" /></ItemGroup>
            </Project>
            """, Archy.Features.Analysis.InventorySources.SourceLanguage.Unknown);
        await fixture.WriteAsync("src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
              <ItemGroup><ProjectReference Include="../Core/Core.csproj" /></ItemGroup>
            </Project>
            """, Archy.Features.Analysis.InventorySources.SourceLanguage.Unknown);

        var result = await new CSharpProjectDiscovery().DiscoverAsync(fixture.RepositoryRoot, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Archy.sln"], result.Value.SolutionPaths);
        Assert.Equal(2, result.Value.Projects.Count);
        var app = Assert.Single(result.Value.Projects, static project => project.RepositoryRelativePath == "src/App/App.csproj");
        Assert.Equal(["src/Core/Core.csproj"], app.ProjectReferences);
        Assert.False(app.GeneratedCode.EnableDefaultCompileItems);
        var core = Assert.Single(result.Value.Projects, static project => project.RepositoryRelativePath == "src/Core/Core.csproj");
        Assert.Equal(["net10.0", "net9.0"], core.TargetFrameworks);
        Assert.Contains("src/Core/Code", core.SourceRoots);
        Assert.True(core.GeneratedCode.EmitCompilerGeneratedFiles);
    }
}
