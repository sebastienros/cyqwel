using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Parsing;
using Cyqwel.Visitors;
using Cyqwel.Validation;
using TSql180Parser = Microsoft.SqlServer.TransactSql.ScriptDom.TSql180Parser;

namespace Cyqwel.Tests;

public class TSqlMutationCoreTests
{
    [Theory]
    [InlineData("INSERT t DEFAULT VALUES")]
    [InlineData("WITH y AS (SELECT 2 AS c) INSERT INTO #t SELECT * FROM y")]
    [InlineData("WITH y AS (SELECT 2 AS c) INSERT INTO t SELECT * FROM y")]
    [InlineData("WITH y AS (SELECT 2 AS c) UPDATE t SET a = y.c FROM y")]
    [InlineData("WITH y AS (SELECT id FROM t) DELETE FROM y WHERE id = 2")]
    [InlineData("WITH y AS (SELECT 2 AS c) MERGE t USING y ON t.a = y.c WHEN MATCHED THEN DELETE;")]
    [InlineData("INSERT TOP (2) INTO t (a) OUTPUT inserted.a INTO @rows (a) VALUES (1), (2)")]
    [InlineData("INSERT INTO t (a) OUTPUT inserted.a SELECT a FROM source")]
    [InlineData("UPDATE TOP (10) PERCENT t SET a += 1 OUTPUT deleted.a, inserted.a INTO audit (old_a, new_a) FROM t JOIN s ON t.id = s.id WHERE s.id > 0")]
    [InlineData("DELETE t OUTPUT deleted.a FROM t JOIN s ON t.id = s.id WHERE s.id > 0")]
    [InlineData("DELETE TOP (2) FROM t OUTPUT deleted.* WHERE a > 0")]
    [InlineData("MERGE TOP (2) t USING s ON t.id = s.id WHEN MATCHED THEN UPDATE SET a = s.a WHEN NOT MATCHED BY TARGET THEN INSERT (a) VALUES (s.a) WHEN NOT MATCHED BY SOURCE THEN DELETE OUTPUT $action, inserted.a, deleted.a INTO audit (action, new_a, old_a);")]
    [InlineData("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN DELETE;")]
    [InlineData("MERGE t USING s ON t.id = s.id WHEN NOT MATCHED BY TARGET THEN INSERT (a) VALUES (s.a);")]
    [InlineData("MERGE t USING s ON t.id = s.id WHEN NOT MATCHED BY SOURCE THEN DELETE;")]
    [InlineData("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN DELETE OUTPUT $action;")]
    [InlineData("MERGE TOP (2) t USING s ON t.id = s.id WHEN MATCHED THEN DELETE;")]
    [InlineData("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN UPDATE SET a = s.a WHEN NOT MATCHED BY TARGET THEN INSERT (a) VALUES (s.a);")]
    [InlineData("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN DELETE OUTPUT $action, inserted.a, deleted.a INTO audit (action, new_a, old_a);")]
    [InlineData("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN DELETE OUTPUT $action, inserted.a, deleted.a INTO audit ([action], new_a, old_a);")]
    [InlineData("MERGE INTO mytable WITH (HOLDLOCK) AS T USING mytable_merge AS S ON (T.user_id = S.user_id) WHEN NOT MATCHED THEN INSERT (c1, c2) VALUES (S.c1, S.c2);")]
    [InlineData("UPDATE x SET y = 1 OUTPUT x.a, x.b INTO @y FROM y")]
    [InlineData("INSERT INTO x (y) OUTPUT x.a, x.b INTO l SELECT * FROM z")]
    [InlineData("DELETE x OUTPUT x.a FROM z")]
    [InlineData("UPDATE start WITH (ROWLOCK) SET a = 1")]
    [InlineData("DELETE FROM start WITH (ROWLOCK)")]
    [InlineData("UPDATE t SET a = 1 OPTION (RECOMPILE, MAXDOP 2)")]
    [InlineData("UPDATE t SET a ||= 'x'")]
    [InlineData("DELETE FROM t WHERE a = 1 OPTION (MAXRECURSION 5)")]
    [InlineData("INSERT t DEFAULT VALUES OPTION (RECOMPILE)")]
    public void Native_mutations_round_trip(string sql) => RoundTrip(sql);

    [Fact]
    public void Output_destination_is_a_table_not_returning_bind_variables()
    {
        var insert = Assert.IsType<InsertStatement>(Assert.Single(SqlParser.Parse(
            "INSERT t OUTPUT inserted.id INTO @rows (id) DEFAULT VALUES", SqlDialects.TSql).Statements));
        Assert.True(insert.IsDefaultValues);
        Assert.Null(insert.Returning);
        Assert.Null(insert.ReturningInto);
        Assert.True(insert.Output!.Into!.IsVariable);
        Assert.Equal("rows", insert.Output.Into.Parts[0].Value);
        Assert.Equal("id", Assert.Single(insert.Output.Columns!).Value);
        Assert.Same(insert, new IdentityRewriter().Visit(insert));
        var rewritten = Assert.IsType<InsertStatement>(new RenameRewriter().Visit(insert));
        Assert.Equal("renamed", rewritten.Output!.Columns![0].Value);
        Assert.Contains(rewritten.FindAll<SqlIdentifier>(), identifier => identifier.Value == "renamed");
    }

    [Fact]
    public void Update_compound_assignments_preserve_operators()
    {
        var update = Assert.IsType<UpdateStatement>(Assert.Single(SqlParser.Parse(
            "UPDATE t SET a += 1, b -= 2, c *= 3, d /= 4, e %= 5, f &= 6, g |= 7, h ^= 8",
            SqlDialects.TSql).Statements));
        Assert.Equal(
            new[] { SqlAssignmentOperator.Add, SqlAssignmentOperator.Subtract, SqlAssignmentOperator.Multiply,
                SqlAssignmentOperator.Divide, SqlAssignmentOperator.Modulo, SqlAssignmentOperator.BitwiseAnd,
                SqlAssignmentOperator.BitwiseOr, SqlAssignmentOperator.BitwiseXor },
            update.Assignments.Select(assignment => assignment.Operator));
        RoundTrip(update.ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Builders_expose_native_mutation_fields()
    {
        var output = new TSqlOutputClause([new SelectItem(Sql.Col("inserted.id"))], new TableName("audit"), [new("id")]);
        var insert = Sql.InsertInto("t").DefaultValues().Output(output).Build();
        Assert.True(insert.IsDefaultValues);
        RoundTrip(insert.ToSql(SqlDialects.TSql));
        var update = Sql.Update("t").Set("id", SqlAssignmentOperator.Add, 1).Top(Sql.Lit(2)).Output(output).Build();
        Assert.Equal(SqlAssignmentOperator.Add, Assert.Single(update.Assignments).Operator);
        RoundTrip(update.ToSql(SqlDialects.TSql));
        Assert.Throws<InvalidOperationException>(() => Sql.InsertInto("t").DefaultValues().Values(1));
    }

    [Fact]
    public void Insert_options_belong_to_the_mutation_not_the_source()
    {
        var insert = Assert.IsType<InsertStatement>(Assert.Single(SqlParser.Parse(
            "INSERT t SELECT id FROM source OPTION (RECOMPILE)", SqlDialects.TSql).Statements));
        Assert.Equal(TSqlQueryOptionKind.Recompile, Assert.Single(insert.QueryOptions!).Kind);
        Assert.Null(insert.Source!.QueryOptions);
        Assert.Single(insert.FindAll<TSqlQueryOption>());
        RoundTrip(insert.ToSql(SqlDialects.TSql));
        var query = Sql.Select("id").From("source").Build() with
        {
            QueryOptions = [new(TSqlQueryOptionKind.Recompile)],
        };
        var built = Sql.InsertInto("t").From(query).Build();
        Assert.Null(built.Source!.QueryOptions);
        Assert.Single(built.QueryOptions!);
        Assert.Throws<InvalidOperationException>(() => Sql.InsertInto("t").From(query)
            .Option(new TSqlQueryOption(TSqlQueryOptionKind.MaxDop, 2)).Build());
    }

    [Fact]
    public void Mutation_ctes_are_structural_and_bind_in_schema_validation()
    {
        var insert = Assert.IsType<InsertStatement>(Assert.Single(SqlParser.Parse(
            "WITH y(id) AS (SELECT 2) INSERT t SELECT id FROM y", SqlDialects.TSql).Statements));
        var cte = Assert.Single(insert.CommonTableExpressions!);
        Assert.Single(insert.FindAll<CommonTableExpression>());
        Assert.Equal("renamed", Assert.IsType<InsertStatement>(new RenameRewriter().Visit(insert))
            .CommonTableExpressions![0].Columns![0].Value);
        var catalog = new SqlSchemaCatalog(new SqlTableSchema("t", [new SqlColumnSchema("id", "INT")]));
        var result = SqlValidator.Validate(insert.ToSql(SqlDialects.TSql), catalog, SqlDialects.TSql);
        Assert.True(result.IsValid, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        var built = Sql.InsertInto("t").With(cte).From(insert.Source!).Build();
        RoundTrip(built.ToSql(SqlDialects.TSql));
        var delete = SqlValidator.Validate("WITH y AS (SELECT id FROM t) DELETE FROM y WHERE id = 2", catalog, SqlDialects.TSql);
        Assert.True(delete.IsValid, string.Join("\n", delete.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void Unsupported_output_and_top_targets_fail_explicitly()
    {
        var insert = Sql.InsertInto("t").DefaultValues()
            .Output(new TSqlOutputClause([new SelectItem(Sql.Col("inserted.id"))])).Build();
        Assert.Throws<NotSupportedException>(() => insert.ToSql(SqlDialects.PostgreSql));
        var update = Sql.Update("t").Set("a", 1).Top(Sql.Lit(1)).Build();
        Assert.Throws<NotSupportedException>(() => update.ToSql(SqlDialects.PostgreSql));
    }

    [Fact]
    public void Schema_validation_binds_mutation_aliases_and_output_pseudo_tables()
    {
        var catalog = new SqlSchemaCatalog(
            new SqlTableSchema("target", [new SqlColumnSchema("id", "INT")]),
            new SqlTableSchema("source", [new SqlColumnSchema("id", "INT")]),
            new SqlTableSchema("audit", [new SqlColumnSchema("id", "INT")]));
        var result = SqlValidator.Validate(
            "UPDATE t SET id = s.id OUTPUT inserted.id INTO audit (id) FROM target t JOIN source s ON t.id = s.id",
            catalog, SqlDialects.TSql);
        Assert.True(result.IsValid, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        var invalid = SqlValidator.Validate(
            "DELETE FROM target OUTPUT deleted.missing INTO audit (id)", catalog, SqlDialects.TSql);
        Assert.Contains(invalid.Diagnostics, d => d.Code == SqlValidationCodes.UnknownColumn);
        var variable = SqlValidator.Validate(
            "DECLARE @rows TABLE (id INT); INSERT target OUTPUT inserted.id INTO @rows (id) VALUES (1);",
            catalog, SqlDialects.TSql);
        Assert.True(variable.IsValid, string.Join("\n", variable.Diagnostics.Select(d => d.Message)));
    }

    internal static void RoundTrip(string sql)
    {
        var document = SqlParser.Parse(sql, SqlDialects.TSql);
        var generated = document.ToSql(SqlDialects.TSql);
        var parser = new TSql180Parser(true);
        parser.Parse(new StringReader(generated), out var errors);
        Assert.True(errors.Count == 0, generated + "\n" + string.Join("\n", errors.Select(error => error.Message)));
        var reparsed = SqlParser.Parse(generated, SqlDialects.TSql);
        Assert.Equal(generated, reparsed.ToSql(SqlDialects.TSql));
        Assert.Same(document, new IdentityRewriter().Visit(document));
    }

    private sealed class IdentityRewriter : SqlRewriter;
    private sealed class RenameRewriter : SqlRewriter
    {
        protected override SqlNode VisitIdentifier(SqlIdentifier node) =>
            node.Value == "id" ? node with { Value = "renamed" } : node;
    }
}
