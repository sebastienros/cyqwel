using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Parsing;
using Cyqwel.Visitors;

namespace Cyqwel.Tests;

public class ParserTests
{
    [Fact]
    public void Parses_complex_select()
    {
        const string sql = """
            SELECT u.id, COUNT(*) AS total
            FROM users u
            LEFT JOIN orders o ON u.id = o.user_id
            WHERE u.active = TRUE
            GROUP BY u.id
            HAVING COUNT(*) > 1
            ORDER BY total DESC
            LIMIT 10 OFFSET 5
            """;

        var document = SqlParser.Parse(sql);
        var select = Assert.IsType<SelectStatement>(Assert.Single(document.Statements));

        Assert.Equal(2, select.Projections.Count);
        Assert.IsType<JoinTable>(select.From);
        Assert.NotNull(select.Where);
        Assert.Single(select.GroupBy!);
        Assert.NotNull(select.Having);
        Assert.Equal(OrderDirection.Descending, Assert.Single(select.OrderBy!).Direction);
        Assert.Equal(10L, Assert.IsType<LiteralExpression>(select.Limit).Value);
        Assert.Equal(5L, Assert.IsType<LiteralExpression>(select.Offset).Value);
    }

    [Fact]
    public void Parses_ctes_and_set_operations()
    {
        const string sql = """
            WITH active AS (SELECT id FROM users WHERE active = TRUE)
            SELECT id FROM active
            UNION ALL
            SELECT id FROM archived
            ORDER BY id
            LIMIT 5
            """;

        var document = SqlParser.Parse(sql);
        var set = Assert.IsType<SetOperationStatement>(Assert.Single(document.Statements));

        Assert.True(set.IsAll);
        Assert.Single(set.CommonTableExpressions!);
        Assert.Single(set.OrderBy!);
        Assert.Equal(5L, Assert.IsType<LiteralExpression>(set.Limit).Value);
    }

    [Theory]
    [InlineData("INSERT INTO users (id, name) VALUES (1, 'Ada'), (2, 'Grace')", typeof(InsertStatement))]
    [InlineData("UPDATE users SET name = 'Ada' WHERE id = 1 RETURNING id", typeof(UpdateStatement))]
    [InlineData("DELETE FROM users WHERE id = 1 RETURNING id", typeof(DeleteStatement))]
    public void Parses_dml(string sql, Type statementType)
    {
        var document = SqlParser.Parse(sql);
        Assert.IsType(statementType, Assert.Single(document.Statements));
    }

    [Fact]
    public void Parses_empty_functions_and_tsql_offset_fetch()
    {
        var document = SqlDialects.TSql.Parse(
            "SELECT GETDATE() FROM users ORDER BY id OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY");
        var select = Assert.IsType<SelectStatement>(Assert.Single(document.Statements));

        Assert.IsType<FunctionCallExpression>(Assert.Single(select.Projections).Expression);
        Assert.Equal(20L, Assert.IsType<LiteralExpression>(select.Offset).Value);
        Assert.Equal(10L, Assert.IsType<LiteralExpression>(select.Limit).Value);
    }

    [Fact]
    public void Parses_standard_string_escaping_and_not_precedence()
    {
        var document = SqlParser.Parse("SELECT 'it''s valid' FROM users WHERE NOT age = 18");
        var select = Assert.IsType<SelectStatement>(Assert.Single(document.Statements));
        var literal = Assert.IsType<LiteralExpression>(Assert.Single(select.Projections).Expression);
        var not = Assert.IsType<UnaryExpression>(select.Where);
        var comparison = Assert.IsType<BinaryExpression>(not.Operand);

        Assert.Equal("it's valid", literal.Value);
        Assert.Equal(BinaryOperator.Equal, comparison.Operator);
        Assert.Equal(
            "SELECT 'it''s valid' FROM users WHERE NOT (age = 18)",
            document.ToSql());
    }

    [Fact]
    public void Parses_distinct_functions_and_comma_tables()
    {
        var document = SqlParser.Parse("SELECT COUNT(DISTINCT u.id) FROM users u, teams t");
        var select = Assert.IsType<SelectStatement>(Assert.Single(document.Statements));
        var function = Assert.IsType<FunctionCallExpression>(Assert.Single(select.Projections).Expression);

        Assert.True(function.IsDistinct);
        Assert.IsType<JoinTable>(select.From);
    }

    [Fact]
    public void Parses_window_functions_and_parameter_defaults()
    {
        var dialect = CreateParameterDefaultDialect();
        var document = dialect.Parse("""
            SELECT COUNT(1) OVER (),
                   SUM(amount) OVER (PARTITION BY account_id),
                   ROW_NUMBER() OVER (PARTITION BY region ORDER BY created_at DESC, id)
            FROM entries
            WHERE account_id = @accountId:10
            """);
        var select = Assert.IsType<SelectStatement>(Assert.Single(document.Statements));
        var windows = select.Projections
            .Select(item => Assert.IsType<WindowExpression>(item.Expression))
            .ToArray();
        var parameter = Assert.Single(document.FindAll<ParameterExpression>());
        var orderedWindow = Assert.IsAssignableFrom<IReadOnlyList<OrderByItem>>(windows[2].OrderBy);

        Assert.Null(windows[0].PartitionBy);
        Assert.Null(windows[0].OrderBy);
        Assert.Single(windows[1].PartitionBy!);
        Assert.Null(windows[1].OrderBy);
        Assert.Single(windows[2].PartitionBy!);
        Assert.Equal(2, orderedWindow.Count);
        Assert.Equal(OrderDirection.Descending, orderedWindow[0].Direction);
        Assert.Equal(OrderDirection.Unspecified, orderedWindow[1].Direction);
        Assert.Equal(10L, Assert.IsType<LiteralExpression>(parameter.DefaultValue).Value);
    }

    [Fact]
    public void Preserves_unspecified_order_direction()
    {
        var document = SqlParser.Parse("SELECT a ORDER BY b");
        var select = Assert.IsType<SelectStatement>(Assert.Single(document.Statements));

        Assert.Equal(OrderDirection.Unspecified, Assert.Single(select.OrderBy!).Direction);
        Assert.Equal("SELECT a ORDER BY b", document.ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Preserves_grouping_decimal_scale_and_comma_tables()
    {
        var document = SqlParser.Parse(
            "select 1.0 from t1, t2 where (a = b) or (c = d)");
        var select = Assert.IsType<SelectStatement>(Assert.Single(document.Statements));
        var join = Assert.IsType<JoinTable>(select.From);
        var literal = Assert.IsType<LiteralExpression>(Assert.Single(select.Projections).Expression);

        Assert.Equal(JoinSyntax.Comma, join.Syntax);
        Assert.IsType<decimal>(literal.Value);
        Assert.Equal(
            "SELECT 1.0 FROM t1, t2 WHERE (a = b) OR (c = d)",
            document.ToSql());
    }

    [Fact]
    public void Parses_keyword_named_parameters_in_row_limits()
    {
        var withoutDefault = SqlParser.Parse(
            "select a where a = @b limit @limit");
        var withDefault = CreateParameterDefaultDialect().Parse(
            "select a where a = @b limit @limit:10");
        var parameter = withDefault.FindAll<ParameterExpression>()
            .Single(value => value.Name == "limit");

        Assert.Equal(
            "SELECT a WHERE a = @b LIMIT @limit",
            withoutDefault.ToSql());
        Assert.Equal(10L, Assert.IsType<LiteralExpression>(parameter.DefaultValue).Value);
        Assert.Equal(
            "SELECT a WHERE a = @b LIMIT @limit",
            withDefault.ToSql());
    }

    [Fact]
    public void Parameter_defaults_are_opt_in()
    {
        Assert.Throws<SqlParseException>(() =>
            SqlParser.Parse("select a where a = @b:10"));

        var dialect = CreateParameterDefaultDialect();
        var document = dialect.Parse("select a where a = @b:10");

        Assert.Equal(
            10L,
            Assert.IsType<LiteralExpression>(
                Assert.Single(document.FindAll<ParameterExpression>()).DefaultValue).Value);
    }

    [Fact]
    public void Generates_flat_set_chains_and_compact_cte_columns()
    {
        var set = SqlParser.Parse(
            "select a from b union all select c from d union select e from f");
        var cte = SqlParser.Parse(
            "with cte1(abc, def) as (select abc, def from source) select abc from cte1");

        Assert.Equal(
            "SELECT a FROM b UNION ALL SELECT c FROM d UNION SELECT e FROM f",
            set.ToSql());
        Assert.Equal(
            "WITH cte1(abc, def) AS (SELECT abc, def FROM source) SELECT abc FROM cte1",
            cte.ToSql());
    }

    [Fact]
    public void Preserves_set_operator_precedence()
    {
        var unionThenIntersect = new SetOperationStatement(
            new SetOperationStatement(
                Sql.Select("a").From("first").Build(),
                SetOperator.Union,
                Sql.Select("a").From("t2").Build()),
            SetOperator.Intersect,
            Sql.Select("a").From("t3").Build());
        var intersectThenUnion = new SetOperationStatement(
            new SetOperationStatement(
                Sql.Select("a").From("first").Build(),
                SetOperator.Intersect,
                Sql.Select("a").From("t2").Build()),
            SetOperator.Union,
            Sql.Select("a").From("t3").Build());

        Assert.Equal(
            "(SELECT a FROM \"first\" UNION SELECT a FROM t2) INTERSECT SELECT a FROM t3",
            unionThenIntersect.ToSql());
        Assert.Equal(
            "SELECT a FROM \"first\" INTERSECT SELECT a FROM t2 UNION SELECT a FROM t3",
            intersectThenUnion.ToSql());
    }

    [Fact]
    public void Window_keywords_remain_valid_identifiers()
    {
        var document = SqlParser.Parse(
            "SELECT over, partition, COUNT(*) OVER (PARTITION BY partition ORDER BY over) FROM data");

        Assert.Equal(
            "SELECT over, partition, COUNT(*) OVER (PARTITION BY partition ORDER BY over) FROM data",
            document.ToSql());
    }

    [Fact]
    public void Requires_semicolons_between_statements()
    {
        Assert.Throws<SqlParseException>(() => SqlParser.Parse("SELECT 1 SELECT 2"));

        var document = SqlParser.Parse("SELECT 1; SELECT 2;");
        Assert.Equal(2, document.Statements.Count);
    }

    [Theory]
    [InlineData("SELECT \"my\"\"column\" FROM data", "SELECT \"my\"\"column\" FROM data")]
    [InlineData("SELECT `my``column` FROM data", "SELECT `my``column` FROM data")]
    [InlineData("SELECT [my]]column] FROM data", "SELECT [my]]column] FROM data")]
    public void Round_trips_escaped_identifiers(string sql, string expected)
    {
        var dialect = sql[7] switch
        {
            '`' => SqlDialects.MySql,
            '[' => SqlDialects.TSql,
            _ => SqlDialects.Generic,
        };

        Assert.Equal(expected, dialect.Parse(sql).ToSql(dialect));
    }

    [Fact]
    public void Parses_deep_parentheses_without_exponential_backtracking()
    {
        const int depth = 64;
        var sql = "SELECT * FROM data WHERE "
            + new string('(', depth)
            + "id = 1"
            + new string(')', depth);

        var document = SqlParser.Parse(sql);

        Assert.IsType<SelectStatement>(Assert.Single(document.Statements));
    }

    [Fact]
    public void Preserves_quoted_identifier_semantics_across_dialects()
    {
        var document = SqlDialects.PostgreSql.Parse("SELECT \"display name\" FROM \"user data\"");

        Assert.Equal(
            "SELECT `display name` FROM `user data`",
            SqlDialects.MySql.Generate(document));
    }

    [Fact]
    public void Returns_structured_parse_errors()
    {
        var parsed = SqlParser.TryParse(
            "SELECT FROM",
            SqlDialects.Generic,
            out var document,
            out var error);

        Assert.False(parsed);
        Assert.Null(document);
        Assert.NotNull(error);
        Assert.True(error.Offset >= 0);
    }

    [Fact]
    public void Enforces_input_guard()
    {
        var exception = Assert.Throws<SqlParseException>(() => SqlParser.Parse(
            "SELECT 1",
            options: new SqlParseOptions { MaximumInputLength = 3 }));

        Assert.Contains("maximum length", exception.Message);
    }

    [Fact]
    public void Enforces_ast_node_guard()
    {
        var parsed = SqlParser.TryParse(
            "SELECT 1",
            SqlDialects.Generic,
            out var document,
            out var error,
            new SqlParseOptions { MaximumAstNodes = 1 });

        Assert.False(parsed);
        Assert.Null(document);
        Assert.Equal(SqlParseErrorCode.AstTooLarge, error!.Code);
        Assert.Contains("maximum node count", error.Message);
    }

    [Fact]
    public void Rejects_preprocessors_that_replace_the_document_root()
    {
        var dialect = SqlDialectBuilder.Create("invalid-root")
            .WithPreprocessor(static _ => Sql.Lit(1))
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => dialect.Parse("SELECT 1"));

        Assert.Contains("SqlDocument root", exception.Message);
    }

    [Fact]
    public void Parses_expression_operator_and_predicate_variants()
    {
        const string sql = """
            SELECT -1, +2, ~3, 5 % 2, flags & 1, flags ^ 2, flags | 4,
                   CASE WHEN score NOT BETWEEN 1 AND 10
                        THEN CAST(score AS DECIMAL(10, 2))
                   END
            FROM users
            WHERE id NOT IN (SELECT id FROM archived)
              AND name NOT LIKE 'x%'
              AND deleted IS NOT NULL
            """;

        var document = SqlParser.Parse(sql);
        var unaryOperators = document.FindAll<UnaryExpression>()
            .Select(expression => expression.Operator)
            .ToArray();
        var binaryOperators = document.FindAll<BinaryExpression>()
            .Select(expression => expression.Operator)
            .ToArray();
        var between = Assert.Single(document.FindAll<BetweenExpression>());
        var @in = Assert.Single(document.FindAll<InExpression>());
        var isNull = Assert.Single(document.FindAll<IsNullExpression>());
        var cast = Assert.Single(document.FindAll<CastExpression>());
        var @case = Assert.Single(document.FindAll<CaseExpression>());

        Assert.Contains(UnaryOperator.Minus, unaryOperators);
        Assert.Contains(UnaryOperator.Plus, unaryOperators);
        Assert.Contains(UnaryOperator.BitwiseNot, unaryOperators);
        Assert.Contains(BinaryOperator.Modulo, binaryOperators);
        Assert.Contains(BinaryOperator.BitwiseAnd, binaryOperators);
        Assert.Contains(BinaryOperator.BitwiseXor, binaryOperators);
        Assert.Contains(BinaryOperator.BitwiseOr, binaryOperators);
        Assert.Contains(BinaryOperator.NotLike, binaryOperators);
        Assert.True(between.IsNegated);
        Assert.True(@in.IsNegated);
        Assert.NotNull(@in.Query);
        Assert.True(isNull.IsNegated);
        Assert.Equal([10, 2], cast.DataType.Arguments);
        Assert.Null(@case.Else);
    }

    [Fact]
    public void Parses_join_set_and_dml_variants()
    {
        var joins = SqlParser.Parse("""
            SELECT *
            FROM a
            JOIN b ON a.id = b.id
            INNER JOIN c ON b.id = c.id
            RIGHT OUTER JOIN d ON c.id = d.id
            FULL OUTER JOIN e ON d.id = e.id
            CROSS JOIN f
            """);
        var joinKinds = joins.FindAll<JoinTable>()
            .Select(join => join.Kind)
            .ToArray();

        Assert.Contains(JoinKind.Inner, joinKinds);
        Assert.Contains(JoinKind.Right, joinKinds);
        Assert.Contains(JoinKind.Full, joinKinds);
        Assert.Contains(JoinKind.Cross, joinKinds);

        var setDocument = SqlParser.Parse("""
            WITH source_ids (id) AS (SELECT id FROM source)
            SELECT id FROM source_ids
            INTERSECT ALL
            SELECT id FROM active
            EXCEPT
            SELECT id FROM deleted
            ORDER BY id
            OFFSET 2
            """);
        var set = Assert.IsType<SetOperationStatement>(Assert.Single(setDocument.Statements));

        Assert.Equal(SetOperator.Except, set.Operator);
        Assert.Equal(
            SetOperator.Intersect,
            Assert.IsType<SetOperationStatement>(set.Left).Operator);
        Assert.Equal(2L, Assert.IsType<LiteralExpression>(set.Offset).Value);

        var insert = Assert.IsType<InsertStatement>(Assert.Single(
            SqlParser.Parse("INSERT INTO archive (id) SELECT id FROM users").Statements));
        var update = Assert.IsType<UpdateStatement>(Assert.Single(
            SqlParser.Parse("UPDATE users SET active = TRUE").Statements));
        var delete = Assert.IsType<DeleteStatement>(Assert.Single(
            SqlParser.Parse("DELETE FROM users").Statements));

        Assert.NotNull(insert.Source);
        Assert.Null(insert.Values);
        Assert.Null(update.Where);
        Assert.Null(delete.Where);
    }

    [Fact]
    public void Decodes_mysql_backslash_string_escapes()
    {
        var document = SqlDialects.MySql.Parse("""SELECT '\0\b\n\r\t\Z\q'""");
        var literal = Assert.IsType<LiteralExpression>(
            Assert.Single(Assert.IsType<SelectStatement>(
                Assert.Single(document.Statements)).Projections).Expression);

        Assert.Equal("\0\b\n\r\t\u001aq", literal.Value);
    }

    private static SqlDialect CreateParameterDefaultDialect() =>
        SqlDialectBuilder.Create($"parameter-defaults-{Guid.NewGuid():N}")
            .ConfigureParser(options => options with { SupportsParameterDefaults = true })
            .Build();
}
