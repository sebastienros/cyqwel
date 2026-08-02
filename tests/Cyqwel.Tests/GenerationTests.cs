using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;
using Cyqwel.Parsing;

namespace Cyqwel.Tests;

public class GenerationTests
{
    [Fact]
    public void Generates_generic_sql_from_builder()
    {
        var query = Sql.Select("u.id", "u.name")
            .From("users", "u")
            .Where(Sql.Col("u.age").GreaterThan(Sql.Lit(18)))
            .OrderBy(Sql.Col("u.name"))
            .Limit(10)
            .Build();

        Assert.Equal(
            "SELECT u.id, u.name FROM users AS u WHERE u.age > 18 ORDER BY u.name ASC LIMIT 10",
            query.ToSql());
    }

    [Fact]
    public void Generates_tsql_top_and_offset_fetch()
    {
        var top = Sql.Select("id").From("users").Limit(10).Build();
        var paged = Sql.Select("id")
            .From("users")
            .OrderBy(Sql.Col("id"))
            .Limit(10)
            .Offset(20)
            .Build();

        Assert.Equal("SELECT TOP (10) id FROM users", top.ToSql(SqlDialects.TSql));
        Assert.Equal(
            "SELECT id FROM users ORDER BY id ASC OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY",
            paged.ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Generates_mysql_limit_syntax()
    {
        var query = Sql.Select("id")
            .From("users")
            .Limit(10)
            .Offset(20)
            .Build();

        Assert.Equal("SELECT id FROM users LIMIT 20, 10", query.ToSql(SqlDialects.MySql));
    }

    [Fact]
    public void Transpiles_tsql_top_to_postgresql_limit()
    {
        Assert.Equal(
            "SELECT id FROM users LIMIT 10",
            SqlDialects.TSql.Transpile(
                "SELECT TOP 10 id FROM users",
                SqlDialects.PostgreSql));
    }

    [Fact]
    public void Rejects_unsupported_dialect_features_by_default()
    {
        var expression = Sql.Col("name").ILike(Sql.Lit("a%"));

        Assert.Throws<NotSupportedException>(() => expression.ToSql(SqlDialects.MySql));
        Assert.Equal(
            "name ILIKE 'a%'",
            expression.ToSql(
                SqlDialects.MySql,
                new SqlGenerationOptions { UnsupportedBehavior = UnsupportedSqlBehavior.Ignore }));
    }

    [Fact]
    public void Supports_custom_dialect_transforms()
    {
        var dialect = SqlDialectBuilder.Create("warehouse")
            .BasedOn(SqlDialects.PostgreSql)
            .WithFunctionNameTransform(name =>
                name.Equals("LEN", StringComparison.OrdinalIgnoreCase) ? "LENGTH" : name)
            .Build();

        var expression = Sql.Func("LEN", Sql.Col("name"));

        Assert.Equal("LENGTH(name)", expression.ToSql(dialect));
    }

    [Fact]
    public void Supports_complete_literal_and_function_rendering()
    {
        var dialect = SqlDialectBuilder.Create("application-tsql")
            .BasedOn(SqlDialects.TSql)
            .WithLiteralRenderer(static (literal, _) => literal.Value is string text
                ? $"N'{text.Replace("'", "''", StringComparison.Ordinal)}'"
                : null)
            .WithFunctionRenderer(static (function, renderExpression, _) =>
                function.Name.Value.Equals("NOW", StringComparison.OrdinalIgnoreCase)
                    ? "getUtcDate()"
                    : function.Name.Value.Equals("SECOND", StringComparison.OrdinalIgnoreCase)
                        ? $"datepart(second, {renderExpression(function.Arguments[0])})"
                        : null)
            .Build();

        Assert.Equal("N'a''b'", Sql.Lit("a'b").ToSql(dialect));
        Assert.Equal("getUtcDate()", Sql.Func("NOW").ToSql(dialect));
        Assert.Equal(
            "datepart(second, created_at)",
            Sql.Func("SECOND", Sql.Col("created_at")).ToSql(dialect));
        Assert.Equal("COUNT(*)", Sql.CountStar().ToSql(dialect));
    }

    [Fact]
    public void Generates_window_functions()
    {
        var document = SqlParser.Parse(
            "SELECT COUNT(1) OVER (), ROW_NUMBER() OVER (PARTITION BY region ORDER BY created_at DESC, id)");

        Assert.Equal(
            "SELECT COUNT(1) OVER (), ROW_NUMBER() OVER (PARTITION BY region ORDER BY created_at DESC, id)",
            document.ToSql());
    }

    [Fact]
    public void Inherited_dialects_preserve_concatenation_behavior()
    {
        var customMySql = SqlDialectBuilder.Create("custom-mysql")
            .BasedOn(SqlDialects.MySql)
            .Build();
        var customTSql = SqlDialectBuilder.Create("custom-tsql")
            .BasedOn(SqlDialects.TSql)
            .Build();
        var expression = new BinaryExpression(
            Sql.Col("first_name"),
            BinaryOperator.Concatenate,
            Sql.Col("last_name"));

        Assert.Equal("CONCAT(first_name, last_name)", expression.ToSql(customMySql));
        Assert.Equal("first_name + last_name", expression.ToSql(customTSql));
    }

    [Fact]
    public void Generates_dml_builders()
    {
        Assert.Equal(
            "INSERT INTO users (id, name) VALUES (1, 'Ada'), (2, 'Grace')",
            Sql.InsertInto("users")
                .Columns("id", "name")
                .Values(1, "Ada")
                .Values(2, "Grace")
                .ToSql());

        Assert.Equal(
            "UPDATE users SET name = 'Ada' WHERE id = 1",
            Sql.Update("users")
                .Set("name", "Ada")
                .Where(Sql.Col("id").EqualTo(Sql.Lit(1)))
                .ToSql());

        Assert.Equal(
            "DELETE FROM users WHERE id = 1",
            Sql.DeleteFrom("users")
                .Where(Sql.Col("id").EqualTo(Sql.Lit(1)))
                .ToSql());
    }

    [Fact]
    public void Generates_case_and_set_builders()
    {
        var category = Sql.Case()
            .When(Sql.Col("score").GreaterThanOrEqualTo(Sql.Lit(90)), "A")
            .Else("B")
            .Build();
        var union = Sql.Select(Sql.Col("id"), category)
            .From("current")
            .Union(Sql.Select("id", "grade").From("archived"), all: true)
            .OrderBy(Sql.Col("id"))
            .Limit(5);

        Assert.Equal(
            "SELECT id, CASE WHEN score >= 90 THEN 'A' ELSE 'B' END FROM current UNION ALL SELECT id, grade FROM archived ORDER BY id ASC LIMIT 5",
            union.ToSql());
    }

    [Fact]
    public void Quotes_unsafe_identifiers_and_rejects_unsafe_parameters()
    {
        Assert.Equal(
            "SELECT \"name; DROP TABLE users\"",
            Sql.Select("name; DROP TABLE users").ToSql());
        Assert.Throws<InvalidOperationException>(() => Sql.Param("id; DROP").ToSql());
    }
}
