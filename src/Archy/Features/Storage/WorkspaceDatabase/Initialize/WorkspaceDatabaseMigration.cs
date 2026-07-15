using System.Security.Cryptography;
using System.Text;

namespace Archy.Features.Storage.WorkspaceDatabase.Initialize;

public sealed record WorkspaceDatabaseMigration(
    int Version,
    string Name,
    string Sql,
    string Checksum)
{
    public static WorkspaceDatabaseMigration Create(int version, string name, string sql)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Migration versions start at one.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        var normalizedSql = NormalizeSql(sql);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedSql))).ToLowerInvariant();
        return new WorkspaceDatabaseMigration(version, name, normalizedSql, checksum);
    }

    private static string NormalizeSql(string sql)
    {
        var normalizedLineEndings = sql.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalizedLineEndings.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = lines[index].TrimEnd();
        }

        return string.Join('\n', lines).Trim();
    }
}
