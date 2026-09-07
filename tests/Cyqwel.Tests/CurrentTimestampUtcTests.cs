using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;
using Cyqwel.Parsing;
using Cyqwel.Validation;
using Cyqwel.Visitors;

namespace Cyqwel.Tests;

public class CurrentTimestampUtcTests
{
    [Theory]
    [InlineData("generic", "UTC_TIMESTAMP()")]
    [InlineData("generic", "UTC_TIMESTAMP")]
    [InlineData("generic", "GETUTCDATE()")]
    [InlineData("generic", "TIMEZONE('UTC', CURRENT_TIMESTAMP)")]
    [InlineData("generic", "SYS_EXTRACT_UTC(CURRENT_TIMESTAMP)")]
    [InlineData("generic", "SYS_EXTRACT_UTC(SYSTIMESTAMP)")]
    [InlineData("generic", "DATETIME('now')")]
    [InlineData("tsql", "getutcdate ( )")]
    [InlineData("mysql", "utc_timestamp")]
    [InlineData("mysql", "UTC_TIMESTAMP()")]
    [InlineData("postgresql", "TIMEZONE('UTC', CURRENT_TIMESTAMP)")]
    [InlineData("postgresql", "timezone('utc', now())")]
    [InlineData("oracle", "SYS_EXTRACT_UTC(CURRENT_TIMESTAMP)")]
    [InlineData("oracle", "sys_extract_utc(systimestamp)")]
    [InlineData("sqlite", "datetime('now')")]
    public void Native_utc_forms_parse_to_the_explicit_kind(string dialectName, string sql)
    {
        var timestamp = Assert.IsType<CurrentTimestampExpression>(
            ParseExpression(SqlDialectRegistry.Get(dialectName), sql));

        Assert.Equal(Sql.CurrentTimestamp(CurrentTimestampKind.Utc), timestamp);
    }

    [Theory]
    [InlineData("generic", "UTC_TIMESTAMP()")]
    [InlineData("tsql", "GETUTCDATE()")]
    [InlineData("postgresql", "TIMEZONE('UTC', CURRENT_TIMESTAMP)")]
    [InlineData("mysql", "UTC_TIMESTAMP()")]
    [InlineData("oracle", "SYS_EXTRACT_UTC(CURRENT_TIMESTAMP)")]
    [InlineData("sqlite", "DATETIME('now')")]
    public void Utc_generation_and_transpilation_preserve_the_kind(string dialectName, string expected)
    {
        var target = SqlDialectRegistry.Get(dialectName);
        var timestamp = Sql.CurrentTimestamp(CurrentTimestampKind.Utc);
        var query = Sql.SelectItems(new SelectItem(timestamp)).Build();

        Assert.Equal(expected, timestamp.ToSql(target));
        Assert.Equal(expected, new SqlGenerator(target).Generate(timestamp));
        Assert.Equal($"SELECT {expected}", query.ToSql(target));
        Assert.Equal(timestamp, ParseExpression(target, expected));

        var sources = new (SqlDialect Dialect, string Sql)[]
        {
            (SqlDialects.Generic, "UTC_TIMESTAMP()"),
            (SqlDialects.TSql, "GETUTCDATE()"),
            (SqlDialects.PostgreSql, "TIMEZONE('UTC', NOW())"),
            (SqlDialects.MySql, "UTC_TIMESTAMP"),
            (SqlDialects.Oracle, "SYS_EXTRACT_UTC(SYSTIMESTAMP)"),
            (SqlDialects.Sqlite, "DATETIME('now')"),
        };
        foreach (var (source, sql) in sources)
        {
            var generated = source.Transpile($"SELECT {sql}", target);
            Assert.Equal($"SELECT {expected}", generated);
            Assert.Equal(generated, target.Parse(generated).ToSql(target));
        }

        var inherited = SqlDialectBuilder.Create($"utc-{dialectName}").BasedOn(target).Build();
        Assert.Equal(expected, timestamp.ToSql(inherited));
        Assert.Equal(timestamp, ParseExpression(inherited, expected));
    }

    [Theory]
    [InlineData("tsql", "UTC_TIMESTAMP()")]
    [InlineData("postgresql", "GETUTCDATE()")]
    [InlineData("postgresql", "UTC_TIMESTAMP()")]
    [InlineData("mysql", "TIMEZONE('UTC', CURRENT_TIMESTAMP)")]
    [InlineData("sqlite", "TIMEZONE('UTC', CURRENT_TIMESTAMP)")]
    [InlineData("postgresql", "SYS_EXTRACT_UTC(CURRENT_TIMESTAMP)")]
    [InlineData("oracle", "DATETIME('now')")]
    [InlineData("mysql", "UTC_TIMESTAMP(3)")]
    [InlineData("tsql", "GETUTCDATE(3)")]
    [InlineData("mysql", "UTC_TIMESTAMP(DISTINCT)")]
    [InlineData("postgresql", "TIMEZONE('UTC', NOW()) FILTER (WHERE TRUE)")]
    [InlineData("tsql", "GETUTCDATE() OVER ()")]
    [InlineData("mysql", "`UTC_TIMESTAMP`()")]
    [InlineData("tsql", "[GETUTCDATE]()")]
    [InlineData("postgresql", "\"timezone\"('UTC', CURRENT_TIMESTAMP)")]
    [InlineData("oracle", "SYS_EXTRACT_UTC(\"SYSTIMESTAMP\")")]
    [InlineData("oracle", "SYS_EXTRACT_UTC(t.SYSTIMESTAMP)")]
    [InlineData("oracle", "SYS_EXTRACT_UTC(SYSDATE)")]
    [InlineData("oracle", "SYS_EXTRACT_UTC(created_at)")]
    [InlineData("postgresql", "TIMEZONE('America/New_York', CURRENT_TIMESTAMP)")]
    [InlineData("postgresql", "TIMEZONE(zone, CURRENT_TIMESTAMP)")]
    [InlineData("postgresql", "TIMEZONE('UTC', created_at)")]
    [InlineData("postgresql", "TIMEZONE('UTC', CURRENT_TIMESTAMP, 3)")]
    [InlineData("postgresql", "TIMEZONE('UTC', CURRENT_TIMESTAMP(3))")]
    [InlineData("oracle", "SYS_EXTRACT_UTC(CURRENT_TIMESTAMP(3))")]
    [InlineData("sqlite", "DATETIME('now', 'localtime')")]
    [InlineData("sqlite", "DATETIME('now', 'utc')")]
    [InlineData("sqlite", "DATETIME('now', 'subsec')")]
    [InlineData("sqlite", "DATETIME('2020-01-01')")]
    public void Only_known_unmodified_utc_shapes_are_normalized(string dialectName, string sql)
    {
        var expression = ParseExpression(SqlDialectRegistry.Get(dialectName), sql);

        Assert.False(expression is CurrentTimestampExpression);
        Assert.DoesNotContain(expression.FindAll<CurrentTimestampExpression>(),
            timestamp => timestamp.Kind == CurrentTimestampKind.Utc);
    }

    [Theory]
    [InlineData("postgresql", "TIMEZONE('UTC', TIMEZONE('UTC', CURRENT_TIMESTAMP))")]
    [InlineData("oracle", "SYS_EXTRACT_UTC(SYS_EXTRACT_UTC(CURRENT_TIMESTAMP))")]
    public void Converting_an_already_utc_timestamp_is_not_collapsed(string dialectName, string sql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var expression = Assert.IsType<FunctionCallExpression>(ParseExpression(dialect, sql));

        Assert.Equal(CurrentTimestampKind.Utc, Assert.Single(expression.FindAll<CurrentTimestampExpression>()).Kind);
        Assert.Equal(sql, expression.ToSql(dialect));
    }

    [Theory]
    [InlineData("generic", "UTC_TIMESTAMP(3)")]
    [InlineData("mysql", "UTC_TIMESTAMP(3)")]
    [InlineData("postgresql", "TIMEZONE('UTC', CURRENT_TIMESTAMP(3))")]
    [InlineData("oracle", "SYS_EXTRACT_UTC(CURRENT_TIMESTAMP(3))")]
    [InlineData("sqlite", "DATETIME('now', 'subsec')")]
    public void Explicit_precision_is_preserved_outside_the_utc_node(string dialectName, string sql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var expression = Assert.IsType<FunctionCallExpression>(ParseExpression(dialect, sql));

        Assert.Equal(sql, expression.ToSql(dialect));
    }

    [Theory]
    [InlineData("tsql", "UTC_TIMESTAMP(3)")]
    [InlineData("tsql", "GETUTCDATE(3)")]
    [InlineData("postgresql", "UTC_TIMESTAMP(3)")]
    [InlineData("oracle", "UTC_TIMESTAMP(3)")]
    [InlineData("sqlite", "UTC_TIMESTAMP(3)")]
    public void Unsupported_utc_precision_is_not_silently_dropped(string dialectName, string sql)
    {
        var target = SqlDialectRegistry.Get(dialectName);
        var expression = ParseExpression(SqlDialects.Generic, sql);

        Assert.Throws<NotSupportedException>(() => expression.ToSql(target));
        Assert.Equal(sql, expression.ToSql(target, new SqlGenerationOptions
        {
            UnsupportedBehavior = UnsupportedSqlBehavior.Ignore,
        }));
    }

    [Theory]
    [InlineData("mysql", "`UTC_TIMESTAMP`")]
    [InlineData("mysql", "t.UTC_TIMESTAMP")]
    [InlineData("mysql", "`UTC_TIMESTAMP`()")]
    [InlineData("tsql", "[GETUTCDATE]()")]
    [InlineData("tsql", "GETUTCDATE")]
    [InlineData("postgresql", "UTC_TIMESTAMP")]
    public void Utc_identifier_lookalikes_round_trip(string dialectName, string sql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var expression = ParseExpression(dialect, sql);

        Assert.False(expression is CurrentTimestampExpression);
        Assert.Equal(sql, expression.ToSql(dialect));
    }

    [Fact]
    public void Explicit_utc_timestamp_columns_are_quoted_only_where_needed()
    {
        Assert.Equal("\"UTC_TIMESTAMP\"", Sql.Col("UTC_TIMESTAMP").ToSql());
        Assert.Equal("`UTC_TIMESTAMP`", Sql.Col("UTC_TIMESTAMP").ToSql(SqlDialects.MySql));
        Assert.Equal("UTC_TIMESTAMP", Sql.Col("UTC_TIMESTAMP").ToSql(SqlDialects.PostgreSql));
        var update = Sql.Update("events").Set("UTC_TIMESTAMP", Sql.CurrentTimestamp(CurrentTimestampKind.Utc)).Build();
        var generated = update.ToSql(SqlDialects.MySql);
        var assignment = Assert.Single(SqlDialects.MySql.Parse(generated).FindAll<Assignment>());

        Assert.True(assignment.Column.Parts[0].IsQuoted);
        Assert.Equal(CurrentTimestampKind.Utc, Assert.IsType<CurrentTimestampExpression>(assignment.Value).Kind);
    }

    [Fact]
    public void Utc_parser_capabilities_are_part_of_the_cache_key()
    {
        var withoutUtc = SqlDialectBuilder.Create("without-utc")
            .BasedOn(SqlDialects.MySql)
            .ConfigureParser(options => options with
            {
                CurrentTimestampSyntax = options.CurrentTimestampSyntax & ~SqlCurrentTimestampSyntax.UtcTimestamp,
            })
            .Build();
        var withoutWrapper = SqlDialectBuilder.Create("without-utc-wrapper")
            .BasedOn(SqlDialects.PostgreSql)
            .ConfigureParser(options => options with { CurrentTimestampSyntax = SqlCurrentTimestampSyntax.Now })
            .Build();

        Assert.IsType<FunctionCallExpression>(ParseExpression(withoutUtc, "UTC_TIMESTAMP()"));
        Assert.IsType<ColumnExpression>(ParseExpression(withoutUtc, "UTC_TIMESTAMP"));
        Assert.IsType<CurrentTimestampExpression>(ParseExpression(SqlDialects.MySql, "UTC_TIMESTAMP()"));
        Assert.IsType<FunctionCallExpression>(ParseExpression(withoutWrapper, "TIMEZONE('UTC', NOW())"));
        Assert.IsType<CurrentTimestampExpression>(ParseExpression(SqlDialects.PostgreSql, "TIMEZONE('UTC', NOW())"));
    }

    [Theory]
    [InlineData("generic", "utc_timestamp()")]
    [InlineData("tsql", "getutcdate()")]
    [InlineData("postgresql", "timezone('UTC', current_timestamp)")]
    [InlineData("mysql", "utc_timestamp()")]
    [InlineData("oracle", "sys_extract_utc(current_timestamp)")]
    [InlineData("sqlite", "datetime('now')")]
    public void Utc_rendering_respects_casing(string dialectName, string expected)
    {
        var options = new SqlGenerationOptions
        {
            UppercaseKeywords = false,
            FunctionNameCase = FunctionNameCase.Lower,
        };
        Assert.Equal(expected, Sql.CurrentTimestamp(CurrentTimestampKind.Utc)
            .ToSql(SqlDialectRegistry.Get(dialectName), options));
    }

    [Fact]
    public void Utc_kind_is_preserved_by_visitors_and_transforms()
    {
        var timestamp = Sql.CurrentTimestamp(CurrentTimestampKind.Utc);
        var document = Sql.SelectItems(new SelectItem(timestamp)).Build();
        var visitor = new TimestampVisitor();
        document.Accept(visitor);

        Assert.Equal(CurrentTimestampKind.Utc, Assert.Single(visitor.Kinds));
        Assert.Same(document, document.Accept(new IdentityRewriter()));
        Assert.Empty(timestamp.Descendants());
        var dialect = SqlDialectBuilder.Create("utc-override")
            .WithNodeTransform(node => node is CurrentTimestampExpression { Kind: CurrentTimestampKind.Utc }
                ? Sql.Lit(1) : node)
            .Build();
        Assert.Equal("SELECT 1", document.ToSql(dialect));
        Assert.Equal("SELECT CURRENT_TIMESTAMP", Sql.Select(Sql.CurrentTimestamp()).ToSql(dialect));
    }

    [Theory]
    [InlineData("tsql", "GETUTCDATE()")]
    [InlineData("postgresql", "TIMEZONE('UTC', CURRENT_TIMESTAMP)")]
    [InlineData("mysql", "UTC_TIMESTAMP()")]
    [InlineData("oracle", "SYS_EXTRACT_UTC(CURRENT_TIMESTAMP)")]
    [InlineData("sqlite", "DATETIME('now')")]
    public void Utc_expressions_have_timestamp_types_and_work_in_defaults(string dialectName, string sql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var catalog = new SqlSchemaCatalog(new SqlTableSchema("events",
        [
            new("created_at", "timestamp"),
            new("count", "integer"),
        ]));
        var options = new SqlSchemaValidationOptions { CheckTypes = true };

        Assert.True(SqlValidator.Validate($"UPDATE events SET created_at = {sql}", catalog, dialect, options).IsValid);
        Assert.Contains(SqlValidator.Validate($"UPDATE events SET count = {sql}", catalog, dialect, options).Diagnostics,
            diagnostic => diagnostic.Code == SqlValidationCodes.InvalidAssignmentType);
        var document = dialect.Parse($"CREATE TABLE events (created_at TIMESTAMP DEFAULT ({sql}))");
        Assert.Equal(CurrentTimestampKind.Utc, Assert.Single(document.FindAll<CurrentTimestampExpression>()).Kind);
        var generated = document.ToSql(dialect);
        Assert.Equal(generated, dialect.Parse(generated).ToSql(dialect));
    }

    [Fact]
    public void Undefined_kinds_are_rejected()
    {
        var invalid = (CurrentTimestampKind)(-1);
        Assert.Throws<ArgumentOutOfRangeException>(() => Sql.CurrentTimestamp(invalid));
        foreach (var dialect in SqlDialects.BuiltIn)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CurrentTimestampExpression(invalid).ToSql(dialect));
        }
    }

    private static SqlExpression ParseExpression(SqlDialect dialect, string sql) =>
        Assert.IsType<SelectStatement>(dialect.Parse($"SELECT {sql}").Statements[0]).Projections[0].Expression;

    private sealed class IdentityRewriter : SqlRewriter;

    private sealed class TimestampVisitor : SqlVisitor
    {
        public List<CurrentTimestampKind> Kinds { get; } = [];

        protected override void VisitCurrentTimestamp(CurrentTimestampExpression node) => Kinds.Add(node.Kind);
    }
}
