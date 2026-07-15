namespace Archy.Features.Duplicates.CheckDataShapeNaming;

public interface IDataShapeNamingChecker
{
    DataShapeNamingAdvisory? Check(DataShapeNamingComparison comparison);
}
