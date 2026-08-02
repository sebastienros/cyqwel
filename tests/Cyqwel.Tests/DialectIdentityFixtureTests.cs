using Cyqwel.Dialects;

namespace Cyqwel.Tests;

/// <summary>
/// SQLGlot v30.12.0 identity cases regenerated through Polyglot's extractor.
/// Polyglot does not commit its generated blobs, so these inline groups are a supported
/// static subset rather than exhaustive parity. Exclusions cover warehouse, administrative,
/// procedural, advanced extension, type-inference, and error-formatting cases outside Cyqwel's AST.
/// </summary>
public partial class DialectIdentityFixtureTests
{
    private static void AssertStable(SqlDialect dialect, string caseName, string sql)
    {
        var generated = dialect.Parse(sql).ToSql(dialect);
        var regenerated = dialect.Parse(generated).ToSql(dialect);

        // Source spans prevent direct record equality across independently parsed trees.
        Assert.True(
            string.Equals(generated, regenerated, StringComparison.Ordinal),
            $"{dialect.Name}/{caseName} changed from {generated} to {regenerated}.");
    }
}
