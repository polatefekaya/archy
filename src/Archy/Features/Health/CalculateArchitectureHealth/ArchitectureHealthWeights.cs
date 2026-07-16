namespace Archy.Features.Health.CalculateArchitectureHealth;
public sealed record ArchitectureHealthWeights(double Architecture,double Duplicates,double Documentation,double Decisions){public static ArchitectureHealthWeights Default{get;}=new(.4,.2,.2,.2);}
