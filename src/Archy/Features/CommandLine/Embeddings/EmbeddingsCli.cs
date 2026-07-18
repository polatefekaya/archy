namespace Archy.Features.CommandLine.Embeddings;

using Mediator;

public static class EmbeddingsCli
{
    public static Task<int> RunAsync(string[] args, IMediator mediator, IServiceProvider? serviceProvider, CancellationToken cancellationToken) => args.FirstOrDefault() switch
    {
        "index" => IndexEmbeddingsCli.RunAsync(args[1..], mediator, cancellationToken),
        "status" => EmbeddingStatusCli.RunAsync(args[1..], mediator, cancellationToken),
        "setup" => SetupEmbeddingsCli.RunAsync(args[1..], mediator, cancellationToken),
        _ => HelpAsync(),
    };
    private static Task<int> HelpAsync() { Console.WriteLine("Usage: archy embeddings setup|index|status [--path <path>] [--model <model-id>] [--max-chunks <1-2048>] [--dry-run] [--json]"); return Task.FromResult(0); }
}
