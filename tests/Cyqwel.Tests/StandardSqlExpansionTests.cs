using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Parsing;
using Cyqwel.Visitors;

namespace Cyqwel.Tests;

public class StandardSqlExpansionTests
{
    [Fact]
    public void Parses_values_richer_joins_and_predicates()
    {
        var values = SqlParser.Parse(
            "VALUES (1, 'one'), (2, 'two') ORDER BY 1 FETCH FIRST 1 ROWS ONLY");
        var query = Assert.IsType<ValuesStatement>(Assert.Single(values.Statements));

        Assert.Equal(2, query.Rows.Count);
        Assert.Equal("VALUES (1, 'one'), (2, 'two') ORDER BY 1 LIMIT 1", values.ToSql());

        var select = SqlParser.Parse("""
            SELECT a.id
            FROM a
            NATURAL LEFT JOIN b
            INNER JOIN c USING (id)
            WHERE a.active IS TRUE
              AND a.value IS DISTINCT FROM b.value
            """);

        Assert.Single(select.FindAll<BooleanTestExpression>());
        Assert.Single(select.FindAll<DistinctFromExpression>());
        Assert.Contains(select.FindAll<JoinTable>(), join => join.IsNatural);
        Assert.Contains(select.FindAll<JoinTable>(), join => join.Using is { Count: 1 });
    }

    [Fact]
    public void Parses_aggregate_modifiers_and_window_frames()
    {
        var document = SqlParser.Parse("""
            SELECT SUM(amount) WITHIN GROUP (ORDER BY created_at)
                     FILTER (WHERE amount > 0)
                     OVER (
                         PARTITION BY account_id
                         ORDER BY created_at
                         ROWS BETWEEN 2 PRECEDING AND CURRENT ROW
                     )
            FROM entries
            WINDOW totals AS (PARTITION BY account_id ORDER BY created_at)
            """);
        var function = Assert.Single(document.FindAll<FunctionCallExpression>());
        var window = Assert.Single(document.FindAll<WindowExpression>());
        var definition = Assert.Single(document.FindAll<WindowDefinition>());

        Assert.NotNull(function.Filter);
        Assert.Single(function.WithinGroup!);
        Assert.NotNull(window.Frame);
        Assert.Equal(WindowFrameBoundKind.Preceding, window.Frame.Start.Kind);
        Assert.Equal(WindowFrameBoundKind.CurrentRow, window.Frame.End!.Kind);
        Assert.Equal("totals", definition.Name.Value);
    }

    [Fact]
    public void Parses_merge_and_extended_dml()
    {
        var merge = SqlParser.Parse("""
            MERGE INTO target t
            USING source s
            ON t.id = s.id
            WHEN MATCHED AND s.deleted IS TRUE THEN DELETE
            WHEN MATCHED THEN UPDATE SET t.name = s.name
            WHEN NOT MATCHED THEN INSERT (id, name) VALUES (s.id, s.name)
            """);
        var statement = Assert.IsType<MergeStatement>(Assert.Single(merge.Statements));

        Assert.Equal(3, statement.WhenClauses.Count);
        Assert.IsType<MergeDeleteAction>(statement.WhenClauses[0].Action);
        Assert.IsType<MergeUpdateAction>(statement.WhenClauses[1].Action);
        Assert.IsType<MergeInsertAction>(statement.WhenClauses[2].Action);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", merge.ToSql());

        var update = Assert.IsType<UpdateStatement>(Assert.Single(
            SqlParser.Parse(
                "UPDATE target SET name = source.name FROM source WHERE target.id = source.id RETURNING target.id INTO @id")
                .Statements));
        var delete = Assert.IsType<DeleteStatement>(Assert.Single(
            SqlParser.Parse(
                "DELETE FROM target USING source WHERE target.id = source.id RETURNING target.id INTO @id")
                .Statements));

        Assert.NotNull(update.From);
        Assert.Single(update.ReturningInto!);
        Assert.NotNull(delete.Using);
        Assert.Single(delete.ReturningInto!);
    }

    [Fact]
    public void Parses_and_generates_core_ddl()
    {
        const string sql = """
            CREATE TABLE accounts (
                id INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                email VARCHAR(255) NOT NULL UNIQUE,
                balance DECIMAL(10, 2) DEFAULT 0,
                CONSTRAINT ck_balance CHECK (balance >= 0),
                CONSTRAINT uq_email UNIQUE (email)
            );
            CREATE UNIQUE INDEX ix_accounts_email ON accounts (email);
            CREATE SEQUENCE account_seq START WITH 10 INCREMENT BY 5 CACHE 20 NO CYCLE;
            ALTER TABLE accounts ADD COLUMN status VARCHAR(20) DEFAULT 'active';
            DROP INDEX IF EXISTS old_accounts_idx;
            TRUNCATE TABLE archived_accounts CASCADE
            """;
        var document = SqlParser.Parse(sql);

        Assert.Collection(
            document.Statements,
            value => Assert.IsType<CreateTableStatement>(value),
            value => Assert.IsType<CreateIndexStatement>(value),
            value => Assert.IsType<CreateSequenceStatement>(value),
            value => Assert.IsType<AlterTableStatement>(value),
            value => Assert.IsType<DropStatement>(value),
            value => Assert.IsType<TruncateStatement>(value));
        Assert.Contains("CONSTRAINT ck_balance CHECK (balance >= 0)", document.ToSql());
        Assert.Contains("CREATE SEQUENCE account_seq START WITH 10 INCREMENT BY 5 CACHE 20 NO CYCLE", document.ToSql());
    }

    [Fact]
    public void Traverses_and_rewrites_every_new_node_family()
    {
        var id = new SqlIdentifier("id");
        var dataType = new SqlDataType("INTEGER");
        var column = new ColumnDefinition(id, dataType, Default: new DefaultExpression());
        var constraint = new ForeignKeyConstraint(
            [id],
            new TableName("parent"),
            [id])
        {
            Name = new SqlIdentifier("fk_parent"),
        };
        var frame = new WindowFrame(
            WindowFrameUnit.Rows,
            new WindowFrameBound(WindowFrameBoundKind.Preceding, new LiteralExpression(1)),
            new WindowFrameBound(WindowFrameBoundKind.CurrentRow));
        var expression = new WindowExpression(
            new FunctionCallExpression(
                "SUM",
                new CollateExpression(new ColumnExpression("amount"), new SqlIdentifier("ordinal"))),
            Frame: frame);
        var select = new SelectStatement(
            [
                new SelectItem(expression),
                new SelectItem(new RowExpression([new LiteralExpression(1), new LiteralExpression(2)])),
                new SelectItem(new ExtractExpression(new SqlIdentifier("YEAR"), new ColumnExpression("created_at"))),
                new SelectItem(new IntervalExpression(new LiteralExpression(1), new SqlIdentifier("DAY"))),
                new SelectItem(new SequenceValueExpression(new TableName("seq"), SequenceValueKind.Next)),
                new SelectItem(new TryCastExpression(new LiteralExpression("1"), dataType)),
            ],
            ConnectBy: new ConnectByClause(
                new UnaryExpression(UnaryOperator.Prior, new ColumnExpression("id"))));
        var document = new SqlDocument(
            select,
            new CreateTableStatement(new TableName("child"), [column, constraint]),
            new AlterTableStatement(
                new TableName("child"),
                [
                    new AlterColumnAction(id, dataType, Nullability.NotNull),
                    new AddConstraintAction(new CheckConstraint(new LiteralExpression(true))),
                    new DropConstraintAction(new SqlIdentifier("old")),
                    new RenameColumnAction(id, new SqlIdentifier("new_id")),
                    new RenameTableAction(new SqlIdentifier("children")),
                ]),
            new CreateViewStatement(new TableName("child_view"), select),
            new CreateIndexStatement(
                new TableName("ix_child"),
                new TableName("child"),
                [new IndexColumn(new ColumnExpression("id"))]),
            new AlterSequenceStatement(
                new TableName("seq"),
                new SequenceOptions(IncrementBy: new LiteralExpression(2))));

        var descendants = document.DescendantsAndSelf().ToArray();

        document.Accept(new NoopVisitor());
        Assert.Same(document, document.Accept(new NoopRewriter()));
        Assert.Contains(descendants, node => node is WindowFrame);
        Assert.Contains(descendants, node => node is ForeignKeyConstraint);
        Assert.Contains(descendants, node => node is AlterColumnAction);
        Assert.Contains(descendants, node => node is SequenceOptions);
        Assert.Contains(descendants, node => node is ConnectByClause);
    }

    private sealed class NoopVisitor : SqlVisitor;

    private sealed class NoopRewriter : SqlRewriter;

    [Fact]
    public void Preserves_set_precedence_and_root_ctes()
    {
        var document = SqlParser.Parse("""
            WITH ids AS (SELECT id FROM source)
            SELECT id FROM one
            UNION
            SELECT id FROM two
            INTERSECT
            SELECT id FROM three
            """);
        var union = Assert.IsType<SetOperationStatement>(Assert.Single(document.Statements));

        Assert.Equal(SetOperator.Union, union.Operator);
        Assert.IsType<SelectStatement>(union.Left);
        Assert.Equal(
            SetOperator.Intersect,
            Assert.IsType<SetOperationStatement>(union.Right).Operator);
        Assert.Single(union.CommonTableExpressions!);
        Assert.Equal(
            "WITH ids AS (SELECT id FROM source) SELECT id FROM one UNION SELECT id FROM two INTERSECT SELECT id FROM three",
            document.ToSql());

        var values = Assert.IsType<ValuesStatement>(Assert.Single(
            SqlParser.Parse("WITH ids AS (SELECT id FROM source) VALUES (1)").Statements));
        Assert.Single(values.CommonTableExpressions!);
    }

    [Fact]
    public void Preserves_identity_modes_and_rewritten_constraint_names()
    {
        var document = SqlParser.Parse("""
            CREATE TABLE identities (
                always_id INTEGER GENERATED ALWAYS AS IDENTITY,
                default_id INTEGER GENERATED BY DEFAULT AS IDENTITY
            )
            """);
        var create = Assert.IsType<CreateTableStatement>(Assert.Single(document.Statements));
        var columns = create.Elements.Cast<ColumnDefinition>().ToArray();

        Assert.Equal(IdentityGeneration.Always, columns[0].Identity);
        Assert.Equal(IdentityGeneration.ByDefault, columns[1].Identity);
        Assert.Contains("GENERATED ALWAYS AS IDENTITY", document.ToSql());
        Assert.Contains("GENERATED BY DEFAULT AS IDENTITY", document.ToSql());

        var constraint = new PrimaryKeyConstraint([new SqlIdentifier("id")])
        {
            Name = new SqlIdentifier("old_name"),
        };
        var rewritten = Assert.IsType<PrimaryKeyConstraint>(
            constraint.Accept(new RenameConstraintRewriter()));

        Assert.Equal("new_name", rewritten.Name!.Value);
        Assert.Equal(
            "CREATE TABLE sample (CONSTRAINT new_name PRIMARY KEY (id))",
            new CreateTableStatement(new TableName("sample"), [rewritten]).ToSql());
    }

    [Theory]
    [InlineData("ALTER TABLE sample ALTER COLUMN value TYPE BIGINT")]
    [InlineData("ALTER TABLE sample ALTER COLUMN value SET DEFAULT 0")]
    [InlineData("ALTER TABLE sample ALTER COLUMN value DROP DEFAULT")]
    [InlineData("ALTER TABLE sample ALTER COLUMN value SET NOT NULL")]
    [InlineData("ALTER TABLE sample ALTER COLUMN value DROP NOT NULL")]
    public void Round_trips_alter_column_actions(string sql)
    {
        var document = SqlParser.Parse(sql);

        Assert.Single(document.FindAll<AlterColumnAction>());
        Assert.Equal(sql, document.ToSql());
    }

    private sealed class RenameConstraintRewriter : SqlRewriter
    {
        protected override SqlNode VisitIdentifier(SqlIdentifier node) =>
            node.Value == "old_name" ? node with { Value = "new_name" } : node;
    }
}
