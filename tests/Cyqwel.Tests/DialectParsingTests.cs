using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Parsing;

namespace Cyqwel.Tests;

public class DialectParsingTests
{
    [Fact]
    public void Enforces_identifier_and_string_quote_rules()
    {
        AssertParses(SqlDialects.TSql, "SELECT [name] FROM [users]");
        AssertRejects(SqlDialects.TSql, "SELECT `name` FROM `users`");

        AssertParses(SqlDialects.PostgreSql, "SELECT \"name\" FROM \"users\"");
        AssertRejects(SqlDialects.PostgreSql, "SELECT [name] FROM [users]");
        AssertRejects(SqlDialects.PostgreSql, "SELECT `name` FROM `users`");

        var mySql = SqlDialects.MySql.Parse("SELECT \"text\", `name` FROM `users`");
        var select = Assert.IsType<SelectStatement>(Assert.Single(mySql.Statements));
        Assert.IsType<LiteralExpression>(select.Projections[0].Expression);
        Assert.IsType<ColumnExpression>(select.Projections[1].Expression);

        AssertParses(SqlDialects.Sqlite, "SELECT [one], `two`, \"three\" FROM data");
    }

    [Fact]
    public void Reserves_keywords_per_dialect()
    {
        AssertParses(SqlDialects.MySql, "SELECT returning, ilike, nulls, top FROM data");
        AssertParses(SqlDialects.TSql, "SELECT returning FROM data");
        var exception = Assert.Throws<SqlParseException>(() =>
            SqlDialects.PostgreSql.Parse("SELECT returning FROM data"));
        Assert.Equal(SqlParseErrorCode.Syntax, exception.Error.Code);
    }

    [Fact]
    public void Enforces_row_limit_syntax()
    {
        AssertParses(SqlDialects.TSql, "SELECT TOP 5 id FROM users");
        AssertParses(
            SqlDialects.TSql,
            "SELECT id FROM users ORDER BY id OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY");
        AssertRejects(SqlDialects.TSql, "SELECT id FROM users LIMIT 5");

        AssertParses(SqlDialects.PostgreSql, "SELECT id FROM users LIMIT 5 OFFSET 10");
        AssertRejects(SqlDialects.PostgreSql, "SELECT TOP 5 id FROM users");
        AssertRejects(SqlDialects.PostgreSql, "SELECT id FROM users LIMIT 10, 5");

        AssertParses(SqlDialects.MySql, "SELECT id FROM users LIMIT 10, 5");
        AssertParses(SqlDialects.MySql, "SELECT id FROM users LIMIT 5 OFFSET 10");
        AssertRejects(
            SqlDialects.MySql,
            "SELECT id FROM users ORDER BY id OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY");
    }

    [Fact]
    public void Parses_complete_tsql_top_and_requires_order_by_for_offset()
    {
        var document = SqlDialects.TSql.Parse(
            "SELECT TOP (10) PERCENT WITH TIES id FROM users ORDER BY id");
        var select = Assert.IsType<SelectStatement>(Assert.Single(document.Statements));

        Assert.True(select.IsTopPercent);
        Assert.True(select.WithTies);
        Assert.Equal(
            "SELECT TOP (10) PERCENT WITH TIES id FROM users ORDER BY id",
            document.ToSql(SqlDialects.TSql));
        Assert.Throws<NotSupportedException>(() => document.ToSql(SqlDialects.PostgreSql));

        AssertRejects(SqlDialects.TSql, "SELECT id FROM users OFFSET 10 ROWS");
        AssertRejects(
            SqlDialects.TSql,
            "SELECT id FROM users UNION SELECT id FROM archived OFFSET 10 ROWS");

        var offsetOnly = SqlDialects.TSql.Parse(
            "SELECT id FROM users ORDER BY id OFFSET 10 ROWS");
        Assert.Equal(
            10L,
            Assert.IsType<LiteralExpression>(
                Assert.IsType<SelectStatement>(Assert.Single(offsetOnly.Statements)).Offset).Value);
    }

    [Fact]
    public void Enforces_returning_ilike_and_null_ordering()
    {
        const string returning = "UPDATE users SET name = 'Ada' WHERE id = 1 RETURNING id";
        AssertParses(SqlDialects.PostgreSql, returning);
        AssertParses(SqlDialects.Sqlite, returning);
        AssertRejects(SqlDialects.MySql, returning);
        AssertRejects(SqlDialects.TSql, returning);

        AssertParses(SqlDialects.PostgreSql, "SELECT name FROM users WHERE name ILIKE 'a%'");
        AssertRejects(SqlDialects.MySql, "SELECT name FROM users WHERE name ILIKE 'a%'");
        AssertRejects(SqlDialects.TSql, "SELECT name FROM users WHERE name ILIKE 'a%'");

        AssertParses(SqlDialects.PostgreSql, "SELECT id FROM users ORDER BY name NULLS LAST");
        AssertParses(SqlDialects.Sqlite, "SELECT id FROM users ORDER BY name NULLS LAST");
        AssertRejects(SqlDialects.MySql, "SELECT id FROM users ORDER BY name NULLS LAST");
        AssertRejects(SqlDialects.TSql, "SELECT id FROM users ORDER BY name NULLS LAST");
    }

    [Fact]
    public void Resolves_double_pipe_semantics_from_source_dialect()
    {
        var postgreSql = ParseProjection(SqlDialects.PostgreSql, "SELECT first_name || last_name");
        var sqlite = ParseProjection(SqlDialects.Sqlite, "SELECT first_name || last_name");
        var tSql = ParseProjection(SqlDialects.TSql, "SELECT first_name || last_name");
        var mySql = ParseProjection(SqlDialects.MySql, "SELECT first_name || last_name");

        Assert.Equal(BinaryOperator.Concatenate, Assert.IsType<BinaryExpression>(postgreSql).Operator);
        Assert.Equal(BinaryOperator.Concatenate, Assert.IsType<BinaryExpression>(sqlite).Operator);
        Assert.Equal(BinaryOperator.Concatenate, Assert.IsType<BinaryExpression>(tSql).Operator);
        Assert.Equal(BinaryOperator.Or, Assert.IsType<BinaryExpression>(mySql).Operator);
        Assert.Equal("SELECT first_name OR last_name", SqlDialects.MySql.Parse(
            "SELECT first_name || last_name").ToSql(SqlDialects.MySql));
    }

    [Fact]
    public void Enforces_parameter_styles()
    {
        AssertParses(SqlDialects.PostgreSql, "SELECT $1");
        AssertRejects(SqlDialects.PostgreSql, "SELECT @id");

        AssertParses(SqlDialects.TSql, "SELECT @id");
        AssertRejects(SqlDialects.TSql, "SELECT $1");

        AssertParses(SqlDialects.MySql, "SELECT ?");
        AssertRejects(SqlDialects.MySql, "SELECT @id");

        AssertParses(SqlDialects.Sqlite, "SELECT ?, @id, :name, $value");

        Assert.Equal("SELECT $1", SqlDialects.PostgreSql.Parse("SELECT $1").ToSql(SqlDialects.PostgreSql));
        Assert.Equal("SELECT @id", SqlDialects.TSql.Parse("SELECT @id").ToSql(SqlDialects.TSql));
        Assert.Equal("SELECT ?", SqlDialects.MySql.Parse("SELECT ?").ToSql(SqlDialects.MySql));
    }

    [Fact]
    public void TryParse_reports_dialect_incompatibility()
    {
        var parsed = SqlDialects.PostgreSql.TryParse(
            "SELECT TOP 5 id FROM users",
            out var document,
            out var error);

        Assert.False(parsed);
        Assert.Null(document);
        Assert.Equal(SqlParseErrorCode.DialectIncompatible, error!.Code);
        Assert.Contains("postgresql", error.Message);
    }

    [Fact]
    public void Custom_dialects_inherit_and_can_modify_parser_configuration()
    {
        var inherited = SqlDialectBuilder.Create("inherited-postgres")
            .BasedOn(SqlDialects.PostgreSql)
            .Build();
        var modified = SqlDialectBuilder.Create("modified-postgres")
            .BasedOn(SqlDialects.PostgreSql)
            .ConfigureParser(options => options with
            {
                IdentifierQuotes = options.IdentifierQuotes | SqlIdentifierQuoteStyle.Backtick,
            })
            .Build();

        AssertRejects(inherited, "SELECT `name` FROM `users`");
        AssertParses(modified, "SELECT `name` FROM `users`");
    }

    private static SqlExpression ParseProjection(SqlDialect dialect, string sql)
    {
        var document = dialect.Parse(sql);
        return Assert.IsType<SelectStatement>(Assert.Single(document.Statements))
            .Projections[0]
            .Expression;
    }

    private static void AssertParses(SqlDialect dialect, string sql) =>
        Assert.NotNull(dialect.Parse(sql));

    private static void AssertRejects(SqlDialect dialect, string sql)
    {
        var exception = Assert.Throws<SqlParseException>(() => dialect.Parse(sql));
        Assert.Equal(SqlParseErrorCode.DialectIncompatible, exception.Error.Code);
        Assert.Contains(dialect.Name, exception.Message);
    }
}
