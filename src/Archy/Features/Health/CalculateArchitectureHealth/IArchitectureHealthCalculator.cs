using Archy.Features.Health.HealthSnapshots;
namespace Archy.Features.Health.CalculateArchitectureHealth;
public interface IArchitectureHealthCalculator { HealthSnapshotFact Calculate(ArchitectureHealthInput input); }
