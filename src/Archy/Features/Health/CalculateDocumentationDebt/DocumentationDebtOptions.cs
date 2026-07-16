namespace Archy.Features.Health.CalculateDocumentationDebt;
public sealed record DocumentationDebtOptions(long StaleAfterRevisions){public static DocumentationDebtOptions Default{get;}=new(5);}
