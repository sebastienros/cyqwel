using Cyqwel.Ast;
using Cyqwel.Visitors;

namespace Cyqwel.Tests;

public class VisitorTests
{
    [Fact]
    public void Traversal_finds_tables_and_columns()
    {
        var query = Sql.Select("u.id", "u.name")
            .From("users", "u")
            .Where(Sql.Col("u.active").EqualTo(Sql.Lit(true)))
            .Build();

        Assert.Equal(["users"], query.GetTableNames());
        Assert.Equal(["u.id", "u.name", "u.active"], query.GetColumnNames());
        Assert.True(query.Contains<BinaryExpression>());
        Assert.Equal(3, query.FindAll<ColumnExpression>().Count());
    }

    [Fact]
    public void Read_only_visitor_dispatches_typed_nodes()
    {
        var visitor = new ColumnCountingVisitor();
        Sql.Select("id", "name").From("users").Build().Accept(visitor);

        Assert.Equal(2, visitor.Count);
    }

    [Fact]
    public void Rewriter_preserves_unchanged_nodes()
    {
        var query = Sql.Select("id").From("users").Build();

        Assert.Same(query, query.Accept(new IdentityRewriter()));
    }

    [Fact]
    public void Convenience_transforms_rewrite_without_mutating_source()
    {
        var source = Sql.Select("u.id").From("users", "u").Build();
        var rewritten = source
            .RenameTable("users", "accounts")
            .RenameColumn("id", "account_id")
            .AddWhere(Sql.Col("u.active").EqualTo(Sql.Lit(true)))
            .SetLimit(25);

        Assert.Equal("SELECT u.id FROM users AS u", source.ToSql());
        Assert.Equal(
            "SELECT u.account_id FROM accounts AS u WHERE u.active = TRUE LIMIT 25",
            rewritten.ToSql());
    }

    private sealed class ColumnCountingVisitor : SqlVisitor
    {
        public int Count { get; private set; }

        protected override void VisitColumn(ColumnExpression node)
        {
            Count++;
            base.VisitColumn(node);
        }
    }

    private sealed class IdentityRewriter : SqlRewriter;
}
