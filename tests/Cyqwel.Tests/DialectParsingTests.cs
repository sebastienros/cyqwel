using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Parsing;
using Cyqwel.Visitors;

namespace Cyqwel.Tests;

public class DialectParsingTests
{
    [Theory]
    [InlineData("generic")]
    [InlineData("tsql")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("oracle")]
    public void Preserves_national_string_literals(string dialectName)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        foreach (var value in new[] { "ação", "", "d'água", "日本語", @"a\b", @"a\'b" })
        {
            var escaped = value.Replace("'", "''");
            if (dialect.ParserOptions.SupportsBackslashStringEscapes)
            {
                escaped = escaped.Replace("\\", "\\\\");
            }
            var quoted = $"'{escaped}'";
            foreach (var prefix in new[] { "N", "n" })
            {
                var document = dialect.Parse($"SELECT {prefix}{quoted}");
                var select = Assert.IsType<SelectStatement>(Assert.Single(document.Statements));
                var projection = Assert.Single(select.Projections);

                var literal = Assert.IsType<LiteralExpression>(projection.Expression);
                Assert.Equal(value, literal.Value);
                Assert.True(literal.IsNational);
                Assert.Null(projection.Alias);
                Assert.Equal($"SELECT N{quoted}", document.ToSql(dialect));
                Assert.Equal($"SELECT N{quoted}", dialect.Parse(document.ToSql(dialect)).ToSql(dialect));
            }
        }
    }

    [Theory]
    [InlineData("SELECT N'ação' AS name")]
    [InlineData("SELECT N'ação' name", "SELECT N'ação' AS name")]
    [InlineData("SELECT COALESCE(N'ação', N'')")]
    [InlineData("SELECT name FROM users WHERE name = N'ação'")]
    [InlineData("INSERT INTO users (name) VALUES (N'ação')")]
    [InlineData("SELECT TRIM(N'á' FROM name)")]
    public void Parses_national_strings_in_expressions(string sql, string? expected = null)
    {
        Assert.Equal(expected ?? sql, SqlDialects.TSql.Parse(sql).ToSql(SqlDialects.TSql));
    }

    [Theory]
    [InlineData("SELECT 1 AS N'alias'", "SELECT 1 AS alias", "alias")]
    [InlineData("SELECT 1 N'alias'", "SELECT 1 AS alias", "alias")]
    [InlineData("SELECT 1 AS n'alias'", "SELECT 1 AS alias", "alias")]
    [InlineData("SELECT 1 n'alias'", "SELECT 1 AS alias", "alias")]
    [InlineData("SELECT N'foo' AS N'alias'", "SELECT N'foo' AS alias", "alias", true)]
    [InlineData("SELECT N'foo' N'alias'", "SELECT N'foo' AS alias", "alias", true)]
    [InlineData("SELECT 1 AS N'display name'", "SELECT 1 AS [display name]", "display name")]
    [InlineData("SELECT 1 AS N'd''água'", "SELECT 1 AS [d'água]", "d'água")]
    [InlineData("SELECT 1 AS N'a]b'", "SELECT 1 AS [a]]b]", "a]b")]
    [InlineData("SELECT 1 AS N'FROM'", "SELECT 1 AS [FROM]", "FROM")]
    [InlineData("SELECT 1 AS N", "SELECT 1 AS N", "N")]
    [InlineData("SELECT 1 N", "SELECT 1 AS N", "N")]
    [InlineData("SELECT 1 AS [N]", "SELECT 1 AS [N]", "N")]
    [InlineData("SELECT 1 AS 'alias'", "SELECT 1 AS alias", "alias")]
    public void Parses_tsql_national_string_column_aliases(
        string sql,
        string expected,
        string expectedAlias,
        bool expressionIsNational = false)
    {
        var document = SqlDialects.TSql.Parse(sql);
        var select = Assert.IsType<SelectStatement>(Assert.Single(document.Statements));
        var projection = Assert.Single(select.Projections);

        Assert.Equal(expectedAlias, projection.Alias!.Value);
        Assert.Equal(expressionIsNational, Assert.IsType<LiteralExpression>(projection.Expression).IsNational);
        Assert.Equal(expected, document.ToSql(SqlDialects.TSql));
        Assert.Equal(expected, SqlDialects.TSql.Parse(document.ToSql(SqlDialects.TSql)).ToSql(SqlDialects.TSql));
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("oracle")]
    [InlineData("sqlite")]
    public void National_string_aliases_require_dialect_support(string dialectName)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);

        AssertRejects(dialect, "SELECT 1 AS N'alias'");
        AssertRejects(dialect, "SELECT 1 N'alias'");
    }

    [Theory]
    [InlineData("SELECT 1 AS N 'alias'")]
    [InlineData("SELECT 1 N /* comment */ 'alias'")]
    [InlineData("SELECT * FROM users AS N'alias'")]
    [InlineData("SELECT * FROM (SELECT 1 AS value) N'alias'")]
    public void National_aliases_require_adjacent_quotes_and_a_column_alias_context(string sql)
    {
        Assert.Throws<SqlParseException>(() => SqlDialects.TSql.Parse(sql));
    }

    [Theory]
    [InlineData("SELECT DATE_ADD(created_at, INTERVAL N'1' DAY) FROM events", "1")]
    [InlineData("SELECT DATE_SUB(created_at, INTERVAL N'1' DAY) FROM events", "1")]
    [InlineData("SELECT created_at + INTERVAL N'1' DAY FROM events", "1")]
    [InlineData("SELECT created_at - INTERVAL N'1' DAY FROM events", "1")]
    [InlineData("SELECT INTERVAL N'1' DAY + created_at FROM events", "1")]
    [InlineData("SELECT created_at + INTERVAL (N'1') DAY FROM events", "1")]
    [InlineData("SELECT created_at + INTERVAL N'1 02:03:04' DAY_SECOND FROM events", "1 02:03:04")]
    [InlineData("SELECT DATE_ADD(created_at, INTERVAL n'1' DAY) FROM events", "1",
        "SELECT DATE_ADD(created_at, INTERVAL N'1' DAY) FROM events")]
    [InlineData("SELECT DATE_ADD(created_at, INTERVAL n\"1\" DAY) FROM events", "1",
        "SELECT DATE_ADD(created_at, INTERVAL N'1' DAY) FROM events")]
    public void Parses_mysql_national_string_interval_values(string sql, string expectedValue, string? expected = null)
    {
        var document = SqlDialects.MySql.Parse(sql);
        var interval = Assert.Single(document.FindAll<IntervalExpression>());
        var literal = Assert.Single(interval.FindAll<LiteralExpression>());

        Assert.Equal(expectedValue, literal.Value);
        Assert.True(literal.IsNational);
        Assert.Equal(expected ?? sql, document.ToSql(SqlDialects.MySql));
        Assert.Equal(expected ?? sql, SqlDialects.MySql.Parse(document.ToSql(SqlDialects.MySql)).ToSql(SqlDialects.MySql));
    }

    [Theory]
    [InlineData("SELECT created_at + INTERVAL day_count DAY FROM events")]
    [InlineData("SELECT created_at + INTERVAL (day_count + 1) DAY FROM events")]
    [InlineData("SELECT created_at + INTERVAL 1 + 2 DAY FROM events")]
    [InlineData("SELECT created_at + INTERVAL (N'1' + N'2') DAY + INTERVAL 1 HOUR FROM events")]
    public void Parses_complete_mysql_interval_value_expressions(string sql)
    {
        var document = SqlDialects.MySql.Parse(sql);

        Assert.Equal(sql, document.ToSql(SqlDialects.MySql));
        Assert.Equal(sql, SqlDialects.MySql.Parse(document.ToSql(SqlDialects.MySql)).ToSql(SqlDialects.MySql));
    }

    [Theory]
    [InlineData("SELECT created_at + INTERVAL N'1' DAY FROM events")]
    [InlineData("SELECT created_at + INTERVAL (N'1') DAY FROM events")]
    [InlineData("SELECT TIMESTAMPTZ N'2026-01-01 00:00:00+00'")]
    public void PostgreSql_rejects_national_strings_in_typed_literal_syntax(string sql)
    {
        Assert.Throws<SqlParseException>(() => SqlDialects.PostgreSql.Parse(sql));
    }

    [Theory]
    [InlineData("generic", "SELECT 1 AS N'alias'", "SELECT 1 AS alias")]
    [InlineData("tsql", "SELECT 1 N'alias'", "SELECT 1 AS alias")]
    [InlineData("generic", "SELECT created_at + INTERVAL N'1' DAY FROM events",
        "SELECT created_at + INTERVAL N'1' DAY FROM events")]
    [InlineData("mysql", "SELECT created_at + INTERVAL (N'1') DAY FROM events",
        "SELECT created_at + INTERVAL (N'1') DAY FROM events")]
    public void Dialects_and_derived_dialects_support_national_string_contexts(
        string dialectName,
        string sql,
        string expected)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var inherited = SqlDialectBuilder.Create($"inherited-{dialectName}").BasedOn(dialect).Build();

        Assert.Equal(expected, dialect.Parse(sql).ToSql(dialect));
        Assert.Equal(expected, inherited.Parse(sql).ToSql(inherited));
    }

    [Theory]
    [InlineData("tsql", "SELECT 1 AS N'alias'", "SELECT 1 AS 'alias'")]
    [InlineData("mysql", "SELECT created_at + INTERVAL N'1' DAY FROM events",
        "SELECT created_at + INTERVAL '1' DAY FROM events")]
    public void National_string_context_support_can_be_disabled(string dialectName, string sql, string ordinarySql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var withoutContexts = SqlDialectBuilder.Create($"without-contexts-{dialectName}")
            .BasedOn(dialect)
            .ConfigureParser(options => options with
            {
                SupportsNationalStringAliases = false,
                SupportsExpressionIntervalValues = false,
            })
            .Build();
        var withoutNationalStrings = SqlDialectBuilder.Create($"without-national-strings-{dialectName}")
            .BasedOn(dialect)
            .ConfigureParser(options => options with { SupportsNationalStringLiterals = false })
            .Build();

        AssertRejects(withoutContexts, sql);
        AssertRejects(withoutNationalStrings, sql);
        AssertParses(withoutContexts, ordinarySql);
        AssertParses(withoutNationalStrings, ordinarySql);
        Assert.Equal("SELECT N'foo'", withoutContexts.Parse("SELECT N'foo'").ToSql(withoutContexts));
    }

    [Theory]
    [InlineData("postgresql", "SELECT TRIM('a' || 'b' FROM name) FROM users")]
    [InlineData("postgresql", "SELECT TRIM(N'a' || N'b' FROM name) FROM users")]
    [InlineData("tsql", "SELECT TRIM(N'a' + N'b' FROM name) FROM users")]
    public void Parses_complete_trim_character_expressions(string dialectName, string sql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);

        Assert.Equal(sql, dialect.Parse(sql).ToSql(dialect));
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("tsql")]
    [InlineData("postgresql")]
    [InlineData("mysql")]
    [InlineData("oracle")]
    [InlineData("sqlite")]
    public void Preserves_columns_and_ordinary_strings(string dialectName)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        const string sql = "SELECT N, name AS N, N AS 'alias', 'ação' FROM users";

        Assert.Equal("SELECT N, name AS N, N AS alias, 'ação' FROM users", dialect.Parse(sql).ToSql(dialect));
    }

    [Fact]
    public void National_prefix_requires_an_adjacent_quote()
    {
        foreach (var sql in new[] { "SELECT N 'alias'", "SELECT N /* comment */ 'alias'", "SELECT [N]'alias'" })
        {
            var select = Assert.IsType<SelectStatement>(Assert.Single(SqlDialects.TSql.Parse(sql).Statements));
            var projection = Assert.Single(select.Projections);

            Assert.IsType<ColumnExpression>(projection.Expression);
            Assert.Equal("alias", projection.Alias!.Value);
        }
    }

    [Fact]
    public void MySql_supports_double_quoted_national_strings()
    {
        Assert.Equal("SELECT N'ação'", SqlDialects.MySql.Parse("SELECT n\"ação\"").ToSql(SqlDialects.MySql));
    }

    [Fact]
    public void Sqlite_uses_ordinary_strings_instead_of_national_strings()
    {
        Assert.Equal("SELECT N AS ação", SqlDialects.Sqlite.Parse("SELECT N'ação'").ToSql(SqlDialects.Sqlite));
        Assert.Equal("SELECT 'ação'", SqlDialects.TSql.Parse("SELECT N'ação'").ToSql(SqlDialects.Sqlite));
    }

    [Fact]
    public void Custom_dialects_inherit_national_string_support()
    {
        var dialect = SqlDialectBuilder.Create("custom-tsql").BasedOn(SqlDialects.TSql).Build();

        Assert.Equal("SELECT N'ação'", dialect.Parse("SELECT N'ação'").ToSql(dialect));
    }

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
        Assert.Equal(BinaryOperator.AnsiConcatenate, Assert.IsType<BinaryExpression>(tSql).Operator);
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
