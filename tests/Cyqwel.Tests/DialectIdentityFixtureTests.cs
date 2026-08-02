using System.Text.Json;
using Cyqwel.Dialects;

namespace Cyqwel.Tests;

public class DialectIdentityFixtureTests
{
    public static IEnumerable<object[]> Cases()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Identity");
        foreach (var path in Directory.EnumerateFiles(fixtureDirectory, "*.json").Order())
        {
            using var fixture = JsonDocument.Parse(File.ReadAllText(path));
            var dialect = fixture.RootElement.GetProperty("dialect").GetString()!;
            foreach (var item in fixture.RootElement.GetProperty("cases").EnumerateArray())
            {
                yield return
                [
                    dialect,
                    item.GetProperty("name").GetString()!,
                    item.GetProperty("sql").GetString()!,
                ];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Parse_generate_parse_is_stable(string dialectName, string caseName, string sql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);

        var generated = dialect.Parse(sql).ToSql(dialect);
        var regenerated = dialect.Parse(generated).ToSql(dialect);

        // Source spans prevent direct record equality across independently parsed trees.
        Assert.True(
            string.Equals(generated, regenerated, StringComparison.Ordinal),
            $"{dialectName}/{caseName} changed from {generated} to {regenerated}.");
    }
}
