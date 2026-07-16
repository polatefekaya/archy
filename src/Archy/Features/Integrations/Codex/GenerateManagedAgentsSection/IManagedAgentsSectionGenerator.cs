using Archy.SharedKernel.Primitives;

namespace Archy.Features.Integrations.Codex.GenerateManagedAgentsSection;

public interface IManagedAgentsSectionGenerator
{
    Result<AgentsManagedSectionResult> Generate(string existingDocument, AgentsManagedSectionInput input);
}
