using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;
using Cyqwel.Parsing;
using Cyqwel.Validation;
using Cyqwel.Visitors;
using TSql180Parser = Microsoft.SqlServer.TransactSql.ScriptDom.TSql180Parser;

namespace Cyqwel.Tests;

public sealed class TSqlScriptCoreTests
{
    [Theory]
    [InlineData("DECLARE @TestVariable AS VARCHAR(100) = 'Save Our Planet'")]
    [InlineData("DECLARE @v1 AS INTEGER = 1, @v2 AS CHAR(1) = 'c'")]
    [InlineData("DECLARE @DWH_DateCreated AS DATETIME2 = CONVERT(DATETIME2, GETDATE(), 104)")]
    [InlineData("DECLARE @X INT")]
    [InlineData("DECLARE @X INT = 1")]
    [InlineData("DECLARE @X INT, @Y VARCHAR(10)")]
    [InlineData("declare @X int = (select col from [table] where id = 1)")]
    [InlineData("declare @X TABLE (Id INT NOT NULL, Name VARCHAR(100) NOT NULL)")]
    [InlineData("declare @X UserDefinedTableType")]
    [InlineData("DECLARE @MyTableVar TABLE (EmpID INT NOT NULL, PRIMARY KEY CLUSTERED (EmpID), UNIQUE NONCLUSTERED (EmpID), INDEX CustomNonClusteredIndex NONCLUSTERED (EmpID))")]
    [InlineData("BEGIN TRANSACTION")]
    [InlineData("BEGIN TRAN")]
    [InlineData("BEGIN TRANSACTION transaction_name")]
    [InlineData("BEGIN TRANSACTION @tran_name_variable")]
    [InlineData("BEGIN TRANSACTION transaction_name WITH MARK 'description'")]
    [InlineData("COMMIT")]
    [InlineData("COMMIT TRAN")]
    [InlineData("COMMIT TRANSACTION")]
    [InlineData("COMMIT TRANSACTION transaction_name")]
    [InlineData("COMMIT TRANSACTION @tran_name_variable")]
    [InlineData("ROLLBACK")]
    [InlineData("ROLLBACK TRAN")]
    [InlineData("ROLLBACK TRANSACTION")]
    [InlineData("ROLLBACK TRANSACTION transaction_name")]
    [InlineData("ROLLBACK TRANSACTION @tran_name_variable")]
    [InlineData("EXEC sp_executesql @payload")]
    public void Core_declarations_calls_and_transactions_round_trip(string sql) => RoundTrip(sql);

    [Theory]
    [InlineData("COMMIT TRANSACTION @tran_name_variable WITH (DELAYED_DURABILITY = ON)", true)]
    [InlineData("COMMIT TRANSACTION transaction_name WITH (DELAYED_DURABILITY = OFF)", false)]
    [InlineData("COMMIT WITH (DELAYED_DURABILITY = ON)", true)]
    [InlineData("COMMIT TRAN WITH (DELAYED_DURABILITY = OFF)", false)]
    [InlineData("COMMIT WORK WITH (DELAYED_DURABILITY = ON)", true)]
    public void Commit_delayed_durability_is_a_typed_bounded_toggle(string sql, bool enabled)
    {
        using var reader = new StringReader(sql);
        new TSql180Parser(true).Parse(reader, out var errors);
        Assert.Empty(errors);
        var document = RoundTrip(sql);
        var transaction = Assert.IsType<TransactionStatement>(Assert.Single(document.Statements));
        Assert.Equal(enabled, transaction.DelayedDurability);
        Assert.Same(document, new NoOpRewriter().Visit(document));
        Assert.Empty(SqlValidator.Validate(sql, new SqlSchemaCatalog(), SqlDialects.TSql,
            new SqlSchemaValidationOptions { Semantic = true, CheckTypes = true }).Diagnostics);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void Transaction_builder_and_rewriter_preserve_nullable_durability(bool? enabled)
    {
        var block = Sql.Block().Transaction(TransactionKind.Commit, Sql.Col("x"),
            delayedDurability: enabled).Build();
        var rewritten = new RenameX().Visit(block);
        var transaction = Assert.Single(rewritten.FindAll<TransactionStatement>());
        Assert.Equal(enabled, transaction.DelayedDurability);
        Assert.Equal("renamed", Assert.IsType<ColumnExpression>(transaction.Name).Parts[0].Value);
        var generated = rewritten.ToSql(SqlDialects.TSql);
        var reparsed = RoundTrip(generated);
        Assert.Equal(enabled, Assert.Single(reparsed.FindAll<TransactionStatement>()).DelayedDurability);
        Assert.Throws<NotSupportedException>(() => rewritten.ToSql(SqlDialects.PostgreSql));
    }

    [Theory]
    [InlineData(TransactionKind.Begin)]
    [InlineData(TransactionKind.Rollback)]
    public void Durability_is_rejected_on_non_commit_transaction_nodes(TransactionKind kind)
    {
        var block = Sql.Block().Transaction(kind, delayedDurability: false).Build();
        Assert.Throws<InvalidOperationException>(() => block.ToSql(SqlDialects.TSql));
    }

    [Theory]
    [InlineData("EXEC p")]
    [InlineData("EXECUTE @return_status = dbo.MyProc @a, @b")]
    [InlineData("EXEC @RC = dbo.MyProc @id = 7")]
    [InlineData("PRINT @TestVariable")]
    [InlineData("DECLARE @a$#b INT; SET @a$#b = 1; EXEC @a$#b = dbo.p @a$#b OUTPUT")]
    [InlineData("DECLARE @sql NVARCHAR(MAX) = N'SELECT '; EXEC(@sql + N'1')")]
    [InlineData("EXEC p (1, 2)")]
    [InlineData("EXECUTE dbo.p 1, @output OUTPUT")]
    [InlineData("EXEC p @x = 1, @y = @output OUT")]
    [InlineData("CREATE PROCEDURE p(@x INT, @y INT = 1 OUTPUT) AS SELECT @x; RETURN @y")]
    [InlineData("CREATE PROCEDURE p @x INT AS BEGIN SET NOCOUNT ON; IF @x > 0 SET @x -= 1 ELSE SET XACT_ABORT OFF; WHILE @x > 0 BEGIN SET @x -= 1; END; RETURN @x; END")]
    [InlineData("CREATE PROCEDURE p AS BEGIN DECLARE @x INT = 100; IF @x > ANY (SELECT 100) BEGIN SET @x = 100 END ELSE BEGIN SET @x = 0 END END")]
    [InlineData("DECLARE @x INT = 1 SELECT @x SET @x += 1 DECLARE @y AS INT = @x RETURN @y")]
    [InlineData("BEGIN TRAN t WITH MARK N'mark'; COMMIT TRAN t; BEGIN TRAN; ROLLBACK WORK")]
    public void Application_scripts_round_trip(string sql) => RoundTrip(sql);

    [Fact]
    public void Batch_boundaries_preserve_procedure_ownership()
    {
        const string sql = """
            CREATE PROCEDURE dbo.p AS
            SELECT 'GO' AS [GO]
            -- GO is not a separator here
            DECLARE @x INT = 1
            IF @x = 1 BEGIN SET @x += 1 END ELSE RETURN 0
            RETURN @x
            GO -- end definition
            EXEC dbo.p
            GO
            SELECT 2
            """;
        var document = RoundTrip(sql);
        Assert.Equal(3, document.Batches!.Count);
        Assert.Equal(3, document.Statements.Count);
        var procedure = Assert.IsType<CreateProcedureStatement>(document.Statements[0]);
        Assert.Equal(4, procedure.Body.Statements.Count);
        Assert.Empty(procedure.Body.Variables);
        Assert.IsType<DeclareStatement>(procedure.Body.Statements[1]);
        Assert.IsType<CallProcedureStatement>(document.Statements[1]);
        Assert.Equal(2, document.Batches.Count(batch => batch.IsTerminated));
    }

    [Fact]
    public void Flat_document_builders_separate_definitions_from_following_statements()
    {
        var document = Sql.Document(
            Sql.CreateProcedure("p").Return(Sql.Lit(1)).Build(),
            Sql.CallProcedure("p").Build(),
            Sql.CreateProcedure("q").Return().Build());
        var parsed = RoundTrip(document.ToSql(SqlDialects.TSql));
        Assert.Equal(3, parsed.Batches!.Count);
        Assert.Equal(3, parsed.Statements.Count);
        Assert.IsType<CallProcedureStatement>(parsed.Statements[1]);
    }

    [Theory]
    [InlineData("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN DELETE;")]
    [InlineData("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN DELETE; SELECT 1")]
    [InlineData("MERGE t USING s ON t.id = s.id WHEN MATCHED THEN DELETE;\nGO\nSELECT 1")]
    [InlineData("CREATE PROCEDURE p AS BEGIN MERGE t USING s ON t.id = s.id WHEN MATCHED THEN DELETE; RETURN END")]
    public void Merge_statement_boundaries_have_exactly_one_terminator(string sql)
    {
        var document = RoundTrip(sql);
        Assert.DoesNotContain(";;", document.ToSql(SqlDialects.TSql));
        if (document.Statements[^1] is MergeStatement)
            Assert.EndsWith(";", document.ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Declarations_and_nested_control_flow_keep_source_order()
    {
        var document = RoundTrip("""
            CREATE PROCEDURE p @in1 INT AS BEGIN
              SELECT 1;
              DECLARE @q1 INT, @q2 INT;
              SET @q1 = (SELECT MAX(col1) FROM t1);
              IF @in1 > 1 BEGIN
                SELECT 3;
                DECLARE @q3 INT;
                SET @q3 = 4;
                IF @q3 < 5 SET @q2 = 1; ELSE SET @q2 = 2;
              END;
              WHILE @q1 > 1 BEGIN SET @q1 -= 1; CONTINUE; END;
              RETURN @q1;
            END
            """);
        var body = Assert.IsType<CreateProcedureStatement>(Assert.Single(document.Statements)).Body;
        Assert.Empty(body.Variables);
        Assert.IsType<SelectStatement>(body.Statements[0]);
        Assert.Equal(2, Assert.IsType<DeclareStatement>(body.Statements[1]).Variables.Count);
        var condition = Assert.IsType<ProceduralIfStatement>(body.Statements[3]);
        Assert.IsType<DeclareStatement>(condition.Then[1]);
        Assert.IsType<ProceduralIfStatement>(condition.Then[3]);
        Assert.IsType<LocalVariableExpression>(Assert.IsType<ProceduralReturnStatement>(body.Statements[^1]).Value);
        Assert.Equal(SqlAssignmentOperator.Subtract, Assert.Single(body.FindAll<SetVariableStatement>(),
            statement => statement.Operator != SqlAssignmentOperator.Assign).Operator);
    }

    [Fact]
    public void Dynamic_sql_is_literal_data_and_not_a_body()
    {
        var document = RoundTrip("""
            CREATE PROCEDURE p @TableName NVARCHAR(128) AS BEGIN
            DECLARE @SQL NVARCHAR(MAX);
            SET @SQL = N'DROP TABLE IF EXISTS [' + @TableName + ']';
            EXECUTE sp_executesql 'SELECT 1 AS c';
            EXECUTE sp_executesql N'GO; CREATE PROCEDURE not_a_definition AS SELECT 1';
            EXECUTE sp_executesql @SQL;
            EXECUTE sp_executesql @stmt = @SQL;
            END
            """);
        Assert.Single(document.FindAll<CreateProcedureStatement>());
        Assert.Empty(document.FindAll<DropStatement>());
        Assert.Equal(4, document.FindAll<CallProcedureStatement>().Count());
        Assert.Contains(document.FindAll<LiteralExpression>(), literal =>
            literal.IsNational && Equals(literal.Value, "GO; CREATE PROCEDURE not_a_definition AS SELECT 1"));
    }

    [Theory]
    [InlineData("IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('db.tbl') AND name = 'idx') EXEC('CREATE INDEX idx ON db.tbl')")]
    [InlineData("IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = 'foo') EXEC('CREATE SCHEMA foo')")]
    [InlineData("IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'baz' AND TABLE_SCHEMA = 'bar' AND TABLE_CATALOG = 'foo') EXEC('CREATE TABLE foo.bar.baz (a INTEGER)')")]
    [InlineData("IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'baz' AND TABLE_SCHEMA = 'bar' AND TABLE_CATALOG = 'foo') EXEC('SELECT * INTO foo.bar.baz FROM (SELECT ''2020'' AS z FROM a.b.c) AS temp')")]
    [InlineData("IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'baz' AND TABLE_SCHEMA = 'bar' AND TABLE_CATALOG = 'foo') EXEC('WITH cte1 AS (SELECT 1 AS col_a), cte2 AS (SELECT 1 AS col_b) SELECT * INTO foo.bar.baz FROM (SELECT col_a FROM cte1 UNION ALL SELECT col_b FROM cte2) AS temp')")]
    public void Conditional_dynamic_commands_remain_data(string sql)
    {
        var document = RoundTrip(sql);
        var conditional = Assert.IsType<ProceduralIfStatement>(Assert.Single(document.Statements));
        var execute = Assert.IsType<ExecuteSqlStatement>(Assert.Single(conditional.Then));
        Assert.IsType<LiteralExpression>(execute.Command);
        Assert.Empty(document.FindAll<CreateTableStatement>());
        Assert.Empty(document.FindAll<CreateIndexStatement>());
    }

    [Fact]
    public void Execution_print_and_return_capture_survive_builders_and_rewriters()
    {
        var call = Sql.CallProcedure("dbo.p").Argument(new ParameterExpression("input")).ReturnInto("x").Build();
        var block = Sql.Block().Variable("x", "INT")
            .Statement(call).Print(Sql.Local("x")).ExecuteSql(Sql.Lit("GO; this is data, not a parsed statement")).Build();
        var document = RoundTrip(block.ToSql(SqlDialects.TSql));
        Assert.Equal("x", Assert.Single(document.FindAll<CallProcedureStatement>()).ReturnVariable!.Value);
        Assert.Same(document, new NoOpRewriter().Visit(document));
        var renamed = new RenameX().Visit(document);
        Assert.Equal("renamed", Assert.Single(renamed.FindAll<CallProcedureStatement>()).ReturnVariable!.Value);
        Assert.IsType<LocalVariableExpression>(Assert.Single(renamed.FindAll<PrintStatement>()).Value);
        Assert.Contains("this is data", Assert.IsType<LiteralExpression>(
            Assert.Single(renamed.FindAll<ExecuteSqlStatement>()).Command).Value!.ToString());
        Assert.Throws<NotSupportedException>(() => call.ToSql(SqlDialects.PostgreSql));
        Assert.Throws<NotSupportedException>(() => new PrintStatement(Sql.Lit("hello")).ToSql(SqlDialects.Sqlite));
    }

    [Fact]
    public void Reserved_variable_names_are_never_identifier_quoted()
    {
        var document = RoundTrip("DECLARE @top INT; EXEC @top = dbo.p @top = @top OUTPUT; PRINT @top");
        var generated = document.ToSql(SqlDialects.TSql);
        Assert.DoesNotContain("@[", generated);
        Assert.Contains("DECLARE @top INT", generated);
    }

    [Fact]
    public void Go_inside_comments_strings_and_identifiers_is_not_a_batch_separator()
    {
        var document = RoundTrip("SELECT 'a\nGO\nb' AS [GO] /*\nGO\n*/;\nSELECT [GO] FROM [GO]");
        Assert.Single(document.Batches!);
        Assert.Equal(2, document.Statements.Count);
    }

    [Fact]
    public void Go_remains_an_identifier_when_not_a_batch_separator()
    {
        var document = RoundTrip("SELECT go FROM t\nGO\nSELECT 1 AS go");
        Assert.Equal(2, document.Batches!.Count);
        var query = Assert.IsType<SelectStatement>(document.Statements[0]);
        Assert.Equal("go", Assert.IsType<ColumnExpression>(query.Projections[0].Expression).Parts[0].Value);
    }

    [Fact]
    public void Leading_and_repeated_go_separators_are_preserved()
    {
        var document = RoundTrip("GO /* initial batch */\nGO\nSELECT 1\nGO");
        Assert.Equal(3, document.Batches!.Count);
        Assert.All(document.Batches, batch => Assert.True(batch.IsTerminated));
        Assert.Single(document.Statements);
    }

    [Fact]
    public void Compact_batches_use_spaces_but_go_keeps_its_own_line()
    {
        var document = RoundTrip("SET NOCOUNT ON; SET XACT_ABORT OFF\nGO\nSELECT 1; SELECT 2");
        Assert.Equal($"SET NOCOUNT ON; SET XACT_ABORT OFF{Environment.NewLine}GO{Environment.NewLine}SELECT 1; SELECT 2",
            document.ToSql(SqlDialects.TSql));
        Assert.Equal($"SET NOCOUNT ON;{Environment.NewLine}SET XACT_ABORT OFF{Environment.NewLine}GO{Environment.NewLine}SELECT{Environment.NewLine}  1;{Environment.NewLine}SELECT{Environment.NewLine}  2",
            document.ToSql(SqlDialects.TSql, new SqlGenerationOptions { PrettyPrint = true }));
    }

    [Fact]
    public void Reference_rejected_sqlglot_named_table_constraint_remains_structured()
    {
        const string sql = "declare @X TABLE (Id INT NOT NULL, constraint PK_Id primary key (Id))";
        var document = SqlDialects.TSql.Parse(sql);
        var declaration = Assert.IsType<TableVariableDeclarationStatement>(Assert.Single(document.Statements));
        Assert.Equal("PK_Id", Assert.IsType<PrimaryKeyConstraint>(declaration.Elements[1]).Name!.Value);
        using var reader = new StringReader(sql);
        new TSql180Parser(true).Parse(reader, out var errors);
        Assert.NotEmpty(errors);
        var generated = document.ToSql(SqlDialects.TSql);
        Assert.Equal(generated, SqlDialects.TSql.Parse(generated).ToSql(SqlDialects.TSql));
    }

    [Theory]
    [InlineData("SELECT 1\nGO 2\nSELECT 2")]
    [InlineData("BEGIN DISTRIBUTED TRANSACTION")]
    [InlineData("ROLLBACK WITH (DELAYED_DURABILITY = ON)")]
    [InlineData("BEGIN TRAN WITH (DELAYED_DURABILITY = OFF)")]
    [InlineData("COMMIT WITH (DELAYED_DURABILITY = FORCED)")]
    [InlineData("COMMIT WITH (DELAYED_DURABILITY = 1)")]
    [InlineData("COMMIT WITH (DELAYED_DURABILITY = ON, OTHER_MODE = OFF)")]
    [InlineData("EXEC(1)")]
    [InlineData("EXEC((SELECT 'SELECT 1'))")]
    [InlineData("DECLARE @ as INT")]
    [InlineData("SET @ x = 1")]
    [InlineData("EXEC @ rc = dbo.p")]
    public void Deferred_batch_and_transaction_modes_are_not_silently_consumed(string sql) =>
        Assert.Throws<SqlParseException>(() => SqlDialects.TSql.Parse(sql));

    [Fact]
    public void Visitors_and_rewriters_include_script_children()
    {
        var document = SqlDialects.TSql.Parse(
            "DECLARE @x INT = 1; SET @x += 2; RETURN @x\nGO\nDECLARE @rows TABLE (id INT); BEGIN TRAN named WITH MARK 'tag'");
        Assert.Same(document, new NoOpRewriter().Visit(document));
        var rewritten = new RenameX().Visit(document);
        Assert.Contains("SET @renamed += 2", rewritten.ToSql(SqlDialects.TSql));
        Assert.Contains("RETURN @renamed", rewritten.ToSql(SqlDialects.TSql));
        Assert.Equal(2, rewritten.Batches!.Count);
        Assert.Same(rewritten.Statements[0], rewritten.Batches[0].Statements[0]);
        Assert.Contains(rewritten.DescendantsAndSelf(), node => node is SqlBatch);
        Assert.Contains(rewritten.FindAll<LiteralExpression>(), literal => Equals(literal.Value, "tag"));
        var visitor = new ScriptVisitor();
        document.Accept(visitor);
        Assert.Equal(2, visitor.BatchCount);
        Assert.Equal(1, visitor.DeclarationCount);
        Assert.Equal(1, visitor.TransactionCount);
    }

    [Fact]
    public void Declared_variables_do_not_reclassify_bare_columns()
    {
        var document = RoundTrip("DECLARE @x INT = 1; SELECT x, @x FROM t");
        var select = Assert.IsType<SelectStatement>(document.Statements[1]);
        Assert.IsType<ColumnExpression>(select.Projections[0].Expression);
        Assert.IsType<LocalVariableExpression>(select.Projections[1].Expression);
    }

    [Theory]
    [InlineData("DECLARE @ROWCOUNT INT = 1; SELECT @@ROWCOUNT, @ROWCOUNT")]
    [InlineData("CREATE PROCEDURE p @ROWCOUNT INT AS SELECT @@ROWCOUNT, @ROWCOUNT")]
    public void System_variables_are_not_rebound_to_same_named_locals(string sql)
    {
        var document = RoundTrip(sql);
        var systemVariable = Assert.Single(document.FindAll<ParameterExpression>());
        Assert.True(systemVariable.IsSystemVariable);
        Assert.Equal("ROWCOUNT", systemVariable.Name);
        Assert.Single(document.FindAll<LocalVariableExpression>());
        Assert.Contains("@@ROWCOUNT", document.ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Routine_builders_reuse_normalized_table_and_system_variable_names()
    {
        var block = Sql.Block()
            .Variable("ROWCOUNT", "INT", Sql.Lit(0))
            .TableVariable("@rows", new ColumnDefinition(new SqlIdentifier("id"), new SqlDataType("INT")))
            .Statement(Sql.Select(Sql.SystemVariable("@@ROWCOUNT"), Sql.Local("ROWCOUNT")).Build())
            .Build();
        var declaration = Assert.IsType<TableVariableDeclarationStatement>(block.Statements[0]);
        Assert.Equal("rows", declaration.Name.Value);
        var parsed = RoundTrip(block.ToSql(SqlDialects.TSql));
        Assert.True(Assert.Single(parsed.FindAll<ParameterExpression>()).IsSystemVariable);
        Assert.Contains("DECLARE @rows TABLE", parsed.ToSql(SqlDialects.TSql));
    }

    [Theory]
    [InlineData("NOCOUNT", true)]
    [InlineData("XACT_ABORT", false)]
    public void Application_settings_reuse_typed_set_toggle(string option, bool enabled)
    {
        var document = RoundTrip($"SET {option} {(enabled ? "ON" : "OFF")}");
        var setting = Assert.IsType<SetStatement>(Assert.Single(document.Statements));
        Assert.Equal(option, Assert.Single(setting.Keywords).Value);
        Assert.Empty(setting.Arguments);
        Assert.Equal(enabled, setting.ToggleValue);
        Assert.Same(document, new NoOpRewriter().Visit(document));
        var block = Sql.Block().SetOption(
            option == "NOCOUNT" ? ApplicationSetOption.NoCount : ApplicationSetOption.XactAbort, enabled).Build();
        Assert.Equal(enabled, Assert.IsType<SetStatement>(Assert.Single(block.Statements)).ToggleValue);
        RoundTrip(block.ToSql(SqlDialects.TSql));
    }

    [Theory]
    [InlineData("SET @x = 1", SqlAssignmentOperator.Assign)]
    [InlineData("SET @x += 1", SqlAssignmentOperator.Add)]
    [InlineData("SET @x -= 1", SqlAssignmentOperator.Subtract)]
    [InlineData("SET @x *= 1", SqlAssignmentOperator.Multiply)]
    [InlineData("SET @x /= 1", SqlAssignmentOperator.Divide)]
    [InlineData("SET @x %= 1", SqlAssignmentOperator.Modulo)]
    [InlineData("SET @x &= 1", SqlAssignmentOperator.BitwiseAnd)]
    [InlineData("SET @x |= 1", SqlAssignmentOperator.BitwiseOr)]
    [InlineData("SET @x ^= 1", SqlAssignmentOperator.BitwiseXor)]
    public void Assignment_operators_are_typed(string sql, SqlAssignmentOperator expected)
    {
        var document = RoundTrip(sql);
        var statement = Assert.IsType<SetVariableStatement>(Assert.Single(document.Statements));
        Assert.Equal("x", statement.Name.Value);
        Assert.Equal(expected, statement.Operator);
    }

    [Fact]
    public void Builder_does_not_move_declarations_across_statements()
    {
        var block = Sql.Block().Statement(Sql.Select(Sql.Lit(1)).Build())
            .Variable("x", "INT", Sql.Lit(2))
            .SetVariable("x", Sql.Lit(1), SqlAssignmentOperator.Add)
            .Return(Sql.Local("x")).Build();
        Assert.Empty(block.Variables);
        Assert.IsType<DeclareStatement>(block.Statements[1]);
        RoundTrip(block.ToSql(SqlDialects.TSql));
        Assert.Throws<NotSupportedException>(() => block.ToSql(SqlDialects.PostgreSql));
    }

    [Fact]
    public void Table_variables_bind_columns_and_do_not_leak_across_batches()
    {
        var catalog = new SqlSchemaCatalog();
        var valid = SqlValidator.Validate(
            "DECLARE @rows TABLE (id INT); INSERT INTO @rows (id) VALUES (1); SELECT id FROM @rows",
            catalog, SqlDialects.TSql, new SqlSchemaValidationOptions { Semantic = true, CheckTypes = true });
        Assert.Empty(valid.Diagnostics);
        var invalid = SqlValidator.Validate(
            "DECLARE @rows TABLE (id INT)\nGO\nSELECT id FROM @rows", catalog, SqlDialects.TSql);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == SqlValidationCodes.UnknownTable);
    }

    [Fact]
    public void Native_computed_table_variable_columns_survive_schema_validation_and_rewriting()
    {
        const string sql = "DECLARE @t TABLE (a INT, b AS (a + 1)); SELECT b FROM @t";
        using var reader = new StringReader(sql);
        new TSql180Parser(true).Parse(reader, out var errors);
        Assert.Empty(errors);

        var document = RoundTrip(sql);
        var options = new SqlSchemaValidationOptions { CheckTypes = true };
        Assert.Empty(SqlValidator.Validate(sql, new SqlSchemaCatalog(), SqlDialects.TSql, options).Diagnostics);
        Assert.Same(document, new NoOpRewriter().Visit(document));

        var rewritten = new RenameComputedColumns().Visit(document);
        var computed = Assert.Single(rewritten.FindAll<ComputedColumnDefinition>());
        Assert.Equal("renamed_b", computed.Name.Value);
        Assert.Equal("renamed_a", Assert.Single(computed.Expression.FindAll<ColumnExpression>()).Parts[0].Value);
        Assert.DoesNotContain(rewritten.FindAll<SqlIdentifier>(), identifier => identifier.Value is "a" or "b");
        var generated = rewritten.ToSql(SqlDialects.TSql);
        RoundTrip(generated);
        Assert.Empty(SqlValidator.Validate(generated, new SqlSchemaCatalog(), SqlDialects.TSql, options).Diagnostics);
    }

    [Theory]
    [InlineData("id INT, doubled AS id * 2")]
    [InlineData("doubled AS id * 2, id INT")]
    public void Table_variable_computed_columns_bind_with_expression_types(string elements)
    {
        var sql = $"DECLARE @rows TABLE ({elements}); INSERT @rows VALUES (1); SELECT doubled FROM @rows";
        var document = RoundTrip(sql);
        var declaration = Assert.IsType<TableVariableDeclarationStatement>(document.Statements[0]);
        Assert.Equal(elements.StartsWith("doubled", StringComparison.Ordinal),
            declaration.Elements[0] is ComputedColumnDefinition);
        var options = new SqlSchemaValidationOptions { CheckTypes = true };
        var result = SqlValidator.Validate(sql, new SqlSchemaCatalog(), SqlDialects.TSql, options);
        Assert.Empty(result.Diagnostics);
        var assignment = SqlValidator.Validate(
            $"DECLARE @rows TABLE ({elements}); DECLARE @text VARCHAR(20); SELECT @text = doubled FROM @rows",
            new SqlSchemaCatalog(), SqlDialects.TSql, options);
        Assert.Contains(assignment.Diagnostics, diagnostic => diagnostic.Code == SqlValidationCodes.InvalidAssignmentType);
    }

    [Theory]
    [InlineData("DECLARE @rows TABLE (id INT, doubled AS missing * 2)", SqlValidationCodes.UnknownColumn)]
    [InlineData("DECLARE @rows TABLE (id INT, doubled AS id * 2, quadrupled AS doubled * 2)", SqlValidationCodes.UnknownColumn)]
    [InlineData("DECLARE @rows TABLE (quadrupled AS doubled * 2, doubled AS id * 2, id INT)", SqlValidationCodes.UnknownColumn)]
    [InlineData("DECLARE @rows TABLE (id INT, doubled AS doubled * 2)", SqlValidationCodes.UnknownColumn)]
    [InlineData("DECLARE @rows TABLE (id INT, doubled AS id * 2); UPDATE @rows SET doubled = 4", SqlValidationCodes.InvalidAssignmentType)]
    [InlineData("DECLARE @rows TABLE (id INT, doubled AS id * 2); INSERT @rows (doubled) VALUES (4)", SqlValidationCodes.InvalidAssignmentType)]
    [InlineData("DECLARE @rows TABLE (id INT, doubled AS id * 2)\nGO\nSELECT doubled FROM @rows", SqlValidationCodes.UnknownTable)]
    public void Table_variable_computed_columns_validate_references_writes_and_scope(string sql, string code)
    {
        var result = SqlValidator.Validate(sql, new SqlSchemaCatalog(), SqlDialects.TSql);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void Assignment_validation_respects_declaration_order_and_batch_scope()
    {
        var invalid = SqlValidator.Validate(
            "SET @x = 1; DECLARE @x INT\nGO\nSET @x = 2",
            SqlDialects.TSql, new SqlValidationOptions { Semantic = true });
        Assert.Equal(2, invalid.Diagnostics.Count(diagnostic =>
            diagnostic.Code == SqlValidationCodes.UnknownLocalVariable));
        var valid = SqlValidator.Validate(
            "DECLARE @x INT = 1; IF @x > 0 BEGIN DECLARE @y INT; SET @y = @x END; SET @x = 2",
            SqlDialects.TSql, new SqlValidationOptions { Semantic = true });
        Assert.Empty(valid.Diagnostics);
    }

    [Fact]
    public void Select_assignment_targets_use_local_types_without_becoming_columns()
    {
        var catalog = new SqlSchemaCatalog(new SqlTableSchema("t", [new SqlColumnSchema("id", "INT")]));
        var options = new SqlSchemaValidationOptions { Semantic = true, CheckTypes = true };
        var valid = SqlValidator.Validate(
            "DECLARE @x INT; SELECT @x = id FROM t; SET @x += 1; SELECT @input FROM t",
            catalog, SqlDialects.TSql, options);
        Assert.Empty(valid.Diagnostics);
        var invalid = SqlValidator.Validate(
            "DECLARE @x INT; SELECT @x = 'text' FROM t",
            catalog, SqlDialects.TSql, options);
        Assert.Contains(invalid.Diagnostics, diagnostic => diagnostic.Code == SqlValidationCodes.InvalidAssignmentType);
        Assert.DoesNotContain(invalid.Diagnostics, diagnostic => diagnostic.Code == SqlValidationCodes.UnknownColumn);
        var procedure = SqlValidator.Validate(
            "CREATE PROCEDURE p @x INT AS SELECT @x = 'text'",
            catalog, SqlDialects.TSql, options);
        Assert.Contains(procedure.Diagnostics, diagnostic => diagnostic.Code == SqlValidationCodes.InvalidAssignmentType);
    }

    [Fact]
    public void Undeclared_parameters_remain_external_but_known_locals_obey_scope()
    {
        var external = SqlValidator.Validate(
            "SELECT @external = @input; SET @other = @input",
            new SqlSchemaCatalog(), SqlDialects.TSql,
            new SqlSchemaValidationOptions { Semantic = true, CheckTypes = true });
        Assert.Empty(external.Diagnostics);
        var local = SqlValidator.Validate(
            "SELECT @x = 1; DECLARE @x INT\nGO\nSELECT @x = 2",
            SqlDialects.TSql, new SqlValidationOptions { Semantic = true });
        Assert.Equal(2, local.Diagnostics.Count(diagnostic => diagnostic.Code == SqlValidationCodes.UnknownLocalVariable));
    }

    [Fact]
    public void Select_assignments_do_not_expose_result_columns()
    {
        var result = SqlValidator.Validate(
            "SELECT id FROM (SELECT @x = id FROM t) AS d",
            new SqlSchemaCatalog(new SqlTableSchema("t", [new SqlColumnSchema("id", "INT")])),
            SqlDialects.TSql);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == SqlValidationCodes.UnknownColumn);
    }

    private static SqlDocument RoundTrip(string sql)
    {
        var document = SqlDialects.TSql.Parse(sql);
        var generated = document.ToSql(SqlDialects.TSql);
        using var reader = new StringReader(generated);
        new TSql180Parser(true).Parse(reader, out var errors);
        Assert.True(errors.Count == 0, generated + Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(error => error.Message)));
        Assert.Equal(generated, SqlDialects.TSql.Parse(generated).ToSql(SqlDialects.TSql));
        return document;
    }

    private sealed class NoOpRewriter : SqlRewriter;

    private sealed class RenameComputedColumns : SqlRewriter
    {
        protected override SqlNode VisitIdentifier(SqlIdentifier node) =>
            node.Value is "a" or "b" ? node with { Value = "renamed_" + node.Value } : node;
    }

    private sealed class ScriptVisitor : SqlVisitor
    {
        public int BatchCount { get; private set; }
        public int DeclarationCount { get; private set; }
        public int TransactionCount { get; private set; }

        protected override void VisitBatch(SqlBatch node)
        {
            BatchCount++;
            base.VisitBatch(node);
        }

        protected override void VisitDeclare(DeclareStatement node)
        {
            DeclarationCount++;
            base.VisitDeclare(node);
        }

        protected override void VisitTransaction(TransactionStatement node)
        {
            TransactionCount++;
            base.VisitTransaction(node);
        }
    }

    private sealed class RenameX : SqlRewriter
    {
        protected override SqlNode VisitIdentifier(SqlIdentifier node) =>
            node.Value == "x" ? node with { Value = "renamed" } : node;
    }
}
