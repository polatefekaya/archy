namespace Archy.Features.Memory.DetermineImportantNodes;

public enum ImportantNodeEligibilityReason
{
    DirectoryContainsSource,
    NamespaceDeclaration,
    PublicTypeDeclaration,
    PublicApiDeclaration,
    ConfiguredModule,
}
