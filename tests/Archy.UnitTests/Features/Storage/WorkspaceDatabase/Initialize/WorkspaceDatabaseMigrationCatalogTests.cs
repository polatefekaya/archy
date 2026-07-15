using Archy.Features.Storage.WorkspaceDatabase.Initialize;

namespace Archy.UnitTests.Features.Storage.WorkspaceDatabase.Initialize;

public sealed class WorkspaceDatabaseMigrationCatalogTests
{
    [Fact]
    public void CreateUsesANewlineStableSqlChecksum()
    {
        var unixNewlineMigration = WorkspaceDatabaseMigration.Create(1, "test", "CREATE TABLE test (id INTEGER);\n");
        var windowsNewlineMigration = WorkspaceDatabaseMigration.Create(1, "test", "CREATE TABLE test (id INTEGER);\r\n");

        Assert.Equal(unixNewlineMigration.Checksum, windowsNewlineMigration.Checksum);
    }

    [Fact]
    public void ConstructorRejectsNonContiguousMigrationVersions()
    {
        var first = WorkspaceDatabaseMigration.Create(1, "first", "SELECT 1;");
        var third = WorkspaceDatabaseMigration.Create(3, "third", "SELECT 3;");

        Assert.Throws<ArgumentException>(() => new WorkspaceDatabaseMigrationCatalog([first, third]));
    }
}
