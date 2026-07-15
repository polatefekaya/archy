namespace Archy.Features.Duplicates.ComposeDuplicateInspection;

public interface IDuplicateInspectionComposer
{
    DuplicateInspection Compose(DuplicateInspectionRequest request);
}
