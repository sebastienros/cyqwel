using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;
using Cyqwel.Parsing;
using Cyqwel.Validation;
using Cyqwel.Visitors;

namespace Cyqwel.Tests;

public class CurrentTimestampTests
{
    [Theory]
    [InlineData("generic", "CURRENT_TIMESTAMP", false)]
    [InlineData("generic", "CURRENT_TIMESTAMP()", false)]
    [InlineData("generic", "GETDATE()", false)]
    [InlineData("generic", "NOW()", false)]
    [InlineData("generic", "SYSDATE", true)]
    [InlineData("tsql", "CURRENT_TIMESTAMP", false)]
    [InlineData("tsql", "getdate ( )", false)]
    [InlineData("postgresql", "current_timestamp", false)]
    [InlineData("postgresql", "now()", false)]
    [InlineData("mysql", "CURRENT_TIMESTAMP", false)]
    [InlineData("mysql", "current_timestamp()", false)]
    [InlineData("mysql", "NOW()", false)]
    [InlineData("oracle", "CURRENT_TIMESTAMP", false)]
    [InlineData("oracle", "sysdate", true)]
    [InlineData("sqlite", "CURRENT_TIMESTAMP", false)]
    public void Native_forms_parse_to_the_shared_node(string dialectName, string sql, bool systemDate)
    {
        var expression = ParseExpression(SqlDialectRegistry.Get(dialectName), sql);

        var timestamp = Assert.IsType<CurrentTimestampExpression>(expression);
        Assert.Equal(systemDate ? CurrentTimestampKind.SystemDate : CurrentTimestampKind.Default, timestamp.Kind);
        if (!systemDate)
        {
            Assert.Equal(Sql.CurrentTimestamp(), timestamp);
        }
    }

    [Theory]
    [InlineData("generic", "CURRENT_TIMESTAMP")]
    [InlineData("tsql", "GETDATE()")]
    [InlineData("postgresql", "CURRENT_TIMESTAMP")]
    [InlineData("mysql", "CURRENT_TIMESTAMP")]
    [InlineData("oracle", "CURRENT_TIMESTAMP")]
    [InlineData("sqlite", "CURRENT_TIMESTAMP")]
    public void Builders_and_parsed_timestamps_share_target_rendering(string dialectName, string expected)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var timestamp = Sql.CurrentTimestamp();
        var query = Sql.SelectItems(new SelectItem(timestamp)).Build();

        Assert.Equal(expected, timestamp.ToSql(dialect));
        Assert.Equal(expected, new SqlGenerator(dialect).Generate(timestamp));
        Assert.Equal($"SELECT {expected}", query.ToSql(dialect));
        Assert.Equal(timestamp, ParseExpression(dialect, expected));
        Assert.Equal(
            $"SELECT {expected}",
            SqlDialects.TSql.Transpile("SELECT GETDATE()", dialect));
        Assert.Equal(
            $"SELECT {expected}",
            SqlDialects.PostgreSql.Transpile("SELECT NOW()", dialect));
        Assert.Equal(
            $"SELECT {expected}",
            SqlDialects.MySql.Transpile("SELECT NOW()", dialect));
    }

    [Fact]
    public void Oracle_system_date_remains_distinct_without_adding_dual()
    {
        const string sql = "SELECT SYSDATE, CURRENT_TIMESTAMP, TRUNC(SYSDATE)";
        var document = SqlDialects.Oracle.Parse(sql);

        Assert.Equal(
            [CurrentTimestampKind.SystemDate, CurrentTimestampKind.Default, CurrentTimestampKind.SystemDate],
            document.FindAll<CurrentTimestampExpression>().Select(timestamp => timestamp.Kind));
        Assert.Equal(sql, document.ToSql(SqlDialects.Oracle));
        Assert.Equal(
            "SELECT CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, TRUNC(CURRENT_TIMESTAMP)",
            document.ToSql(SqlDialects.PostgreSql));
        Assert.Equal(
            "SELECT GETDATE(), GETDATE(), TRUNC(GETDATE())",
            document.ToSql(SqlDialects.TSql));
        Assert.Equal(
            "SELECT CURRENT_TIMESTAMP",
            Sql.Select(Sql.CurrentTimestamp()).ToSql(SqlDialects.Oracle));
        Assert.Equal(CurrentTimestampKind.SystemDate, Assert.IsType<CurrentTimestampExpression>(
            ParseExpression(SqlDialects.Oracle, "SYSDATE")).Kind);
    }

    [Theory]
    [InlineData("tsql", "NOW()")]
    [InlineData("postgresql", "GETDATE()")]
    [InlineData("mysql", "GETDATE()")]
    [InlineData("oracle", "NOW()")]
    [InlineData("oracle", "SYSDATE()")]
    [InlineData("sqlite", "NOW()")]
    [InlineData("tsql", "CURRENT_TIMESTAMP()")]
    [InlineData("postgresql", "CURRENT_TIMESTAMP()")]
    [InlineData("oracle", "CURRENT_TIMESTAMP()")]
    [InlineData("sqlite", "CURRENT_TIMESTAMP()")]
    [InlineData("generic", "my_now()")]
    [InlineData("generic", "NOW(3)")]
    [InlineData("generic", "GETDATE(1)")]
    [InlineData("generic", "CURRENT_TIMESTAMP(6)")]
    [InlineData("postgresql", "NOW(DISTINCT)")]
    [InlineData("postgresql", "NOW() FILTER (WHERE TRUE)")]
    [InlineData("postgresql", "NOW() WITHIN GROUP (ORDER BY id)")]
    [InlineData("postgresql", "NOW() OVER ()")]
    [InlineData("generic", "CURRENT_TIMESTAMP() OVER ()")]
    public void Other_calls_are_not_normalized(string dialectName, string sql)
    {
        var expression = ParseExpression(SqlDialectRegistry.Get(dialectName), sql);

        Assert.Empty(expression.FindAll<CurrentTimestampExpression>());
        Assert.Single(expression.FindAll<FunctionCallExpression>());
    }

    [Theory]
    [InlineData("generic", "NOW")]
    [InlineData("generic", "GETDATE")]
    [InlineData("generic", "CURRENT_TIMESTAMP_VALUE")]
    [InlineData("generic", "SYSDATE_VALUE")]
    [InlineData("postgresql", "SYSDATE")]
    [InlineData("tsql", "SYSDATE")]
    [InlineData("mysql", "SYSDATE")]
    [InlineData("sqlite", "SYSDATE")]
    [InlineData("oracle", "t.sysdate")]
    [InlineData("oracle", "t.current_timestamp")]
    [InlineData("postgresql", "t.current_timestamp")]
    [InlineData("oracle", "\"SYSDATE\"")]
    [InlineData("postgresql", "\"CURRENT_TIMESTAMP\"")]
    [InlineData("tsql", "[CURRENT_TIMESTAMP]")]
    [InlineData("mysql", "`CURRENT_TIMESTAMP`")]
    public void Identifier_lookalikes_remain_columns(string dialectName, string sql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var expression = Assert.IsType<ColumnExpression>(ParseExpression(dialect, sql));

        Assert.Equal(sql, expression.ToSql(dialect));
        Assert.IsType<ColumnExpression>(ParseExpression(dialect, expression.ToSql(dialect)));
    }

    [Theory]
    [InlineData("postgresql", "\"now\"()")]
    [InlineData("postgresql", "\"CURRENT_TIMESTAMP\"()")]
    [InlineData("tsql", "[GETDATE]()")]
    [InlineData("mysql", "`NOW`()")]
    [InlineData("oracle", "\"NOW\"()")]
    [InlineData("sqlite", "\"CURRENT_TIMESTAMP\"(3)")]
    public void Quoted_calls_preserve_their_names_and_arguments(string dialectName, string sql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var expression = Assert.IsType<FunctionCallExpression>(ParseExpression(dialect, sql));

        Assert.True(expression.Name.IsQuoted);
        Assert.Equal(sql, expression.ToSql(dialect));
        Assert.IsType<FunctionCallExpression>(ParseExpression(dialect, expression.ToSql(dialect)));
    }

    [Theory]
    [InlineData("generic", "CURRENT_TIMESTAMP")]
    [InlineData("tsql", "CURRENT_TIMESTAMP")]
    [InlineData("oracle", "SYSDATE")]
    [InlineData("mysql", "CURRENT_TIMESTAMP")]
    public void Explicit_column_builders_do_not_turn_into_timestamps(string dialectName, string name)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var sql = Sql.Col(name).ToSql(dialect);

        Assert.IsType<ColumnExpression>(ParseExpression(dialect, sql));
    }

    [Theory]
    [InlineData("generic", "UPDATE events SET current_timestamp = CURRENT_TIMESTAMP")]
    [InlineData("oracle", "UPDATE events SET sysdate = SYSDATE")]
    [InlineData("generic", "MERGE INTO events t USING incoming s ON t.id = s.id "
        + "WHEN MATCHED THEN UPDATE SET current_timestamp = CURRENT_TIMESTAMP")]
    public void Assignment_targets_are_identifiers_not_timestamp_expressions(string dialectName, string sql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var document = dialect.Parse(sql);
        var assignment = Assert.Single(document.FindAll<Assignment>());

        Assert.IsType<CurrentTimestampExpression>(assignment.Value);
        var generated = document.ToSql(dialect);
        var reparsed = Assert.Single(dialect.Parse(generated).FindAll<Assignment>());
        Assert.Equal(assignment.Column.Parts[0].Value, reparsed.Column.Parts[0].Value);
        Assert.IsType<CurrentTimestampExpression>(reparsed.Value);
    }

    [Theory]
    [InlineData("generic", "CURRENT_TIMESTAMP(6)")]
    [InlineData("postgresql", "CURRENT_TIMESTAMP(3)")]
    [InlineData("mysql", "CURRENT_TIMESTAMP(6)")]
    [InlineData("mysql", "NOW(3)")]
    [InlineData("oracle", "CURRENT_TIMESTAMP(6)")]
    public void Precision_calls_keep_their_arguments(string dialectName, string sql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var expression = Assert.IsType<FunctionCallExpression>(ParseExpression(dialect, sql));

        Assert.Single(expression.Arguments);
        Assert.Equal(sql, expression.ToSql(dialect));
    }

    [Theory]
    [InlineData("tsql", "CURRENT_TIMESTAMP(3)")]
    [InlineData("tsql", "NOW(3)")]
    [InlineData("tsql", "GETDATE(3)")]
    [InlineData("sqlite", "CURRENT_TIMESTAMP(3)")]
    [InlineData("sqlite", "NOW(3)")]
    [InlineData("oracle", "NOW(3)")]
    [InlineData("postgresql", "NOW(3)")]
    public void Unsupported_timestamp_arguments_are_not_silently_discarded(string dialectName, string sql)
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
    [InlineData("generic", "CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP")]
    [InlineData("tsql", "NOW", "GETDATE()")]
    [InlineData("tsql", "CURRENT_TIMESTAMP", "GETDATE()")]
    [InlineData("sqlite", "NOW", "CURRENT_TIMESTAMP")]
    [InlineData("sqlite", "CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP")]
    [InlineData("oracle", "NOW", "CURRENT_TIMESTAMP")]
    [InlineData("oracle", "CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP")]
    public void Legacy_timestamp_function_rendering_uses_the_same_target_syntax(
        string dialectName, string functionName, string expected)
    {
        Assert.Equal(expected, Sql.Func(functionName).ToSql(SqlDialectRegistry.Get(dialectName)));
    }

    [Fact]
    public void Timestamps_work_in_nested_expressions_and_defaults()
    {
        var document = SqlDialects.PostgreSql.Parse(
            "CREATE TABLE events (created_at TIMESTAMP DEFAULT NOW()); "
            + "SELECT COALESCE(created_at, NOW()) FROM events WHERE created_at < CURRENT_TIMESTAMP");

        Assert.Equal(3, document.FindAll<CurrentTimestampExpression>().Count());
        Assert.DoesNotContain(document.GetColumnNames(),
            name => name.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase));
        var generated = document.ToSql(SqlDialects.Oracle);
        Assert.Contains("DEFAULT CURRENT_TIMESTAMP", generated);
        Assert.Contains("NVL(created_at, CURRENT_TIMESTAMP)", generated);
        Assert.Equal(generated, SqlDialects.Oracle.Parse(generated).ToSql(SqlDialects.Oracle));
    }

    [Theory]
    [InlineData("tsql", "SYSUTCDATETIME()")]
    [InlineData("tsql", "SYSDATETIME()")]
    [InlineData("tsql", "SYSDATETIMEOFFSET()")]
    [InlineData("postgresql", "CLOCK_TIMESTAMP()")]
    [InlineData("postgresql", "STATEMENT_TIMESTAMP()")]
    [InlineData("postgresql", "LOCALTIMESTAMP")]
    [InlineData("mysql", "SYSDATE()")]
    [InlineData("oracle", "SYSTIMESTAMP")]
    [InlineData("oracle", "CURRENT_DATE")]
    public void Other_temporal_expressions_are_unchanged(string dialectName, string sql)
    {
        var dialect = SqlDialectRegistry.Get(dialectName);
        var expression = ParseExpression(dialect, sql);

        Assert.Empty(expression.FindAll<CurrentTimestampExpression>());
        Assert.Equal(sql, expression.ToSql(dialect));
    }

    [Theory]
    [InlineData("generic", "CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP")]
    [InlineData("tsql", "GETDATE()", "GETDATE()")]
    [InlineData("postgresql", "NOW()", "CURRENT_TIMESTAMP")]
    [InlineData("mysql", "NOW()", "CURRENT_TIMESTAMP")]
    [InlineData("oracle", "SYSDATE", "SYSDATE")]
    [InlineData("sqlite", "CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP")]
    public void Custom_dialects_inherit_parsing_and_rendering(string dialectName, string input, string expected)
    {
        var dialect = SqlDialectBuilder.Create($"custom-{dialectName}")
            .BasedOn(SqlDialectRegistry.Get(dialectName))
            .Build();
        var timestamp = Assert.IsType<CurrentTimestampExpression>(ParseExpression(dialect, input));

        Assert.Equal(expected, timestamp.ToSql(dialect));
    }

    [Fact]
    public void Parser_cache_distinguishes_timestamp_capabilities()
    {
        var withoutNow = SqlDialectBuilder.Create("without-now")
            .BasedOn(SqlDialects.PostgreSql)
            .ConfigureParser(options => options with { CurrentTimestampSyntax = SqlCurrentTimestampSyntax.None })
            .Build();
        var withGetDate = SqlDialectBuilder.Create("with-getdate")
            .BasedOn(SqlDialects.PostgreSql)
            .ConfigureParser(options => options with { CurrentTimestampSyntax = SqlCurrentTimestampSyntax.GetDate })
            .Build();

        Assert.IsType<FunctionCallExpression>(ParseExpression(withoutNow, "NOW()"));
        Assert.IsType<CurrentTimestampExpression>(ParseExpression(SqlDialects.PostgreSql, "NOW()"));
        Assert.IsType<CurrentTimestampExpression>(ParseExpression(withGetDate, "GETDATE()"));
        Assert.IsType<FunctionCallExpression>(ParseExpression(SqlDialects.PostgreSql, "GETDATE()"));
        Assert.IsType<FunctionCallExpression>(ParseExpression(withoutNow, "NOW()"));
    }

    [Fact]
    public void Semantic_rendering_can_be_overridden_and_generic_function_hooks_remain_available()
    {
        var dialect = SqlDialectBuilder.Create("custom-clock")
            .BasedOn(new ClockDialect())
            .WithFunctionRenderer((function, _, _) =>
                function.Name.Value == "NOW" ? "custom_now()" : null)
            .Build();

        Assert.Equal("custom_clock()", Sql.CurrentTimestamp().ToSql(dialect));
        Assert.Equal("custom_now()", Sql.Func("NOW").ToSql(dialect));
        Assert.Equal("NOW()", Sql.Func("NOW").ToSql(SqlDialects.Generic));

        var transformed = SqlDialectBuilder.Create("fixed-clock")
            .WithNodeTransform(node => node is CurrentTimestampExpression ? Sql.Lit(42) : node)
            .Build();
        Assert.Equal("SELECT 42", Sql.Select(Sql.CurrentTimestamp()).ToSql(transformed));
    }

    [Fact]
    public void Timestamp_rendering_respects_casing_and_pretty_print_options()
    {
        var options = new SqlGenerationOptions
        {
            UppercaseKeywords = false,
            FunctionNameCase = FunctionNameCase.Lower,
            PrettyPrint = true,
        };
        var query = Sql.SelectItems(new SelectItem(Sql.CurrentTimestamp(), "created_at")).Build();

        Assert.Equal("current_timestamp", Sql.CurrentTimestamp().ToSql(SqlDialects.PostgreSql, options));
        Assert.Equal("getdate()", Sql.CurrentTimestamp().ToSql(SqlDialects.TSql, options));
        Assert.Equal("sysdate", Sql.CurrentTimestamp(CurrentTimestampKind.SystemDate).ToSql(SqlDialects.Oracle, options));
        Assert.Contains("current_timestamp as created_at", query.ToSql(SqlDialects.PostgreSql, options));
        Assert.Equal("GETDATE()", Sql.CurrentTimestamp().ToSql(SqlDialects.TSql, new SqlGenerationOptions
        {
            UppercaseKeywords = false,
            FunctionNameCase = FunctionNameCase.Preserve,
        }));
    }

    [Fact]
    public void Timestamps_are_leaf_nodes_with_typed_visiting_and_rewriting()
    {
        var timestamp = Sql.CurrentTimestamp() with { Span = new SqlTextSpan(7, 17) };
        var query = Sql.Select(timestamp).Build();
        var visitor = new TimestampVisitor();
        query.Accept(visitor);

        Assert.Equal(1, visitor.Count);
        Assert.Empty(timestamp.Descendants());
        Assert.Same(timestamp, Assert.Single(timestamp.BreadthFirst()));
        Assert.Same(query, query.Accept(new IdentityRewriter()));

        var rewritten = Assert.IsType<SelectStatement>(query.Accept(new TimestampRewriter()));
        var literal = Assert.IsType<LiteralExpression>(rewritten.Projections[0].Expression);
        Assert.Equal(timestamp.Span, literal.Span);
        Assert.Equal(1, literal.Value);
        Assert.Same(timestamp, query.Projections[0].Expression);
    }

    [Fact]
    public void Schema_validation_recognizes_timestamps_without_column_lookup()
    {
        var catalog = new SqlSchemaCatalog(new SqlTableSchema("events",
        [
            new("created_at", "timestamp"),
            new("count", "integer"),
        ]));
        var options = new SqlSchemaValidationOptions { CheckTypes = true };
        var valid = SqlValidator.Validate(
            "UPDATE events SET created_at = NOW()",
            catalog,
            SqlDialects.PostgreSql,
            options);
        Assert.True(valid.IsValid, string.Join(Environment.NewLine, valid.Diagnostics));

        var invalid = SqlValidator.Validate(
            "UPDATE events SET count = CURRENT_TIMESTAMP",
            catalog,
            options: options);
        Assert.Contains(invalid.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidAssignmentType);

        var predicate = SqlValidator.Validate(
            "SELECT created_at FROM events WHERE SYSDATE",
            catalog,
            SqlDialects.Oracle,
            options);
        Assert.Contains(predicate.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidPredicateType);
    }

    private static SqlExpression ParseExpression(SqlDialect dialect, string sql) =>
        Assert.IsType<SelectStatement>(dialect.Parse($"SELECT {sql}").Statements[0])
            .Projections[0].Expression;

    private sealed class ClockDialect() : SqlDialect("clock")
    {
        public override string RenderCurrentTimestamp(
            CurrentTimestampExpression timestamp,
            SqlGenerationOptions options) => "custom_clock()";
    }

    private sealed class TimestampVisitor : SqlVisitor
    {
        public int Count { get; private set; }

        protected override void VisitCurrentTimestamp(CurrentTimestampExpression node)
        {
            Count++;
            base.VisitCurrentTimestamp(node);
        }
    }

    private sealed class TimestampRewriter : SqlRewriter
    {
        protected override SqlNode VisitCurrentTimestamp(CurrentTimestampExpression node) =>
            Sql.Lit(1) with { Span = node.Span };
    }

    private sealed class IdentityRewriter : SqlRewriter;
}
