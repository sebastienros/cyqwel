using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;
using Cyqwel.Parsing;
using Cyqwel.Visitors;
using Cyqwel.Validation;

namespace Cyqwel.Tests;

public sealed class StoredProcedureTests
{
    private static CreateProcedureStatement CreateSample() => Sql.CreateProcedure("app.save_user")
        .Parameter("user_id", "INT")
        .Parameter("result", "INT", ProcedureParameterMode.Out)
        .Variable("changed", "INT", Sql.Lit(0))
        .Statement(Sql.Update("users")
            .Set("active", true)
            .Where(Sql.Col("id").EqualTo(Sql.Local("user_id")))
            .Build())
        .If(
            Sql.Local("changed").GreaterThan(Sql.Lit(0)),
            [Sql.Return()])
        .Build();

    [Fact]
    public void Capabilities_are_exposed_by_dialect()
    {
        Assert.True(SqlDialects.Generic.SupportsStoredProcedures);
        Assert.True(SqlDialects.TSql.SupportsStoredProcedures);
        Assert.True(SqlDialects.PostgreSql.SupportsStoredProcedures);
        Assert.True(SqlDialects.MySql.SupportsStoredProcedures);
        Assert.True(SqlDialects.Oracle.SupportsStoredProcedures);
        Assert.False(SqlDialects.Sqlite.SupportsStoredProcedures);
        Assert.False(new SqlDialect("custom-defaults").SupportsStoredProcedures);
        Assert.False(new SqlDialect("custom-defaults").SupportsAnonymousProceduralBlocks);

        Assert.True(SqlDialects.Generic.SupportsAnonymousProceduralBlocks);
        Assert.True(SqlDialects.TSql.SupportsAnonymousProceduralBlocks);
        Assert.True(SqlDialects.PostgreSql.SupportsAnonymousProceduralBlocks);
        Assert.False(SqlDialects.MySql.SupportsAnonymousProceduralBlocks);
        Assert.True(SqlDialects.Oracle.SupportsAnonymousProceduralBlocks);
        Assert.False(SqlDialects.Sqlite.SupportsAnonymousProceduralBlocks);

        var custom = SqlDialectBuilder.Create("custom-procedures")
            .BasedOn(SqlDialects.Sqlite)
            .ConfigureParser(options => options with
            {
                SupportsStoredProcedures = true,
            })
            .Build();
        Assert.True(custom.SupportsStoredProcedures);
        Assert.StartsWith("CREATE PROCEDURE", CreateSample().ToSql(custom));

        var customBlocks = SqlDialectBuilder.Create("custom-blocks")
            .BasedOn(SqlDialects.Sqlite)
            .ConfigureParser(options => options with
            {
                SupportsAnonymousProceduralBlocks = true,
            })
            .Build();
        Assert.True(customBlocks.SupportsAnonymousProceduralBlocks);
        Assert.False(customBlocks.SupportsStoredProcedures);
        Assert.IsType<ProceduralBlock>(customBlocks.Parse(
            "BEGIN ATOMIC RETURN; END").Statements[0]);
        Assert.Equal("BEGIN ATOMIC RETURN; END", Sql.Block().Return().Build().ToSql(customBlocks));
    }

    [Fact]
    public void Builders_generate_native_procedure_syntax()
    {
        var procedure = CreateSample();

        Assert.StartsWith("CREATE PROCEDURE app.save_user @user_id INT, @result INT OUTPUT AS BEGIN", procedure.ToSql(SqlDialects.TSql));
        Assert.Contains("LANGUAGE PLPGSQL AS $cyqwel$", procedure.ToSql(SqlDialects.PostgreSql));
        Assert.Contains("cyqwel_body: BEGIN", procedure.ToSql(SqlDialects.MySql));
        Assert.Contains("CREATE PROCEDURE app.save_user(user_id IN INT, result OUT INT) AS", procedure.ToSql(SqlDialects.Oracle));
        Assert.Contains("LEAVE cyqwel_body", procedure.ToSql(SqlDialects.MySql));
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Native_syntax_round_trips(SqlDialect dialect, string sql)
    {
        var document = dialect.Parse(sql);
        var generated = document.ToSql(dialect);
        Assert.Equal(sql, generated);
        Assert.Single(document.FindAll<CreateProcedureStatement>());
    }

    public static IEnumerable<object[]> RoundTripCases()
    {
        yield return [SqlDialects.Generic, "CREATE PROCEDURE p(IN x INT) BEGIN ATOMIC RETURN; END"];
        yield return [SqlDialects.TSql, "CREATE PROCEDURE p @x INT AS BEGIN RETURN; END"];
        yield return [SqlDialects.PostgreSql, "CREATE PROCEDURE p(IN x INT) LANGUAGE PLPGSQL AS $cyqwel$ BEGIN RETURN; END $cyqwel$"];
        yield return [SqlDialects.MySql, "CREATE PROCEDURE p(IN x INT) cyqwel_body: BEGIN LEAVE cyqwel_body; END cyqwel_body"];
        yield return [SqlDialects.Oracle, "CREATE PROCEDURE p(x IN INT) AS BEGIN RETURN; END"];
    }

    [Fact]
    public void Calls_generate_native_syntax()
    {
        var call = Sql.CallProcedure("app.save_user")
            .NamedArgument("user_id", 42)
            .NamedArgument("result", Sql.Local("result"), output: true)
            .Build();

        Assert.Equal("EXEC app.save_user @user_id = 42, @result = @result OUTPUT", call.ToSql(SqlDialects.TSql));
        Assert.Equal("CALL app.save_user(user_id => 42, result => result)", call.ToSql(SqlDialects.PostgreSql));
        Assert.Throws<NotSupportedException>(() => call.ToSql(SqlDialects.MySql));
    }

    [Fact]
    public void Lifecycle_and_calls_parse_to_dedicated_nodes()
    {
        Assert.IsType<ReplaceProcedureStatement>(SqlDialects.TSql.Parse(
            "CREATE OR ALTER PROCEDURE p @x INT = 1 OUTPUT AS BEGIN IF @x > 0 BEGIN RETURN; END END")
            .Statements[0]);
        Assert.IsType<ReplaceProcedureStatement>(SqlDialects.PostgreSql.Parse(
            "CREATE OR REPLACE PROCEDURE p(INOUT x INT DEFAULT 1) LANGUAGE PLPGSQL AS $cyqwel$ BEGIN IF x > 0 THEN RETURN; END IF; END $cyqwel$")
            .Statements[0]);
        Assert.IsType<DropProcedureStatement>(SqlDialects.PostgreSql.Parse(
            "DROP PROCEDURE IF EXISTS p(INT)").Statements[0]);
        Assert.IsType<CallProcedureStatement>(SqlDialects.Generic.Parse(
            "CALL p(x => 1)").Statements[0]);
        Assert.IsType<CallProcedureStatement>(SqlDialects.TSql.Parse(
            "EXEC p @x = @result OUTPUT").Statements[0]);
    }

    [Fact]
    public void TSql_proc_shorthand_parses_to_procedure_nodes()
    {
        var create = Assert.IsType<CreateProcedureStatement>(SqlDialects.TSql.Parse(
            "CREATE PROC p AS BEGIN RETURN; END").Statements[0]);
        var createOrAlter = Assert.IsType<ReplaceProcedureStatement>(SqlDialects.TSql.Parse(
            "CREATE OR ALTER PROC p AS BEGIN RETURN; END").Statements[0]);
        var alter = Assert.IsType<ReplaceProcedureStatement>(SqlDialects.TSql.Parse(
            "ALTER PROC p AS BEGIN RETURN; END").Statements[0]);
        var drop = Assert.IsType<DropProcedureStatement>(SqlDialects.TSql.Parse(
            "DROP PROC p").Statements[0]);

        Assert.StartsWith("CREATE PROCEDURE p", create.ToSql(SqlDialects.TSql));
        Assert.StartsWith("CREATE OR ALTER PROCEDURE p", createOrAlter.ToSql(SqlDialects.TSql));
        Assert.StartsWith("CREATE OR ALTER PROCEDURE p", alter.ToSql(SqlDialects.TSql));
        Assert.Equal("DROP PROCEDURE p", drop.ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Parser_normalizes_local_references()
    {
        var procedure = Assert.IsType<CreateProcedureStatement>(SqlDialects.TSql.Parse(
            "CREATE PROCEDURE p @x INT AS BEGIN SELECT @x; END").Statements[0]);
        Assert.Single(procedure.FindAll<LocalVariableExpression>());
        Assert.Empty(procedure.FindAll<ParameterExpression>());
        Assert.Equal(
            "CREATE PROCEDURE p(IN x INT) BEGIN ATOMIC SELECT x; END",
            procedure.ToSql(SqlDialects.Generic));
    }

    [Fact]
    public void TSql_parser_does_not_rewrite_bare_columns_as_parameters()
    {
        const string sql = "CREATE PROCEDURE p @x INT AS BEGIN SELECT x, @x FROM t; END";

        var procedure = Assert.IsType<CreateProcedureStatement>(
            SqlDialects.TSql.Parse(sql).Statements[0]);

        Assert.Single(procedure.FindAll<ColumnExpression>());
        Assert.Single(procedure.FindAll<LocalVariableExpression>());
        Assert.Equal(sql, procedure.ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Dialect_parser_rejects_foreign_procedural_control_flow()
    {
        Assert.Throws<SqlParseException>(() => SqlDialects.PostgreSql.Parse(
            "CREATE PROCEDURE p(IN x INT) LANGUAGE PLPGSQL AS $cyqwel$ BEGIN IF x > 0 BEGIN RETURN; END; END $cyqwel$"));
        Assert.Throws<SqlParseException>(() => SqlDialects.PostgreSql.Parse(
            "CREATE PROCEDURE p() LANGUAGE PLPGSQL AS $cyqwel$ BEGIN BREAK; END $cyqwel$"));
        Assert.Throws<SqlParseException>(() => SqlDialects.MySql.Parse(
            "CREATE PROCEDURE p() cyqwel_body: BEGIN RETURN; END cyqwel_body"));
        Assert.Throws<SqlParseException>(() => SqlDialects.TSql.Parse(
            "CREATE PROCEDURE p AS BEGIN EXIT; END"));
    }

    [Fact]
    public void Parser_rejects_loop_control_targeting_an_outer_loop()
    {
        const string sql = "CREATE PROCEDURE p() cyqwel_body: BEGIN cyqwel_loop_1: WHILE 1 = 1 DO cyqwel_loop_2: WHILE 1 = 1 DO LEAVE cyqwel_loop_1; END WHILE; END WHILE; END cyqwel_body";

        var exception = Assert.Throws<SqlParseException>(() => SqlDialects.MySql.Parse(sql));
        Assert.Equal(SqlParseErrorCode.DialectIncompatible, exception.Error.Code);
    }

    [Fact]
    public void Structural_validation_reports_procedure_errors()
    {
        var result = SqlValidator.Validate(
            "CREATE PROCEDURE p(IN x INT DEFAULT 1, IN x INT) BEGIN ATOMIC RETURN; END",
            SqlDialects.Generic,
            new SqlValidationOptions { Semantic = true });

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.DuplicateProceduralSymbol);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidProcedureDefault);
    }

    [Fact]
    public void Lifecycle_generation_is_dialect_aware()
    {
        var replace = Sql.ReplaceProcedure("p").Return().Build();
        Assert.StartsWith("CREATE OR ALTER PROCEDURE p AS", replace.ToSql(SqlDialects.TSql));
        Assert.StartsWith("CREATE OR REPLACE PROCEDURE p()", replace.ToSql(SqlDialects.PostgreSql));
        Assert.Throws<NotSupportedException>(() => replace.ToSql(SqlDialects.MySql));
        Assert.Equal(string.Empty, replace.ToSql(SqlDialects.MySql,
            new SqlGenerationOptions { UnsupportedBehavior = UnsupportedSqlBehavior.Ignore }));

        Assert.Equal("DROP PROCEDURE IF EXISTS p(INT)",
            Sql.DropProcedure("p").IfExists().ParameterType("INT").ToSql(SqlDialects.PostgreSql));
    }

    [Fact]
    public void PostgreSql_rejects_a_conflicting_dollar_quote_tag_safely()
    {
        var procedure = Sql.CreateProcedure("p")
            .Statement(Sql.Select(Sql.Lit("$cyqwel$")).Build())
            .Build();

        Assert.Throws<NotSupportedException>(() => procedure.ToSql(SqlDialects.PostgreSql));
        Assert.Equal(string.Empty, procedure.ToSql(SqlDialects.PostgreSql,
            new SqlGenerationOptions { UnsupportedBehavior = UnsupportedSqlBehavior.Ignore }));
    }

    [Fact]
    public void Oracle_omits_empty_parameter_parentheses()
    {
        var sql = Sql.CreateProcedure("p").Return().ToSql(SqlDialects.Oracle);
        Assert.Equal("CREATE PROCEDURE p AS BEGIN RETURN; END", sql);
        Assert.Equal(sql, SqlDialects.Oracle.Parse(sql).ToSql(SqlDialects.Oracle));
    }

    [Fact]
    public void MySql_rejects_defaults_and_supports_output_variables()
    {
        var exception = Assert.Throws<SqlParseException>(() => SqlDialects.MySql.Parse(
            "CREATE PROCEDURE p(IN x INT DEFAULT 1) cyqwel_body: BEGIN LEAVE cyqwel_body; END cyqwel_body"));
        Assert.Equal(SqlParseErrorCode.DialectIncompatible, exception.Error.Code);

        var call = new CallProcedureStatement(
            new TableName("p"),
            [new ProcedureArgument(Sql.Local("result"), IsOutput: true)]);
        Assert.Equal("CALL p(@result)", call.ToSql(SqlDialects.MySql));
        Assert.Equal("CALL p(@result)", SqlDialects.MySql.Parse("CALL p(@result)").ToSql(SqlDialects.MySql));
    }

    [Fact]
    public void Sqlite_throws_or_omits_complete_statements()
    {
        var create = Sql.CreateProcedure("p").Return().Build();
        Assert.Throws<NotSupportedException>(() => create.ToSql(SqlDialects.Sqlite));
        Assert.Equal(string.Empty, create.ToSql(SqlDialects.Sqlite,
            new SqlGenerationOptions { UnsupportedBehavior = UnsupportedSqlBehavior.Ignore }));

        var document = Sql.Document(
            Sql.Select("id").From("users").Build(),
            create,
            Sql.Select("name").From("users").Build());
        Assert.Equal($"SELECT id FROM users;{Environment.NewLine}SELECT name FROM users", document.ToSql(
            SqlDialects.Sqlite,
            new SqlGenerationOptions { UnsupportedBehavior = UnsupportedSqlBehavior.Ignore }));

        var exception = Assert.Throws<SqlParseException>(() =>
            SqlDialects.Sqlite.Parse("CALL p()"));
        Assert.Equal(SqlParseErrorCode.DialectIncompatible, exception.Error.Code);

        var unsupportedDialect = new SqlDialect("custom-defaults");
        var embeddedException = Assert.Throws<SqlParseException>(() =>
            unsupportedDialect.Parse("SELECT 1; CALL p()"));
        Assert.Equal(SqlParseErrorCode.DialectIncompatible, embeddedException.Error.Code);
    }

    [Fact]
    public void Visitors_and_rewriters_include_procedure_nodes()
    {
        var procedure = CreateSample();
        Assert.NotEmpty(procedure.FindAll<ProcedureParameter>());
        Assert.NotEmpty(procedure.FindAll<LocalVariable>());
        Assert.NotEmpty(procedure.FindAll<LocalVariableExpression>());
        Assert.Single(procedure.FindAll<ProceduralBlock>());
        Assert.Single(procedure.FindAll<ProceduralIfStatement>());
        Assert.Same(procedure, procedure.Accept(new IdentityRewriter()));
    }

    [Fact]
    public void Procedure_pretty_printing_uses_line_breaks()
    {
        var sql = Sql.CreateProcedure("p")
            .Statement(Sql.Select("id").From("users").Build())
            .Return()
            .ToSql(SqlDialects.Generic, new SqlGenerationOptions { PrettyPrint = true });

        Assert.Contains(Environment.NewLine, sql);
        Assert.Contains("SELECT", sql);
        Assert.Contains("RETURN", sql);
    }

    [Fact]
    public void While_builder_generates_portable_native_syntax()
    {
        var procedure = Sql.CreateProcedure("p")
            .Parameter("x", "INT")
            .While(
                Sql.Local("x").GreaterThan(Sql.Lit(0)),
                [
                    Sql.If(
                        Sql.Local("x").EqualTo(Sql.Lit(1)),
                        [Sql.Break()]),
                    Sql.Continue(),
                ])
            .Build();

        Assert.Contains("WHILE @x > 0 BEGIN IF @x = 1 BEGIN BREAK; END; CONTINUE; END",
            procedure.ToSql(SqlDialects.TSql));
        Assert.Contains("WHILE x > 0 LOOP IF x = 1 THEN EXIT; END IF; CONTINUE; END LOOP",
            procedure.ToSql(SqlDialects.PostgreSql));
        Assert.Contains("cyqwel_loop_1: WHILE x > 0 DO IF x = 1 THEN LEAVE cyqwel_loop_1; END IF; ITERATE cyqwel_loop_1; END WHILE",
            procedure.ToSql(SqlDialects.MySql));
        Assert.Contains("WHILE x > 0 LOOP IF x = 1 THEN EXIT; END IF; CONTINUE; END LOOP",
            procedure.ToSql(SqlDialects.Oracle));
        Assert.Contains("cyqwel_loop_1: WHILE x > 0 DO IF x = 1 THEN LEAVE cyqwel_loop_1; END IF; ITERATE cyqwel_loop_1; END WHILE",
            procedure.ToSql(SqlDialects.Generic));
        Assert.Same(procedure, procedure.Accept(new IdentityRewriter()));
    }

    [Theory]
    [MemberData(nameof(WhileRoundTripCases))]
    public void While_syntax_round_trips(SqlDialect dialect, string sql)
    {
        var document = dialect.Parse(sql);

        Assert.Equal(sql, document.ToSql(dialect));
        Assert.Single(document.FindAll<ProceduralWhileStatement>());
        Assert.Single(document.FindAll<ProceduralBreakStatement>());
        Assert.Single(document.FindAll<ProceduralContinueStatement>());
    }

    public static IEnumerable<object[]> WhileRoundTripCases()
    {
        yield return [SqlDialects.Generic, "CREATE PROCEDURE p(IN x INT) BEGIN ATOMIC cyqwel_loop_1: WHILE x > 0 DO LEAVE cyqwel_loop_1; ITERATE cyqwel_loop_1; END WHILE; END"];
        yield return [SqlDialects.TSql, "CREATE PROCEDURE p @x INT AS BEGIN WHILE @x > 0 BEGIN BREAK; CONTINUE; END; END"];
        yield return [SqlDialects.PostgreSql, "CREATE PROCEDURE p(IN x INT) LANGUAGE PLPGSQL AS $cyqwel$ BEGIN WHILE x > 0 LOOP EXIT; CONTINUE; END LOOP; END $cyqwel$"];
        yield return [SqlDialects.MySql, "CREATE PROCEDURE p(IN x INT) cyqwel_body: BEGIN cyqwel_loop_1: WHILE x > 0 DO LEAVE cyqwel_loop_1; ITERATE cyqwel_loop_1; END WHILE; END cyqwel_body"];
        yield return [SqlDialects.Oracle, "CREATE PROCEDURE p(x IN INT) AS BEGIN WHILE x > 0 LOOP EXIT; CONTINUE; END LOOP; END"];
    }

    [Fact]
    public void Nested_mysql_loops_receive_distinct_labels()
    {
        var procedure = Sql.CreateProcedure("p")
            .While(Sql.Lit(true),
            [
                Sql.While(Sql.Lit(true), [Sql.Break()]),
                Sql.Continue(),
            ])
            .Build();

        var sql = procedure.ToSql(SqlDialects.MySql);
        Assert.Contains("cyqwel_loop_1: WHILE", sql);
        Assert.Contains("cyqwel_loop_2: WHILE", sql);
        Assert.Contains("LEAVE cyqwel_loop_2", sql);
        Assert.Contains("ITERATE cyqwel_loop_1", sql);
        Assert.Equal(sql, SqlDialects.MySql.Parse(sql).ToSql(SqlDialects.MySql));
    }

    [Fact]
    public void Validation_rejects_loop_control_outside_while()
    {
        var result = SqlValidator.Validate(
            "CREATE PROCEDURE p() LANGUAGE PLPGSQL AS $cyqwel$ BEGIN EXIT; CONTINUE; END $cyqwel$",
            SqlDialects.PostgreSql,
            new SqlValidationOptions { Semantic = true });

        Assert.Equal(2, result.Diagnostics.Count(diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidLoopControl));
        Assert.Throws<NotSupportedException>(() => Sql.Break().ToSql(SqlDialects.Generic));
    }

    [Fact]
    public void Schema_validation_descends_into_nested_procedural_blocks()
    {
        var nestedBlock = Sql.Block()
            .Statement(Sql.Block()
                .Statement(Sql.Select("id").From("missing").Build())
                .Build())
            .Build();
        var diagnostics = new List<SqlValidationDiagnostic>();

        new SchemaValidationEngine(
            string.Empty,
            new SqlSchemaCatalog(),
            SqlSchemaValidationOptions.Default,
            diagnostics).Validate(Sql.Document(nestedBlock));

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.UnknownTable);
    }

    [Fact]
    public void Nested_procedural_blocks_preserve_the_enclosing_loop_context()
    {
        var block = Sql.Block()
            .While(
                Sql.Lit(true),
                [Sql.Block().Break().Continue().Build()])
            .Build();

        var result = SqlValidator.Validate(
            block.ToSql(SqlDialects.Generic),
            SqlDialects.Generic,
            new SqlValidationOptions { Semantic = true });

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidLoopControl);
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("tsql")]
    [InlineData("postgresql")]
    [InlineData("oracle")]
    public void Anonymous_procedural_blocks_round_trip(string dialectName)
    {
        var dialect = SqlDialects.BuiltIn.Single(value => value.Name == dialectName);
        var block = Sql.Block()
            .Variable("x", "INT", Sql.Lit(0))
            .While(Sql.Local("x").LessThan(Sql.Lit(10)),
            [
                Sql.If(Sql.Local("x").EqualTo(Sql.Lit(5)), [Sql.Break()]),
                Sql.Continue(),
            ])
            .Return()
            .Build();

        var sql = block.ToSql(dialect);
        var parsed = dialect.Parse(sql);

        Assert.Equal(sql, parsed.ToSql(dialect));
        Assert.Single(parsed.FindAll<ProceduralBlock>());
        Assert.NotEmpty(parsed.FindAll<LocalVariableExpression>());
    }

    [Fact]
    public void Anonymous_blocks_throw_or_are_cleanly_omitted_when_unsupported()
    {
        var block = Sql.Block().Return().Build();

        Assert.Throws<NotSupportedException>(() => block.ToSql(SqlDialects.MySql));
        Assert.Throws<NotSupportedException>(() => block.ToSql(SqlDialects.Sqlite));
        Assert.Equal(string.Empty, block.ToSql(SqlDialects.MySql,
            new SqlGenerationOptions { UnsupportedBehavior = UnsupportedSqlBehavior.Ignore }));

        var document = Sql.Document(
            Sql.Select(Sql.Lit(1)).Build(),
            block,
            Sql.Select(Sql.Lit(2)).Build());
        Assert.Equal($"SELECT 1;{Environment.NewLine}SELECT 2", document.ToSql(
            SqlDialects.MySql,
            new SqlGenerationOptions { UnsupportedBehavior = UnsupportedSqlBehavior.Ignore }));

        var error = Assert.Throws<SqlParseException>(() =>
            SqlDialects.MySql.Parse("BEGIN ATOMIC RETURN; END"));
        Assert.Equal(SqlParseErrorCode.DialectIncompatible, error.Error.Code);
    }

    [Fact]
    public void TSql_allows_top_level_procedural_control_flow()
    {
        const string sql = "WHILE 1 = 1 BEGIN IF 1 = 1 BEGIN BREAK; END; CONTINUE; END";

        var parsed = SqlDialects.TSql.Parse(sql);
        Assert.Equal(sql, parsed.ToSql(SqlDialects.TSql));

        var validation = SqlValidator.Validate(
            sql,
            SqlDialects.TSql,
            new SqlValidationOptions { Semantic = true });
        Assert.DoesNotContain(validation.Diagnostics, diagnostic =>
            diagnostic.Code is SqlValidationCodes.InvalidProceduralContext
                or SqlValidationCodes.InvalidLoopControl);

        Assert.Throws<NotSupportedException>(() => Sql.Return().ToSql(SqlDialects.PostgreSql));
        Assert.Equal(string.Empty, Sql.Return().ToSql(
            SqlDialects.PostgreSql,
            new SqlGenerationOptions { UnsupportedBehavior = UnsupportedSqlBehavior.Ignore }));
    }

    [Fact]
    public void TSql_top_level_loop_control_only_reports_loop_depth_errors()
    {
        var validation = SqlValidator.Validate(
            "BREAK; CONTINUE",
            SqlDialects.TSql,
            new SqlValidationOptions { Semantic = true });

        Assert.Equal(2, validation.Diagnostics.Count(diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidLoopControl));
        Assert.DoesNotContain(validation.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidProceduralContext);
    }

    private sealed class IdentityRewriter : SqlRewriter;
}
