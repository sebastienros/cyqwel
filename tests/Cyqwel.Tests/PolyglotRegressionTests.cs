using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;
using Cyqwel.Visitors;

namespace Cyqwel.Tests;

public class PolyglotRegressionTests
{
    [Theory]
    [InlineData("EXPLAIN (VERBOSE, COSTS OFF) SELECT 1", "EXPLAIN SELECT 1")]
    [InlineData("EXPLAIN (FORMAT json) SELECT 1", "EXPLAIN SELECT 1")]
    [InlineData("EXPLAIN (ANALYZE) SELECT 1", "EXPLAIN ANALYZE SELECT 1")]
    [InlineData("EXPLAIN (ANALYZE true, BUFFERS) SELECT MAX(a) FROM t", "EXPLAIN ANALYZE SELECT MAX(a) FROM t")]
    [InlineData("EXPLAIN (ANALYZE false) SELECT 1", "EXPLAIN SELECT 1")]
    [InlineData("EXPLAIN ANALYZE SELECT 1", "EXPLAIN ANALYZE SELECT 1")]
    [InlineData("EXPLAIN (SELECT 1)", "EXPLAIN (SELECT 1)")]
    [InlineData("EXPLAIN (SELECT a)", "EXPLAIN (SELECT a)")]
    public void PostgreSql_explain_options_are_normalized(string sql, string expected)
    {
        var document = SqlDialects.PostgreSql.Parse(sql);
        var explain = Assert.IsType<ExplainStatement>(Assert.Single(document.Statements));

        Assert.Equal(expected, document.ToSql(SqlDialects.PostgreSql));
        Assert.Same(explain, explain.Accept(new NoopRewriter()));
        explain.Accept(new NoopVisitor());
        Assert.Contains(explain.Query, explain.DescendantsAndSelf());
    }

    [Fact]
    public void PostgreSql_explain_rejects_unsupported_targets()
    {
        var document = SqlDialects.PostgreSql.Parse("EXPLAIN ANALYZE SELECT a FROM t");

        Assert.Throws<NotSupportedException>(() => document.ToSql(SqlDialects.TSql));
        Assert.Equal(
            "SELECT a FROM t",
            document.ToSql(
                SqlDialects.TSql,
                new SqlGenerationOptions { UnsupportedBehavior = UnsupportedSqlBehavior.Ignore }));
    }

    [Theory]
    [InlineData(ViewSecurity.Definer, "CREATE SQL SECURITY DEFINER VIEW v AS SELECT 1 AS a")]
    [InlineData(ViewSecurity.Invoker, "CREATE SQL SECURITY INVOKER VIEW v AS SELECT 1 AS a")]
    public void MySql_create_view_preserves_security(ViewSecurity security, string sql)
    {
        var document = SqlDialects.MySql.Parse(sql);
        var view = Assert.IsType<CreateViewStatement>(Assert.Single(document.Statements));

        Assert.Equal(security, view.Security);
        Assert.Equal(sql, document.ToSql(SqlDialects.MySql));
        Assert.Equal(sql, SqlDialects.MySql.Parse(document.ToSql(SqlDialects.MySql)).ToSql(SqlDialects.MySql));
    }

    [Theory]
    [InlineData("CHAR(10 CHAR)")]
    [InlineData("VARCHAR2(100 BYTE)")]
    [InlineData("NUMBER")]
    [InlineData("NUMBER(15, 2)")]
    [InlineData("NUMBER(5, -2)")]
    [InlineData("TIMESTAMP WITH TIME ZONE")]
    [InlineData("TIMESTAMP WITH LOCAL TIME ZONE")]
    [InlineData("INTERVAL YEAR(2) TO MONTH")]
    [InlineData("INTERVAL DAY(2) TO SECOND(6)")]
    [InlineData("LONG RAW")]
    [InlineData("ROWID")]
    public void Oracle_data_types_round_trip(string dataType)
    {
        var sql = $"CREATE TABLE sample (value {dataType})";
        var document = SqlDialects.Oracle.Parse(sql);

        Assert.Equal(sql, document.ToSql(SqlDialects.Oracle));
        document.Accept(new NoopVisitor());
        Assert.Same(document, document.Accept(new NoopRewriter()));
        if (dataType.Contains(" TO ", StringComparison.Ordinal))
        {
            Assert.NotNull(Assert.Single(document.FindAll<SqlDataType>()).IntervalEndField);
        }

        Assert.Equal(
            sql,
            SqlDialects.Oracle.Parse(document.ToSql(SqlDialects.Oracle)).ToSql(SqlDialects.Oracle));
    }

    [Theory]
    [InlineData("SELECT LEAST(a, b) FROM t", "SELECT MIN(a, b) FROM t")]
    [InlineData("SELECT GREATEST(a, b) FROM t", "SELECT MAX(a, b) FROM t")]
    [InlineData("SELECT JSON_AGG(name) FROM t", "SELECT JSON_GROUP_ARRAY(name) FROM t")]
    [InlineData("SELECT JSONB_AGG(name) FROM t", "SELECT JSON_GROUP_ARRAY(name) FROM t")]
    [InlineData("SELECT JSON_OBJECT_AGG(k, v) FROM t", "SELECT JSON_GROUP_OBJECT(k, v) FROM t")]
    [InlineData("SELECT JSON_BUILD_OBJECT('id', id) FROM t", "SELECT JSON_OBJECT('id', id) FROM t")]
    [InlineData("SELECT JSON_BUILD_ARRAY(a, b) FROM t", "SELECT JSON_ARRAY(a, b) FROM t")]
    [InlineData("SELECT DATE_PART('year', ts) FROM t", "SELECT CAST(STRFTIME('%Y', ts) AS INTEGER) FROM t")]
    [InlineData("SELECT DATE_PART('second', ts) FROM t", "SELECT CAST(STRFTIME('%f', ts) AS REAL) FROM t")]
    [InlineData("SELECT EXTRACT(DOY FROM ts) FROM t", "SELECT CAST(STRFTIME('%j', ts) AS INTEGER) FROM t")]
    [InlineData("SELECT EXTRACT(EPOCH FROM ts) FROM t", "SELECT CAST(STRFTIME('%s', ts) AS REAL) FROM t")]
    [InlineData("SELECT DATE_TRUNC('month', ts) FROM t", "SELECT STRFTIME('%Y-%m-01', ts) FROM t")]
    [InlineData("SELECT DATE_TRUNC('day', ts) FROM t", "SELECT DATE(ts) FROM t")]
    public void PostgreSql_to_Sqlite_rewrites_relational_functions(string sql, string expected)
    {
        Assert.Equal(expected, SqlDialects.PostgreSql.Transpile(sql, SqlDialects.Sqlite));
    }

    [Fact]
    public void PostgreSql_to_TSql_rewrites_common_functions()
    {
        const string sql = """
            SELECT NOW(), CLOCK_TIMESTAMP(), LN(x), CHR(65), REPEAT('a', 2)
            """;

        Assert.Equal(
            "SELECT GETDATE(), SYSDATETIME(), LOG(x), CHAR(65), REPLICATE('a', 2)",
            SqlDialects.PostgreSql.Transpile(sql, SqlDialects.TSql));
    }

    private sealed class NoopVisitor : SqlVisitor;

    private sealed class NoopRewriter : SqlRewriter;
}
