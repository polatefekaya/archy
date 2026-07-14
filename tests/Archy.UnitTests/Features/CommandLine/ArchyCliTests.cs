using Archy.Features.CommandLine;

namespace Archy.UnitTests.Features.CommandLine;

[Collection(nameof(ConsoleOutputSynchronization))]
public sealed class ArchyCliTests
{
    [Fact]
    public async Task VersionCommandIsAvailableWithoutStartingTheHost()
    {
        var originalOutput = Console.Out;
        using var capturedOutput = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);

        try
        {
            Console.SetOut(capturedOutput);
            var exitCode = await ArchyCli.RunAsync(["--version"], mediator: null!, CancellationToken.None);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        Assert.Equal(
            $"Archy {ArchyProductMetadata.Version}{Environment.NewLine}",
            capturedOutput.ToString());
    }
}
