using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;
using Cyqwel.Parsing;
using Cyqwel.Validation;
using Cyqwel.Visitors;
using TSql180Parser = Microsoft.SqlServer.TransactSql.ScriptDom.TSql180Parser;

namespace Cyqwel.Tests;

public class TSqlQueryCoreTests
{
    public static TheoryData<string> CoreQueries => new()
    {
        "SELECT d.a FROM (SELECT 1) AS d(a)",
        "SELECT f.a FROM dbo.f(1, 'x') AS f(a)",
        "SELECT t.a, f.b FROM t CROSS APPLY dbo.f(t.a) AS f(b)",
        "SELECT d.b FROM t OUTER APPLY (SELECT t.a AS b) AS d",
        "SELECT * FROM t LEFT JOIN (u INNER JOIN v ON u.id = v.id) ON t.id = u.id",
        "SELECT * FROM OPENJSON(@json)",
        "SELECT j.id FROM OPENJSON(@json, '$.items') WITH (id INT '$.id', payload NVARCHAR(MAX) '$.data' AS JSON) AS j",
        "SELECT * FROM t WITH (NOLOCK, ROWLOCK, READPAST)",
        "SELECT * FROM t WITH (TABLOCK, INDEX(myindex))",
        "SELECT * FROM t WITH (INDEX([First.Index], other_index), NOLOCK)",
        "SELECT * FROM t WITH (INDEX(0))",
        "SELECT * FROM t WITH (INDEX = myindex)",
        "SELECT * FROM t WITH (INDEX = 1)",
        "SELECT * FROM t TABLESAMPLE (10 PERCENT)",
        "SELECT * FROM t TABLESAMPLE (20 ROWS)",
        "SELECT * FROM t TABLESAMPLE SYSTEM (2.5 PERCENT)",
        "SELECT * FROM t TABLESAMPLE (20)",
        "SELECT t.id FROM users AS t TABLESAMPLE (20 ROWS) WITH (NOLOCK)",
        "SELECT * FROM t AS a INNER HASH JOIN u AS b ON a.id = b.id",
        "SELECT * FROM t AS a LEFT OUTER LOOP JOIN u AS b ON a.id = b.id",
        "SELECT * FROM t AS a FULL MERGE JOIN u AS b ON a.id = b.id",
        "SELECT a INTO #copy FROM t",
        "SELECT a INTO #copy FROM t UNION ALL SELECT a FROM u",
        "SELECT * FROM (SELECT category, amount FROM t) AS s PIVOT (SUM(amount) FOR category IN ([a], [b])) AS p",
        "SELECT * FROM t UNPIVOT (amount FOR category IN ([a], [b])) AS p",
        "SELECT a FROM t FOR JSON AUTO",
        "SELECT a FROM t FOR JSON PATH, ROOT('items'), INCLUDE_NULL_VALUES",
        "SELECT a FROM t FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER",
        "SELECT a FROM t FOR XML PATH('row'), TYPE, ROOT('items')",
        "SELECT a FROM t FOR XML RAW('row'), ROOT",
        "SELECT a FROM t FOR XML AUTO, TYPE",
        "SELECT (SELECT a FROM t FOR JSON PATH) AS data",
        "SELECT (SELECT a FROM t FOR XML PATH(''), TYPE) AS data",
        "SELECT a FROM t UNION ALL SELECT a FROM u ORDER BY a FOR JSON PATH OPTION (RECOMPILE)",
        "SELECT a FROM t OPTION (RECOMPILE, MAXDOP 2, MAXRECURSION 100, OPTIMIZE FOR UNKNOWN, FORCE ORDER, FAST 10)",
    };

    [Theory]
    [MemberData(nameof(CoreQueries))]
    public void Core_queries_round_trip_and_preserve_reference_validity(string sql)
    {
        AssertReferenceValid(sql);
        var document = SqlParser.Parse(sql, SqlDialects.TSql);
        var generated = document.ToSql(SqlDialects.TSql);
        AssertReferenceValid(generated);
        Assert.Equal(generated, SqlParser.Parse(generated, SqlDialects.TSql).ToSql(SqlDialects.TSql));
        Assert.Same(document, new NoOpRewriter().Visit(document));
        document.Accept(new CountingVisitor());
    }

    [Theory]
    [InlineData("SELECT t.x, y.z FROM x CROSS APPLY tvfTest(t.x) y(z)", "CROSS APPLY")]
    [InlineData("SELECT t.x, y.z FROM x OUTER APPLY tvfTest(t.x)y(z)", "OUTER APPLY")]
    public void Frozen_tvf_sources_preserve_exact_object_name_case(string sql, string apply)
    {
        var document = SqlParser.Parse(sql, SqlDialects.TSql);
        var original = Assert.Single(document.FindAll<TableFunction>()).Function.Name;
        Assert.Equal("tvfTest", original.Value);
        Assert.False(original.IsQuoted);
        foreach (var policy in Enum.GetValues<FunctionNameCase>())
        {
            var options = new SqlGenerationOptions { FunctionNameCase = policy };
            var generated = document.ToSql(SqlDialects.TSql, options);
            Assert.Equal($"SELECT t.x, y.z FROM x {apply} tvfTest(t.x) AS y (z)", generated);
            AssertReferenceValid(generated);
            var reparsed = SqlParser.Parse(generated, SqlDialects.TSql);
            var name = Assert.Single(reparsed.FindAll<TableFunction>()).Function.Name;
            Assert.Equal(original.Value, name.Value);
            Assert.Equal(original.IsQuoted, name.IsQuoted);
            Assert.Equal(generated, reparsed.ToSql(SqlDialects.TSql, options));
        }
    }

    [Theory]
    [InlineData(FunctionNameCase.Preserve, "ABS")]
    [InlineData(FunctionNameCase.Upper, "ABS")]
    [InlineData(FunctionNameCase.Lower, "abs")]
    public void Scalar_function_case_policy_does_not_change_table_function_names(FunctionNameCase policy, string scalar)
    {
        var document = SqlParser.Parse("SELECT ABS(1) FROM tvfTest(ABS(1)) AS f", SqlDialects.TSql);
        var options = new SqlGenerationOptions { FunctionNameCase = policy };
        var generated = document.ToSql(SqlDialects.TSql, options);
        Assert.Equal($"SELECT {scalar}(1) FROM tvfTest({scalar}(1)) AS f", generated);
        AssertReferenceValid(generated);
        Assert.Equal(generated, SqlParser.Parse(generated, SqlDialects.TSql).ToSql(SqlDialects.TSql, options));
    }

    [Theory]
    [InlineData("Repeat('x', 2)")]
    [InlineData("Now(1)")]
    [InlineData("Ln(2)")]
    public void Table_function_object_names_bypass_scalar_builtin_mapping(string function)
    {
        var sql = $"SELECT * FROM {function} AS f";
        var document = SqlParser.Parse(sql, SqlDialects.TSql);
        Assert.Equal(sql, document.ToSql(SqlDialects.TSql));
        AssertReferenceValid(sql);
    }

    [Theory]
    [InlineData("tvfTest")]
    [InlineData("[tvfTest]")]
    [InlineData("mixedSchema.tvfTest")]
    [InlineData("[mixed.Schema].[tvfTest]")]
    public void Renaming_table_functions_preserves_case_and_quoting(string name)
    {
        var document = SqlParser.Parse($"SELECT * FROM {name}(1) AS f", SqlDialects.TSql);
        var original = Assert.Single(document.FindAll<TableFunction>()).Function;
        var rewritten = new RenameTableFunctionRewriter().Visit(document);
        var renamed = Assert.Single(rewritten.FindAll<TableFunction>()).Function;
        Assert.Equal("tvfTest", original.Name.Value);
        Assert.Equal("tvfRenamed", renamed.Name.Value);
        Assert.Equal(original.Name.IsQuoted, renamed.Name.IsQuoted);
        Assert.Same(original.Qualifiers, renamed.Qualifiers);
        foreach (var policy in Enum.GetValues<FunctionNameCase>())
        {
            var options = new SqlGenerationOptions { FunctionNameCase = policy };
            var generated = rewritten.ToSql(SqlDialects.TSql, options);
            Assert.Equal($"SELECT * FROM {name.Replace("tvfTest", "tvfRenamed", StringComparison.Ordinal)}(1) AS f", generated);
            AssertReferenceValid(generated);
            Assert.Equal(generated, SqlParser.Parse(generated, SqlDialects.TSql).ToSql(SqlDialects.TSql, options));
        }
    }

    [Fact]
    public void Sources_and_query_tail_are_structured()
    {
        var select = ParseSelect("SELECT j.id INTO #copy FROM t OUTER APPLY OPENJSON(t.json, '$.items') WITH (id INT '$.id', payload NVARCHAR(MAX) AS JSON) j FOR JSON PATH, ROOT('rows') OPTION (MAXDOP 2)");
        Assert.Equal("#copy", select.Into!.Parts[0].Value);
        var join = Assert.IsType<JoinTable>(select.From);
        Assert.Equal(JoinKind.OuterApply, join.Kind);
        var json = Assert.IsType<OpenJsonTable>(join.Right);
        Assert.Equal("$.items", Assert.IsType<LiteralExpression>(json.Path).Value);
        Assert.Equal("$.id", json.Schema![0].Path!.Value);
        Assert.True(json.Schema[1].AsJson);
        Assert.Equal("rows", select.ResultFormat!.Root!.Value);
        Assert.Equal(new TSqlQueryOption(TSqlQueryOptionKind.MaxDop, 2), Assert.Single(select.QueryOptions!));
    }

    [Fact]
    public void Rewriters_visit_alias_lists_schema_and_tail_literals()
    {
        var document = SqlParser.Parse(
            "SELECT * FROM (SELECT 1) d(old) CROSS APPLY OPENJSON('{}') WITH (old INT '$.old') j FOR JSON PATH, ROOT('old')",
            SqlDialects.TSql);
        var rewritten = new RenameRewriter().Visit(document);
        var sql = rewritten.ToSql(SqlDialects.TSql);
        Assert.Contains("new", sql);
        Assert.Contains("'$.new'", sql);
        Assert.Contains("ROOT('new')", sql);
        Assert.DoesNotContain("old", sql);
        AssertReferenceValid(sql);
    }

    [Fact]
    public void Set_tail_belongs_to_set_not_last_select()
    {
        var set = Assert.IsType<SetOperationStatement>(Assert.Single(SqlParser.Parse(
            "SELECT 1 UNION ALL SELECT 2 FOR JSON PATH OPTION (RECOMPILE)", SqlDialects.TSql).Statements));
        Assert.NotNull(set.ResultFormat);
        Assert.Single(set.QueryOptions!);
        Assert.Null(set.Left.ResultFormat);
        Assert.Null(set.Right.QueryOptions);
        Assert.Null(set.Right.ResultFormat);
    }

    [Theory]
    [InlineData("SELECT * FROM t OPTION (QUERYTRACEON 1234)")]
    [InlineData("SELECT * FROM t OPTION (HASH JOIN)")]
    [InlineData("SELECT * FROM t FOR XML EXPLICIT")]
    [InlineData("SELECT * FROM t FOR XML PATH, BINARY BASE64")]
    [InlineData("SELECT * FROM t FOR JSON RAW")]
    [InlineData("SELECT * FROM t INNER REMOTE JOIN u ON t.id = u.id")]
    public void Deferred_modes_are_rejected(string sql) =>
        Assert.False(SqlParser.TryParse(sql, SqlDialects.TSql, out _, out _));

    [Theory]
    [InlineData("SELECT * FROM t WITH (NOLOCK)")]
    [InlineData("SELECT * FROM OPENJSON('{}')")]
    [InlineData("SELECT * FROM t FOR JSON PATH")]
    [InlineData("SELECT * FROM t OPTION (RECOMPILE)")]
    [InlineData("SELECT * INTO copy FROM t")]
    [InlineData("SELECT * FROM t TABLESAMPLE (10 PERCENT)")]
    public void TSql_extensions_are_gated_and_unsupported_targets_are_explicit(string sql)
    {
        Assert.False(SqlParser.TryParse(sql, SqlDialects.PostgreSql, out _, out _));
        var document = SqlParser.Parse(sql, SqlDialects.TSql);
        Assert.Throws<NotSupportedException>(() => document.ToSql(SqlDialects.PostgreSql));
    }

    [Fact]
    public void Builder_carries_source_and_query_extensions()
    {
        var query = Sql.Select(Sql.Col("j.id"))
            .From(new OpenJsonTable(Sql.Lit("{}"), Schema: [new(new("id"), new("INT"))], Alias: new("j")))
            .Into(new TableName("#copy"))
            .ResultFormat(new(TSqlResultFormatKind.Json, TSqlResultFormatMode.Path))
            .QueryOptions(new TSqlQueryOption(TSqlQueryOptionKind.Recompile))
            .Build();
        var parsed = ParseSelect(query.ToSql(SqlDialects.TSql));
        Assert.Equal(query.ResultFormat, parsed.ResultFormat);
        Assert.Equal(query.QueryOptions, parsed.QueryOptions);
        Assert.IsType<OpenJsonTable>(parsed.From);
    }

    [Fact]
    public void Builders_preserve_derived_alias_columns_and_set_tail()
    {
        var query = Sql.Select(Sql.Col("d.named"))
            .From(Sql.Select(Sql.Lit(1)).Build(), "d", "named")
            .Union(Sql.Select(Sql.Lit(2)))
            .ResultFormat(new(TSqlResultFormatKind.Json, TSqlResultFormatMode.Path))
            .QueryOptions(new TSqlQueryOption(TSqlQueryOptionKind.Recompile))
            .Build();
        var sql = query.ToSql(SqlDialects.TSql);
        AssertReferenceValid(sql);
        var set = Assert.IsType<SetOperationStatement>(Assert.Single(SqlParser.Parse(sql, SqlDialects.TSql).Statements));
        var source = Assert.IsType<DerivedTable>(Assert.IsType<SelectStatement>(set.Left).From);
        Assert.Equal("named", Assert.Single(source.Columns!).Value);
        Assert.Single(set.QueryOptions!);
    }

    [Fact]
    public void Actual_rewriters_preserve_hints_and_visit_options_once()
    {
        var document = SqlParser.Parse(
            "SELECT a.id INTO #copy FROM a WITH (NOLOCK) INNER HASH JOIN b ON a.id = b.id OPTION (MAXDOP 2)",
            SqlDialects.TSql);
        var rewriter = new OptionRewriter();
        var rewritten = rewriter.Visit(document).RenameTable("#copy", "#renamed");
        Assert.Equal(1, rewriter.Visits);
        var select = Assert.IsType<SelectStatement>(Assert.Single(rewritten.Statements));
        Assert.Equal("#renamed", select.Into!.Parts[0].Value);
        var join = Assert.IsType<JoinTable>(select.From);
        Assert.Equal(TSqlJoinHint.Hash, join.Hint);
        Assert.Equal(TSqlTableHint.NoLock, Assert.Single(Assert.IsType<NamedTable>(join.Left).Hints!));
        Assert.Equal(3, Assert.Single(select.QueryOptions!).Value);
        AssertReferenceValid(rewritten.ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Invalid_manually_constructed_options_fail_explicitly()
    {
        var query = Sql.Select(Sql.Lit(1)).Build();
        Assert.Throws<ArgumentException>(() =>
            (query with { QueryOptions = [new(TSqlQueryOptionKind.MaxDop)] }).ToSql(SqlDialects.TSql));
        Assert.Throws<ArgumentException>(() =>
            (query with { ResultFormat = new(TSqlResultFormatKind.Json, TSqlResultFormatMode.Raw) }).ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Column_transform_rewrites_unpivot_source_column_references()
    {
        var original = ParseSelect("SELECT amount FROM t UNPIVOT (amount FOR category IN (a,b)) p");
        var renamed = original.RenameColumn("a", "renamed");
        Assert.Equal("renamed", Assert.IsType<UnpivotTable>(renamed.From).Columns[0].Value);
        Assert.Equal("a", Assert.IsType<UnpivotTable>(original.From).Columns[0].Value);
        AssertReferenceValid(renamed.ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Derived_merge_output_is_a_structured_source()
    {
        const string sql = "INSERT INTO archive SELECT d.changed FROM (MERGE INTO users AS t USING users AS s ON t.id = s.id WHEN MATCHED THEN UPDATE SET name = s.name OUTPUT inserted.id) AS d(changed)";
        AssertReferenceValid(sql);
        var document = SqlParser.Parse(sql, SqlDialects.TSql);
        var insert = Assert.IsType<InsertStatement>(Assert.Single(document.Statements));
        var source = Assert.IsType<DerivedMutationTable>(Assert.IsType<SelectStatement>(insert.Source).From);
        Assert.Equal("changed", Assert.Single(source.Columns!).Value);
        Assert.Single(source.Statement.Output!.Items);
        Assert.Same(document, new NoOpRewriter().Visit(document));
        var generated = document.ToSql(SqlDialects.TSql);
        AssertReferenceValid(generated);
        Assert.Equal(generated, SqlParser.Parse(generated, SqlDialects.TSql).ToSql(SqlDialects.TSql));
        AssertReferenceValid(document.RenameTable("users", "people").ToSql(SqlDialects.TSql));
        Assert.Throws<NotSupportedException>(() => document.ToSql(SqlDialects.PostgreSql));
    }

    [Fact]
    public void Derived_merge_output_schema_uses_output_types_and_aliases()
    {
        const string sql = "INSERT INTO orders (id) SELECT d.changed FROM (MERGE INTO users AS t USING users AS s ON t.id = s.id WHEN MATCHED THEN UPDATE SET name = s.name OUTPUT inserted.id) AS d(changed)";
        var result = SqlValidator.Validate(sql, ValidationTestCatalog.Create(), SqlDialects.TSql);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var invalid = SqlValidator.Validate(sql.Replace("d(changed)", "d(changed,extra)", StringComparison.Ordinal),
            ValidationTestCatalog.Create(), SqlDialects.TSql);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == SqlValidationCodes.CteColumnCountMismatch);
    }

    [Fact]
    public void Derived_column_aliases_use_target_capabilities()
    {
        var query = ParseSelect("SELECT d.a FROM (SELECT 1) d(a)");
        var postgres = query.ToSql(SqlDialects.PostgreSql);
        Assert.Equal(postgres, SqlParser.Parse(postgres, SqlDialects.PostgreSql).ToSql(SqlDialects.PostgreSql));
        Assert.Throws<NotSupportedException>(() => query.ToSql(SqlDialects.Sqlite));
        Assert.Throws<NotSupportedException>(() => query.ToSql(SqlDialects.Oracle));
    }

    [Theory]
    [InlineData(JoinKind.CrossApply, "CROSS APPLY")]
    [InlineData(JoinKind.OuterApply, "OUTER APPLY")]
    public void Existing_generic_apply_support_is_preserved(JoinKind kind, string keyword)
    {
        var source = new DerivedTable(Sql.Select(Sql.Lit(1)).Build(), new SqlIdentifier("d"));
        var builder = Sql.Select(Sql.Star()).From("t");
        var query = (kind == JoinKind.CrossApply ? builder.CrossApply(source) : builder.OuterApply(source)).Build();
        var generated = query.ToSql(SqlDialects.Generic);
        Assert.Contains(keyword, generated);
        var parsed = Assert.IsType<SelectStatement>(Assert.Single(SqlParser.Parse(generated, SqlDialects.Generic).Statements));
        Assert.Equal(kind, Assert.IsType<JoinTable>(parsed.From).Kind);
        Assert.Equal(generated, parsed.ToSql(SqlDialects.Generic));
    }

    [Fact]
    public void Index_hint_references_are_typed_ordered_and_rewritten()
    {
        var query = ParseSelect("SELECT id FROM t WITH (INDEX([old], 2), NOLOCK)");
        var table = Assert.IsType<NamedTable>(query.From);
        var hint = table.Hints![0];
        Assert.Equal(TSqlTableHintKind.Index, hint.Kind);
        Assert.Equal("old", hint.Indexes![0].Name!.Value);
        Assert.Null(hint.Indexes[0].Id);
        Assert.Equal(2, hint.Indexes[1].Id);
        Assert.Equal(TSqlTableHint.NoLock, table.Hints[1]);
        Assert.Equal(2, query.FindAll<TSqlIndexReference>().Count());
        Assert.Same(query, new NoOpRewriter().Visit(query));
        var rewritten = new RenameRewriter().Visit(query);
        Assert.Contains("INDEX([new], 2), NOLOCK", rewritten.ToSql(SqlDialects.TSql));
        AssertReferenceValid(rewritten.ToSql(SqlDialects.TSql));
        Assert.Throws<NotSupportedException>(() => query.ToSql(SqlDialects.PostgreSql));
    }

    [Fact]
    public void Index_names_are_not_schema_column_references()
    {
        var result = SqlValidator.Validate("SELECT id FROM users WITH (INDEX(not_a_column))",
            ValidationTestCatalog.Create(), SqlDialects.TSql);
        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Builders_preserve_structured_index_hints()
    {
        var query = Sql.Select(Sql.Col("id")).From(new NamedTable("t")
        {
            Hints = [TSqlTableHint.TabLock,
                new(TSqlTableHintKind.Index, [new(Name: new SqlIdentifier("ix"))])],
        }).Build();
        var parsed = ParseSelect(query.ToSql(SqlDialects.TSql));
        Assert.Equal("ix", Assert.IsType<NamedTable>(parsed.From).Hints![1].Indexes![0].Name!.Value);
        AssertReferenceValid(query.ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Table_sampling_is_structured_and_rewritten()
    {
        var query = ParseSelect("SELECT id FROM users AS u TABLESAMPLE SYSTEM (10 PERCENT) WITH (NOLOCK)");
        var table = Assert.IsType<NamedTable>(query.From);
        Assert.Equal(TSqlTableSampleUnit.Percent, table.Sample!.Unit);
        Assert.True(table.Sample.IsSystem);
        Assert.Equal(10L, table.Sample.Amount.Value);
        Assert.Same(query, new NoOpRewriter().Visit(query));
        var rewritten = new SamplingRewriter().Visit(query);
        Assert.Equal(20L, Assert.IsType<NamedTable>(rewritten.From).Sample!.Amount.Value);
        AssertReferenceValid(rewritten.ToSql(SqlDialects.TSql));
        var validation = SqlValidator.Validate(query.ToSql(SqlDialects.TSql), ValidationTestCatalog.Create(), SqlDialects.TSql);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void Builder_supports_bounded_table_sampling()
    {
        var query = Sql.Select(Sql.Col("id")).From(new NamedTable("users")
        {
            Sample = new TSqlTableSample(Sql.Lit(20), TSqlTableSampleUnit.Rows),
        }).Build();
        AssertReferenceValid(query.ToSql(SqlDialects.TSql));
        Assert.Equal(TSqlTableSampleUnit.Rows, Assert.IsType<NamedTable>(ParseSelect(query.ToSql(SqlDialects.TSql)).From).Sample!.Unit);
    }

    [Fact]
    public void Custom_dialects_inherit_query_capabilities()
    {
        const string sql = "SELECT d.a FROM (SELECT 1) d(a) FOR JSON PATH OPTION (RECOMPILE)";
        var inherited = SqlDialectBuilder.Create("query-tsql").BasedOn(SqlDialects.TSql).Build();
        var document = SqlParser.Parse(sql, inherited);
        AssertReferenceValid(document.ToSql(inherited));
        var disabled = SqlDialectBuilder.Create("without-query-extensions").BasedOn(SqlDialects.TSql)
            .ConfigureParser(options => options with { SupportsTSqlExtensions = false }).Build();
        Assert.False(SqlParser.TryParse(sql, disabled, out _, out _));
        Assert.Throws<NotSupportedException>(() => document.ToSql(disabled));
    }

    [Theory]
    [InlineData("SELECT d.renamed FROM (SELECT id FROM users) d(renamed)")]
    [InlineData("SELECT j.id FROM OPENJSON('{}') WITH (id INT) j")]
    [InlineData("SELECT j.[key], j.value, j.[type] FROM OPENJSON('{}') j")]
    [InlineData("SELECT f.renamed FROM users u CROSS APPLY dbo.f(u.id) f(renamed)")]
    [InlineData("SELECT d.id FROM users u OUTER APPLY (SELECT u.id) d")]
    [InlineData("SELECT (SELECT id, name FROM users FOR JSON PATH) AS data")]
    [InlineData("SELECT p.a FROM (SELECT name, age FROM users) s PIVOT (SUM(age) FOR name IN (a, b)) p")]
    [InlineData("SELECT p.age FROM users UNPIVOT (age FOR k IN (id)) p")]
    public void Query_source_schemas_resolve(string sql)
    {
        var result = SqlValidator.Validate(sql, ValidationTestCatalog.Create(), SqlDialects.TSql);
        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
    }

    [Theory]
    [InlineData("SELECT d.missing FROM (SELECT 1) d(known)", SqlValidationCodes.UnknownColumn)]
    [InlineData("SELECT j.missing FROM OPENJSON('{}') WITH (known INT) j", SqlValidationCodes.UnknownColumn)]
    [InlineData("SELECT d.a FROM (SELECT 1) d(a,b)", SqlValidationCodes.CteColumnCountMismatch)]
    public void Query_source_schema_errors_are_not_silenced(string sql, string code)
    {
        var result = SqlValidator.Validate(sql, ValidationTestCatalog.Create(), SqlDialects.TSql);
        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("SELECT 1 OPTION (MAXDOP -1)")]
    [InlineData("SELECT 1 OPTION (MAXRECURSION 32768)")]
    [InlineData("SELECT 1 OPTION (MAXDOP 99999999999999999)")]
    [InlineData("SELECT 1 OPTION (RECOMPILE, RECOMPILE)")]
    [InlineData("SELECT 1 FOR JSON PATH, ROOT('a'), ROOT('b')")]
    [InlineData("SELECT 1 FOR XML PATH, TYPE, TYPE")]
    [InlineData("SELECT 1 FOR JSON PATH, ROOT('a'), WITHOUT_ARRAY_WRAPPER")]
    [InlineData("SELECT 1 OPTION (RECOMPILE) FOR JSON PATH")]
    [InlineData("SELECT 1 FOR JSON PATH ORDER BY 1")]
    [InlineData("SELECT * FROM OPENJSON()")]
    [InlineData("SELECT * FROM OPENJSON('{}', '$', 1)")]
    [InlineData("SELECT * FROM dbo.f() (a)")]
    [InlineData("SELECT * FROM t WITH (INDEX())")]
    [InlineData("SELECT * FROM t WITH (INDEX(-1))")]
    [InlineData("SELECT * FROM t WITH (INDEX(2147483648))")]
    [InlineData("SELECT * FROM t TABLESAMPLE (10 PERCENT) REPEATABLE (123)")]
    [InlineData("SELECT * FROM t TABLESAMPLE (101 PERCENT)")]
    [InlineData("SELECT * FROM t TABLESAMPLE (-1 ROWS)")]
    [InlineData("UPDATE t TABLESAMPLE (10 PERCENT) SET x = 1")]
    public void Invalid_or_duplicate_options_are_rejected(string sql) =>
        Assert.False(SqlParser.TryParse(sql, SqlDialects.TSql, out _, out _));

    private static SelectStatement ParseSelect(string sql) =>
        Assert.IsType<SelectStatement>(Assert.Single(SqlParser.Parse(sql, SqlDialects.TSql).Statements));

    private static void AssertReferenceValid(string sql)
    {
        new TSql180Parser(true).Parse(new StringReader(sql), out var errors);
        Assert.True(errors.Count == 0, sql + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(error => error.Message)));
    }

    private sealed class NoOpRewriter : SqlRewriter;
    private sealed class CountingVisitor : SqlVisitor;
    private sealed class RenameTableFunctionRewriter : SqlRewriter
    {
        protected override SqlNode VisitFunctionCall(FunctionCallExpression node)
        {
            var rewritten = (FunctionCallExpression)base.VisitFunctionCall(node);
            return rewritten.Name.Value == "tvfTest"
                ? rewritten with { Name = rewritten.Name with { Value = "tvfRenamed" } }
                : rewritten;
        }
    }
    private sealed class SamplingRewriter : SqlRewriter
    {
        protected override SqlNode VisitLiteral(LiteralExpression node) =>
            node.Value is long value && value == 10 ? node with { Value = 20L } : node;
    }
    private sealed class OptionRewriter : SqlRewriter
    {
        public int Visits { get; private set; }
        protected override SqlNode VisitTSqlQueryOption(TSqlQueryOption node)
        {
            Visits++;
            return node with { Value = node.Value + 1 };
        }
    }
    private sealed class RenameRewriter : SqlRewriter
    {
        protected override SqlNode VisitIdentifier(SqlIdentifier node) =>
            node.Value == "old" ? node with { Value = "new" } : node;

        protected override SqlNode VisitLiteral(LiteralExpression node) =>
            node.Value is string value && value.Contains("old", StringComparison.Ordinal)
                ? node with { Value = value.Replace("old", "new", StringComparison.Ordinal) } : node;
    }
}
