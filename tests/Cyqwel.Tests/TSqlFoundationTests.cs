using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.TSqlCompatibility;
using Cyqwel.Validation;
using Cyqwel.Visitors;

namespace Cyqwel.Tests;

public class TSqlFoundationTests
{
    [Theory]
    [InlineData("SELECT answer = 42", "answer")]
    [InlineData("SELECT [display.name] = 42", "display.name")]
    [InlineData("SELECT 'display name' = 42", "display name")]
    [InlineData("SELECT N'display name' = 42", "display name")]
    public void Assignment_style_aliases_have_projection_semantics(string sql, string alias)
    {
        var select = Assert.IsType<SelectStatement>(Assert.Single(SqlDialects.TSql.Parse(sql).Statements));
        var item = Assert.Single(select.Projections);
        Assert.Equal(alias, item.Alias!.Value);
        Assert.Equal(42L, Assert.IsType<LiteralExpression>(item.Expression).Value);
        Assert.Null(item.AssignmentTarget);
        AssertReferenceRoundTrip(sql);
    }

    [Theory]
    [InlineData("=", SqlAssignmentOperator.Assign)]
    [InlineData("+=", SqlAssignmentOperator.Add)]
    [InlineData("-=", SqlAssignmentOperator.Subtract)]
    [InlineData("*=", SqlAssignmentOperator.Multiply)]
    [InlineData("/=", SqlAssignmentOperator.Divide)]
    [InlineData("%=", SqlAssignmentOperator.Modulo)]
    [InlineData("&=", SqlAssignmentOperator.BitwiseAnd)]
    [InlineData("|=", SqlAssignmentOperator.BitwiseOr)]
    [InlineData("^=", SqlAssignmentOperator.BitwiseXor)]
    [InlineData("||=", SqlAssignmentOperator.Concatenate)]
    public void Variable_projections_preserve_target_operator_and_value(string token, SqlAssignmentOperator operation)
    {
        var sql = $"SELECT @total {token} amount FROM sales";
        var document = SqlDialects.TSql.Parse(sql);
        var item = Assert.Single(Assert.IsType<SelectStatement>(Assert.Single(document.Statements)).Projections);
        Assert.Equal("total", item.AssignmentTarget!.Value);
        Assert.Equal(operation, item.AssignmentOperator);
        Assert.Null(item.Alias);
        Assert.Equal("amount", Assert.Single(Assert.IsType<ColumnExpression>(item.Expression).Parts).Value);
        Assert.Same(document, document.Accept(new NoopRewriter()));
        Assert.Contains(item.AssignmentTarget, document.DescendantsAndSelf());
        AssertReferenceRoundTrip(sql);
        Assert.Throws<NotSupportedException>(() => document.ToSql(SqlDialects.PostgreSql));
    }

    [Fact]
    public void Generic_equality_projections_are_not_reinterpreted_as_aliases()
    {
        var select = Assert.IsType<SelectStatement>(Assert.Single(SqlDialects.Generic.Parse("SELECT a = 1").Statements));
        Assert.IsType<BinaryExpression>(Assert.Single(select.Projections).Expression);
    }

    [Theory]
    [InlineData("#temp")]
    [InlineData("##temp")]
    [InlineData("#123")]
    [InlineData("db..#temp")]
    [InlineData("db..t")]
    [InlineData(".dbo.t")]
    [InlineData("..dbo.t")]
    public void Native_object_names_preserve_omitted_parts(string name)
    {
        var sql = $"SELECT * FROM {name}";
        var document = SqlDialects.TSql.Parse(sql);
        Assert.Equal(name, Assert.Single(document.GetTableNames()));
        var generated = document.ToSql(SqlDialects.TSql);
        Assert.DoesNotContain("[]", generated);
        AssertReferenceRoundTrip(sql);
    }

    [Fact]
    public void Table_variables_are_distinct_from_physical_tables_and_rename_safely()
    {
        var document = SqlDialects.TSql.Parse("SELECT id FROM @rows");
        var name = Assert.Single(document.FindAll<TableName>());
        Assert.True(name.IsVariable);
        Assert.Equal("@rows", Assert.Single(document.GetTableNames()));
        var renamed = document.RenameTable("@rows", "@next");
        Assert.Equal("SELECT id FROM @next", renamed.ToSql(SqlDialects.TSql));
        Assert.Equal("SELECT id FROM @rows", document.RenameTable("rows", "physical").ToSql(SqlDialects.TSql));
        Assert.True(Assert.Single(renamed.FindAll<TableName>()).IsVariable);
        AssertReferenceRoundTrip("SELECT id FROM @rows");
    }

    [Fact]
    public void Qualified_functions_are_not_normalized_to_builtins()
    {
        var document = SqlDialects.TSql.Parse("SELECT dbo.getdate(), [s.name].[f.name](1)");
        var calls = document.FindAll<FunctionCallExpression>().ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal("dbo", Assert.Single(calls[0].Qualifiers!).Value);
        Assert.Equal("getdate", calls[0].Name.Value);
        Assert.Equal("s.name", Assert.Single(calls[1].Qualifiers!).Value);
        Assert.True(calls[1].Name.IsQuoted);
        Assert.Same(document, document.Accept(new NoopRewriter()));
        Assert.Empty(document.FindAll<CurrentTimestampExpression>());
        AssertReferenceRoundTrip("SELECT dbo.getdate(), [s.name].[f.name](1)");
        Assert.Equal("dbo.getdate()", Sql.Func("dbo.getdate").ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Qualified_function_and_assignment_identifiers_participate_in_rewrites()
    {
        var document = SqlDialects.TSql.Parse("SELECT @total = db.calculate(1)");
        var changed = document.Accept(new RenameIdentifiers());
        Assert.Equal("SELECT @next = archive.calculate(1)", changed.ToSql(SqlDialects.TSql));
        Assert.Equal("SELECT @total = db.calculate(1)", document.ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Qualified_user_functions_are_not_classified_as_builtin_aggregates()
    {
        var result = SqlValidator.Validate("SELECT dbo.sum(x), y FROM t", SqlDialects.TSql,
            new SqlValidationOptions { Semantic = true });
        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Diagnostics, value => value.Code == SqlValidationCodes.AggregateWithoutGroupBy);
    }

    [Fact]
    public void System_variables_retain_their_prefix_and_identity()
    {
        var document = SqlDialects.TSql.Parse("SELECT @@ROWCOUNT, @value");
        var parameters = document.FindAll<ParameterExpression>().ToArray();
        Assert.True(parameters[0].IsSystemVariable);
        Assert.False(parameters[1].IsSystemVariable);
        Assert.Equal("ROWCOUNT", parameters[0].Name);
        Assert.Equal("SELECT @@ROWCOUNT, @value", document.ToSql(SqlDialects.TSql));
        AssertReferenceRoundTrip("SELECT @@ROWCOUNT, @value");
    }

    [Fact]
    public void Set_toggles_are_keywords_without_changing_ordinary_identifier_quoting()
    {
        var document = SqlDialects.TSql.Parse("SET IDENTITY_INSERT dbo.t ON; SET STATISTICS TIME OFF");
        var settings = document.FindAll<SetStatement>().ToArray();
        Assert.True(settings[0].ToggleValue);
        Assert.False(settings[1].ToggleValue);
        Assert.Equal("SET IDENTITY_INSERT dbo.t ON; SET STATISTICS TIME OFF", document.ToSql(SqlDialects.TSql));
        Assert.Equal("SELECT [ON]", SqlDialects.TSql.Parse("SELECT [ON]").ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Foundation_builders_round_trip_and_reject_invalid_variable_names()
    {
        var query = new SelectStatement([Sql.SelectAssign("@total", Sql.SystemVariable("@@ROWCOUNT"))],
            Sql.TableVariable("@rows", "r"));
        var sql = query.ToSql(SqlDialects.TSql);
        Assert.Equal("SELECT @total = @@ROWCOUNT FROM @rows AS r", sql);
        AssertReferenceRoundTrip(sql);
        Assert.Equal("SELECT * FROM db..t", new SelectStatement(
            [new SelectItem(Sql.Star())], new NamedTable(new TableName("db..t"))).ToSql(SqlDialects.TSql));
        Assert.Throws<ArgumentException>(() => Sql.TableVariable("@bad.name"));
        Assert.Throws<ArgumentException>(() => Sql.SelectAssign("@@", Sql.Lit(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sql.SelectAssign("x", Sql.Lit(1), (SqlAssignmentOperator)99));
    }

    [Fact]
    public void TSql_extensions_are_inherited_without_leaking_into_other_dialects()
    {
        var custom = SqlDialectBuilder.Create("custom-core").BasedOn(SqlDialects.TSql).Build();
        Assert.NotNull(custom.Parse("SELECT x = 1 FROM #t"));
        Assert.False(SqlDialects.PostgreSql.TryParse("SELECT * FROM #t", out _, out _));
        Assert.False(SqlDialects.Sqlite.TryParse("SELECT * FROM @rows", out _, out _));
    }

    [Theory]
    [InlineData("VARCHAR")]
    [InlineData("NVARCHAR")]
    [InlineData("VARBINARY")]
    public void Max_length_is_a_typed_modifier_not_a_numeric_sentinel(string type)
    {
        var sql = $"SELECT CAST(NULL AS {type}(MAX))";
        var document = SqlDialects.TSql.Parse(sql);
        var dataType = Assert.Single(document.FindAll<SqlDataType>());
        Assert.True(dataType.IsMaxLength);
        Assert.Null(dataType.Arguments);
        Assert.Same(document, document.Accept(new NoopRewriter()));
        AssertReferenceRoundTrip(sql);
        Assert.Equal(sql, new SelectStatement(
            [new SelectItem(new CastExpression(Sql.Lit(null), Sql.MaxLengthType(type)))]).ToSql(SqlDialects.TSql));
        Assert.Throws<NotSupportedException>(() => document.ToSql(SqlDialects.PostgreSql));
        Assert.False(SqlDialects.TSql.TryParse("SELECT CAST(NULL AS INT(MAX))", out _, out _));
    }

    [Theory]
    [InlineData(">", "ANY", SqlQuantifier.Any)]
    [InlineData("<=", "ALL", SqlQuantifier.All)]
    [InlineData("=", "SOME", SqlQuantifier.Some)]
    public void Quantified_comparisons_preserve_subqueries_and_quantifiers(
        string operation, string keyword, SqlQuantifier quantifier)
    {
        var sql = $"SELECT 1 WHERE @value {operation} {keyword} (SELECT 100)";
        var document = SqlDialects.TSql.Parse(sql);
        var comparison = Assert.Single(document.FindAll<QuantifiedComparisonExpression>());
        Assert.Equal(quantifier, comparison.Quantifier);
        Assert.IsType<ParameterExpression>(comparison.Left);
        Assert.IsType<SelectStatement>(comparison.Query);
        Assert.Same(document, document.Accept(new NoopRewriter()));
        AssertReferenceRoundTrip(sql);
        Assert.Throws<NotSupportedException>(() => document.ToSql(SqlDialects.Sqlite));
    }

    [Fact]
    public void Quantified_subqueries_validate_correlated_columns_and_projection_width()
    {
        var catalog = ValidationTestCatalog.Create();
        var valid = SqlValidator.Validate(
            "SELECT u.id FROM users u WHERE u.id > ANY (SELECT x.id FROM users x WHERE x.id = u.id)",
            catalog, SqlDialects.TSql);
        Assert.True(valid.IsValid, string.Join("; ", valid.Diagnostics.Select(value => value.Message)));
        var invalid = SqlValidator.Validate(
            "SELECT id FROM users WHERE id > ALL (SELECT id, name FROM users)",
            catalog, SqlDialects.TSql);
        Assert.Contains(invalid.Diagnostics, value => value.Code == SqlValidationCodes.InvalidScalarSubquery);
        var aggregateScope = SqlValidator.Validate(
            "SELECT id, CASE WHEN id > ANY (SELECT MAX(id) FROM users) THEN 1 ELSE 0 END FROM users",
            SqlDialects.TSql, new SqlValidationOptions { Semantic = true });
        Assert.DoesNotContain(aggregateScope.Diagnostics, value => value.Code == SqlValidationCodes.AggregateWithoutGroupBy);
    }

    [Fact]
    public void Quantified_comparison_builders_reject_noncomparison_operators()
    {
        var query = new SelectStatement([new SelectItem(Sql.Lit(1))]);
        var comparison = Sql.QuantifiedComparison(
            Sql.Col("score"), BinaryOperator.GreaterThan, SqlQuantifier.All, query);
        Assert.Equal("score > ALL (SELECT 1)", comparison.ToSql(SqlDialects.TSql));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Sql.QuantifiedComparison(Sql.Lit(1), BinaryOperator.Add, SqlQuantifier.Any, query));
        Assert.Throws<InvalidOperationException>(() =>
            (comparison with { Operator = BinaryOperator.Add }).ToSql(SqlDialects.TSql));
    }

    [Theory]
    [InlineData("CONVERT(NVARCHAR(MAX), N'hello', 0)", false)]
    [InlineData("TRY_CONVERT(DECIMAL(10, 2), N'12.5')", true)]
    [InlineData("CONVERT(DATE, N'20260102', 112)", false)]
    public void Native_conversions_preserve_types_without_inventing_column_references(string expression, bool isTry)
    {
        var sql = "SELECT " + expression;
        var document = SqlDialects.TSql.Parse(sql);
        var convert = Assert.Single(document.FindAll<ConvertExpression>());
        Assert.Equal(isTry, convert.IsTry);
        Assert.Empty(document.FindAll<ColumnExpression>());
        Assert.Same(document, document.Accept(new NoopRewriter()));
        Assert.Equal(expression, Sql.Convert(convert.Expression, convert.DataType, convert.Style, isTry)
            .ToSql(SqlDialects.TSql));
        Assert.True(SqlValidator.Validate(sql, ValidationTestCatalog.Create(), SqlDialects.TSql).IsValid);
        AssertReferenceRoundTrip(sql);
        Assert.Throws<NotSupportedException>(() => document.ToSql(SqlDialects.PostgreSql));
    }

    [Theory]
    [InlineData("Test#")]
    [InlineData("Test@")]
    [InlineData("Test$")]
    public void Regular_identifiers_accept_native_nonleading_characters(string name)
    {
        var sql = $"SELECT t.{name} FROM t";
        var document = SqlDialects.TSql.Parse(sql);
        Assert.Equal(name, Assert.Single(document.FindAll<ColumnExpression>()).Parts[^1].Value);
        AssertReferenceRoundTrip(sql);
    }

    [Theory]
    [InlineData("SELECT @top = 1")]
    [InlineData("SELECT @name$ = 1")]
    [InlineData("SELECT @name# = 1")]
    [InlineData("SELECT @name@ = 1")]
    [InlineData("SELECT * FROM @table")]
    public void Variable_names_are_not_quoted_as_keywords(string sql) => AssertReferenceRoundTrip(sql);

    [Theory]
    [InlineData("SELECT 1 WHERE x IS DISTINCT FROM ANY (SELECT 2)")]
    [InlineData("SELECT 1 WHERE x IS NOT DISTINCT FROM SOME (SELECT 2)")]
    [InlineData("SELECT @ value")]
    public void Unsupported_or_nonadjacent_tokens_are_not_split_into_unrelated_statements(string sql) =>
        Assert.False(SqlDialects.TSql.TryParse(sql, out _, out _));

    [Fact]
    public void Native_ansi_concatenation_is_not_rewritten_as_addition()
    {
        const string sql = "SELECT @a ||= 'foo' || 'bar'";
        var document = SqlDialects.TSql.Parse(sql);
        Assert.Equal(BinaryOperator.AnsiConcatenate,
            Assert.Single(document.FindAll<BinaryExpression>()).Operator);
        Assert.Equal(sql, document.ToSql(SqlDialects.TSql));
        Assert.Equal("'foo' || 'bar'", Sql.AnsiConcat(Sql.Lit("foo"), Sql.Lit("bar")).ToSql(SqlDialects.TSql));
        Assert.Equal("'foo' + 'bar'",
            new BinaryExpression(Sql.Lit("foo"), BinaryOperator.Concatenate, Sql.Lit("bar")).ToSql(SqlDialects.TSql));
        AssertReferenceRoundTrip(sql);
    }

    [Fact]
    public void Native_bitwise_precedence_is_preserved_around_quantified_comparisons()
    {
        const string sql = "SELECT 1 WHERE @value & 1 > ANY (SELECT 0)";
        var document = SqlDialects.TSql.Parse(sql);
        var comparison = Assert.Single(document.FindAll<QuantifiedComparisonExpression>());
        Assert.Equal(BinaryOperator.BitwiseAnd, Assert.IsType<BinaryExpression>(comparison.Left).Operator);
        AssertReferenceRoundTrip(sql);

        var arithmetic = SqlDialects.TSql.Parse("SELECT 1 & 2 + 3");
        var root = Assert.IsType<BinaryExpression>(
            Assert.Single(Assert.IsType<SelectStatement>(Assert.Single(arithmetic.Statements)).Projections).Expression);
        Assert.Equal(BinaryOperator.Add, root.Operator);
        Assert.Equal(BinaryOperator.BitwiseAnd, Assert.IsType<BinaryExpression>(root.Left).Operator);
        var grouped = new BinaryExpression(Sql.Lit(1), BinaryOperator.BitwiseAnd,
            new BinaryExpression(Sql.Lit(2), BinaryOperator.Add, Sql.Lit(3)));
        Assert.Equal("1 & (2 + 3)", grouped.ToSql(SqlDialects.TSql));
    }

    [Theory]
    [InlineData("JSON_ARRAYAGG(c)", null, false)]
    [InlineData("JSON_ARRAYAGG(c NULL ON NULL)", JsonNullHandling.NullOnNull, false)]
    [InlineData("JSON_ARRAYAGG(c ABSENT ON NULL)", JsonNullHandling.AbsentOnNull, false)]
    [InlineData("JSON_ARRAYAGG(c ORDER BY c)", null, true)]
    [InlineData("JSON_ARRAYAGG(c ORDER BY c NULL ON NULL)", JsonNullHandling.NullOnNull, true)]
    [InlineData("JSON_ARRAYAGG(c ORDER BY c DESC ABSENT ON NULL)", JsonNullHandling.AbsentOnNull, true)]
    public void Json_array_aggregation_preserves_ordering_and_null_handling(
        string expression, JsonNullHandling? nullHandling, bool ordered)
    {
        var sql = "SELECT " + expression;
        var document = SqlDialects.TSql.Parse(sql);
        var aggregate = Assert.Single(document.FindAll<JsonArrayAggregateExpression>());
        Assert.Equal(nullHandling, aggregate.NullHandling);
        Assert.Equal(ordered, aggregate.OrderBy is { Count: > 0 });
        Assert.Same(document, document.Accept(new NoopRewriter()));
        Assert.Equal(expression, Sql.JsonArrayAgg(aggregate.Expression, aggregate.OrderBy, aggregate.NullHandling)
            .ToSql(SqlDialects.TSql));
        Assert.All(document.RenameColumn("c", "renamed").FindAll<ColumnExpression>(),
            column => Assert.Equal("renamed", column.Parts[^1].Value));
        AssertReferenceRoundTrip(sql);
        Assert.Throws<NotSupportedException>(() => document.ToSql(SqlDialects.PostgreSql));
    }

    [Fact]
    public void Json_array_aggregation_validates_value_and_ordering_columns()
    {
        var catalog = ValidationTestCatalog.Create();
        Assert.True(SqlValidator.Validate(
            "SELECT JSON_ARRAYAGG(id ORDER BY name ABSENT ON NULL) FROM users",
            catalog, SqlDialects.TSql).IsValid);
        Assert.Contains(SqlValidator.Validate(
            "SELECT JSON_ARRAYAGG(id ORDER BY missing) FROM users",
            catalog, SqlDialects.TSql).Diagnostics, value => value.Code == SqlValidationCodes.UnknownColumn);
        Assert.Contains(SqlValidator.Validate("SELECT JSON_ARRAYAGG(id), name FROM users",
            SqlDialects.TSql, new SqlValidationOptions { Semantic = true }).Diagnostics,
            value => value.Code == SqlValidationCodes.AggregateWithoutGroupBy);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Sql.JsonArrayAgg(Sql.Lit(1), nullHandling: (JsonNullHandling)99));
    }

    [Fact]
    public void Nested_replace_functions_preserve_literal_contents() =>
        AssertReferenceRoundTrip("SELECT JSON_QUERY(REPLACE(REPLACE(x , '''', '\"'), '\"\"', '\"'))");

    [Theory]
    [InlineData("JSON_ARRAYAGG(name)")]
    [InlineData("JSON_ARRAYAGG(name ABSENT ON NULL)")]
    [InlineData("JSON_ARRAYAGG(name ORDER BY name NULL ON NULL)")]
    public void Json_array_aggregation_retains_existing_window_support(string expression)
    {
        var sql = $"SELECT {expression} OVER (PARTITION BY dept) FROM employees";
        var document = SqlDialects.TSql.Parse(sql);
        var window = Assert.Single(document.FindAll<WindowExpression>());
        Assert.IsType<JsonArrayAggregateExpression>(window.Expression);
        Assert.Equal("dept", Assert.Single(Assert.IsType<ColumnExpression>(
            Assert.Single(window.PartitionBy!)).Parts).Value);
        Assert.Same(document, document.Accept(new NoopRewriter()));
        AssertReferenceRoundTrip(sql);
        Assert.DoesNotContain(SqlValidator.Validate(
            "SELECT id, JSON_ARRAYAGG(name) OVER (PARTITION BY id) FROM users",
            SqlDialects.TSql, new SqlValidationOptions { Semantic = true }).Diagnostics,
            value => value.Code == SqlValidationCodes.AggregateWithoutGroupBy);
    }

    private static void AssertReferenceRoundTrip(string sql)
    {
        var result = new CampaignRunner().Evaluate(new CorpusCase
        {
            Id = "foundation",
            Source = "scriptdom",
            Path = "foundation-tests",
            Line = 1,
            Context = "foundation",
            Sql = sql,
        });
        Assert.True(result.ReferenceAccepted, string.Join("; ", result.ReferenceErrors?.Select(error => error.Message) ?? []));
        Assert.True(result.CyqwelParsed, result.ParseError?.Message);
        Assert.Empty(result.GeneratedReferenceErrors!);
        Assert.Empty(result.AstMismatches!);
        Assert.Equal(result.GeneratedSql, result.RegeneratedSql);
    }

    private sealed class NoopRewriter : SqlRewriter;

    private sealed class RenameIdentifiers : SqlRewriter
    {
        protected override SqlNode VisitIdentifier(SqlIdentifier node) => node.Value switch
        {
            "total" => node with { Value = "next" },
            "db" => node with { Value = "archive" },
            _ => node,
        };
    }
}
