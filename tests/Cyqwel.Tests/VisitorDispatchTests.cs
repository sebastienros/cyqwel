using Cyqwel.Ast;
using Cyqwel.Visitors;

namespace Cyqwel.Tests;

public class VisitorDispatchTests
{
    private static readonly Type[] AllNodeTypes =
    [
        typeof(SqlDocument),
        typeof(SelectStatement),
        typeof(SetOperationStatement),
        typeof(ExplainStatement),
        typeof(InsertStatement),
        typeof(UpdateStatement),
        typeof(DeleteStatement),
        typeof(SqlIdentifier),
        typeof(ColumnExpression),
        typeof(StarExpression),
        typeof(LiteralExpression),
        typeof(ParameterExpression),
        typeof(ParenthesizedExpression),
        typeof(UnaryExpression),
        typeof(BinaryExpression),
        typeof(BetweenExpression),
        typeof(InExpression),
        typeof(IsNullExpression),
        typeof(FunctionCallExpression),
        typeof(WindowExpression),
        typeof(ExistsExpression),
        typeof(SubqueryExpression),
        typeof(WhenClause),
        typeof(CaseExpression),
        typeof(CastExpression),
        typeof(SqlDataType),
        typeof(TableName),
        typeof(NamedTable),
        typeof(DerivedTable),
        typeof(JoinTable),
        typeof(SelectItem),
        typeof(OrderByItem),
        typeof(CommonTableExpression),
        typeof(Assignment),
    ];

    [Fact]
    public void Read_only_visitor_dispatches_every_node_kind()
    {
        var document = CreateCompleteDocument();
        var visitor = new CompleteVisitor();

        document.Accept(visitor);

        Assert.Equal(
            AllNodeTypes.OrderBy(static type => type.Name),
            visitor.Visited.OrderBy(static type => type.Name));
    }

    [Fact]
    public void Child_enumeration_reaches_every_node_kind()
    {
        var document = CreateCompleteDocument();
        var depthFirst = document.DescendantsAndSelf().ToArray();
        var breadthFirst = document.BreadthFirst().ToArray();

        Assert.Equal(
            AllNodeTypes.OrderBy(static type => type.Name),
            depthFirst.Select(static node => node.GetType()).Distinct().OrderBy(static type => type.Name));
        Assert.Equal(depthFirst.Length, breadthFirst.Length);
        Assert.Same(document, depthFirst[0]);
        Assert.Same(document, breadthFirst[0]);
        Assert.DoesNotContain(document, document.Descendants());
    }

    [Fact]
    public void Visitor_and_rewriter_reject_unknown_nodes()
    {
        var node = new UnknownNode();

        Assert.Throws<NotSupportedException>(() => node.Accept(new CompleteVisitor()));
        Assert.Throws<NotSupportedException>(() => node.Accept(new CompleteRewriter()));
    }

    [Fact]
    public void Identity_rewriter_preserves_complete_tree()
    {
        var document = CreateCompleteDocument();

        Assert.Same(document, document.Accept(new IdentityRewriter()));
    }

    [Fact]
    public void Rewriter_dispatches_and_rebuilds_every_node_kind()
    {
        var document = CreateCompleteDocument();
        var rewriter = new CompleteRewriter();

        var rewritten = Assert.IsType<SqlDocument>(document.Accept(rewriter));

        Assert.NotSame(document, rewritten);
        Assert.Equal(
            AllNodeTypes.OrderBy(static type => type.Name),
            rewriter.Visited.OrderBy(static type => type.Name));
        Assert.All(
            rewritten.FindAll<SqlIdentifier>(),
            static identifier => Assert.False(identifier.Span.IsEmpty));
        Assert.All(
            rewritten.FindAll<LiteralExpression>(),
            static literal => Assert.False(literal.Span.IsEmpty));
        Assert.All(
            document.DescendantsAndSelf(),
            static node => Assert.True(node.Span.IsEmpty));
    }

    private static SqlDocument CreateCompleteDocument()
    {
        var subquery = SimpleSelect("subquery_source");
        var cteQuery = SimpleSelect("cte_source");
        var derivedQuery = SimpleSelect("derived_source");
        var caseExpression = new CaseExpression(
            new ColumnExpression("status"),
            [
                new WhenClause(
                    new BetweenExpression(
                        new ColumnExpression("score"),
                        new LiteralExpression(1),
                        new LiteralExpression(10)),
                    new CastExpression(
                        new LiteralExpression("matched"),
                        new SqlDataType("VARCHAR", 20))),
            ],
            new FunctionCallExpression(
                new SqlIdentifier("COALESCE"),
                [
                    new ParameterExpression("fallback", DefaultValue: new LiteralExpression("unknown")),
                    new StarExpression([new SqlIdentifier("source")]),
                ]));

        var select = new SelectStatement(
            [
                new SelectItem(caseExpression, new SqlIdentifier("category")),
                new SelectItem(new SubqueryExpression(subquery)),
                new SelectItem(new ExistsExpression(subquery)),
                new SelectItem(new ParenthesizedExpression(new ColumnExpression("grouped"))),
                new SelectItem(new WindowExpression(
                    new FunctionCallExpression("ROW_NUMBER"),
                    [new ColumnExpression("region")],
                    [new OrderByItem(new ColumnExpression("created_at"))])),
                new SelectItem(new UnaryExpression(
                    UnaryOperator.Not,
                    new IsNullExpression(new ColumnExpression("deleted_at")))),
                new SelectItem(new InExpression(
                    new ColumnExpression("state"),
                    [new LiteralExpression("active"), new LiteralExpression("pending")])),
                new SelectItem(new InExpression(new ColumnExpression("owner_id"), subquery)),
            ],
            new JoinTable(
                new NamedTable(new TableName("users"), new SqlIdentifier("u")),
                new DerivedTable(derivedQuery, new SqlIdentifier("d")),
                JoinKind.Left,
                new BinaryExpression(
                    new ColumnExpression("u.id"),
                    BinaryOperator.Equal,
                    new ColumnExpression("d.user_id"))),
            new BinaryExpression(
                new ColumnExpression("u.enabled"),
                BinaryOperator.Equal,
                new LiteralExpression(true)),
            [new ColumnExpression("u.id")],
            new BinaryExpression(
                new FunctionCallExpression("COUNT", new StarExpression()),
                BinaryOperator.GreaterThan,
                new LiteralExpression(0)),
            [new OrderByItem(new ColumnExpression("u.id"), OrderDirection.Descending, NullOrder.Last)],
            new LiteralExpression(10),
            new LiteralExpression(2),
            true,
            [
                new CommonTableExpression(
                    new SqlIdentifier("recent"),
                    cteQuery,
                    [new SqlIdentifier("id")]),
            ],
            new LiteralExpression(5));

        var set = new SetOperationStatement(
            SimpleSelect("current"),
            SetOperator.Union,
            SimpleSelect("archive"),
            true,
            [new OrderByItem(new ColumnExpression("id"))],
            new LiteralExpression(20),
            new LiteralExpression(4));

        var insert = new InsertStatement(
            new TableName("target"),
            [new SqlIdentifier("id"), new SqlIdentifier("name")],
            [[new LiteralExpression(1), new LiteralExpression("Ada")]],
            SimpleSelect("insert_source"),
            [new ColumnExpression("id")]);

        var update = new UpdateStatement(
            new NamedTable("target", "t"),
            [new Assignment(new ColumnExpression("name"), new LiteralExpression("Grace"))],
            new BinaryExpression(
                new ColumnExpression("id"),
                BinaryOperator.Equal,
                new ParameterExpression("id")),
            [new ColumnExpression("id")]);

        var delete = new DeleteStatement(
            new NamedTable("target"),
            new IsNullExpression(new ColumnExpression("deleted_at"), true),
            [new ColumnExpression("id")]);

        return new SqlDocument(
            select,
            set,
            new ExplainStatement(SimpleSelect("explain_source")),
            insert,
            update,
            delete);
    }

    private static SelectStatement SimpleSelect(string table) =>
        new(
            [new SelectItem(new ColumnExpression("id"))],
            new NamedTable(table));

    private sealed record UnknownNode : SqlNode;

    private sealed class CompleteVisitor : SqlVisitor
    {
        public HashSet<Type> Visited { get; } = [];

        protected override void VisitDocument(SqlDocument node) { Mark(node); base.VisitDocument(node); }
        protected override void VisitSelect(SelectStatement node) { Mark(node); base.VisitSelect(node); }
        protected override void VisitSetOperation(SetOperationStatement node) { Mark(node); base.VisitSetOperation(node); }
        protected override void VisitExplain(ExplainStatement node) { Mark(node); base.VisitExplain(node); }
        protected override void VisitInsert(InsertStatement node) { Mark(node); base.VisitInsert(node); }
        protected override void VisitUpdate(UpdateStatement node) { Mark(node); base.VisitUpdate(node); }
        protected override void VisitDelete(DeleteStatement node) { Mark(node); base.VisitDelete(node); }
        protected override void VisitIdentifier(SqlIdentifier node) { Mark(node); base.VisitIdentifier(node); }
        protected override void VisitColumn(ColumnExpression node) { Mark(node); base.VisitColumn(node); }
        protected override void VisitStar(StarExpression node) { Mark(node); base.VisitStar(node); }
        protected override void VisitLiteral(LiteralExpression node) { Mark(node); base.VisitLiteral(node); }
        protected override void VisitParameter(ParameterExpression node) { Mark(node); base.VisitParameter(node); }
        protected override void VisitParenthesized(ParenthesizedExpression node) { Mark(node); base.VisitParenthesized(node); }
        protected override void VisitUnary(UnaryExpression node) { Mark(node); base.VisitUnary(node); }
        protected override void VisitBinary(BinaryExpression node) { Mark(node); base.VisitBinary(node); }
        protected override void VisitBetween(BetweenExpression node) { Mark(node); base.VisitBetween(node); }
        protected override void VisitIn(InExpression node) { Mark(node); base.VisitIn(node); }
        protected override void VisitIsNull(IsNullExpression node) { Mark(node); base.VisitIsNull(node); }
        protected override void VisitFunctionCall(FunctionCallExpression node) { Mark(node); base.VisitFunctionCall(node); }
        protected override void VisitWindow(WindowExpression node) { Mark(node); base.VisitWindow(node); }
        protected override void VisitExists(ExistsExpression node) { Mark(node); base.VisitExists(node); }
        protected override void VisitSubquery(SubqueryExpression node) { Mark(node); base.VisitSubquery(node); }
        protected override void VisitWhen(WhenClause node) { Mark(node); base.VisitWhen(node); }
        protected override void VisitCase(CaseExpression node) { Mark(node); base.VisitCase(node); }
        protected override void VisitCast(CastExpression node) { Mark(node); base.VisitCast(node); }
        protected override void VisitDataType(SqlDataType node) { Mark(node); base.VisitDataType(node); }
        protected override void VisitTableName(TableName node) { Mark(node); base.VisitTableName(node); }
        protected override void VisitNamedTable(NamedTable node) { Mark(node); base.VisitNamedTable(node); }
        protected override void VisitDerivedTable(DerivedTable node) { Mark(node); base.VisitDerivedTable(node); }
        protected override void VisitJoin(JoinTable node) { Mark(node); base.VisitJoin(node); }
        protected override void VisitSelectItem(SelectItem node) { Mark(node); base.VisitSelectItem(node); }
        protected override void VisitOrderByItem(OrderByItem node) { Mark(node); base.VisitOrderByItem(node); }
        protected override void VisitCommonTableExpression(CommonTableExpression node) { Mark(node); base.VisitCommonTableExpression(node); }
        protected override void VisitAssignment(Assignment node) { Mark(node); base.VisitAssignment(node); }

        private void Mark(SqlNode node) => Visited.Add(node.GetType());
    }

    private sealed class CompleteRewriter : SqlRewriter
    {
        public HashSet<Type> Visited { get; } = [];

        protected override SqlNode VisitDocument(SqlDocument node) => Mark(node, base.VisitDocument(node));
        protected override SqlNode VisitSelect(SelectStatement node) => Mark(node, base.VisitSelect(node));
        protected override SqlNode VisitSetOperation(SetOperationStatement node) => Mark(node, base.VisitSetOperation(node));
        protected override SqlNode VisitExplain(ExplainStatement node) => Mark(node, base.VisitExplain(node));
        protected override SqlNode VisitInsert(InsertStatement node) => Mark(node, base.VisitInsert(node));
        protected override SqlNode VisitUpdate(UpdateStatement node) => Mark(node, base.VisitUpdate(node));
        protected override SqlNode VisitDelete(DeleteStatement node) => Mark(node, base.VisitDelete(node));
        protected override SqlNode VisitColumn(ColumnExpression node) => Mark(node, base.VisitColumn(node));
        protected override SqlNode VisitStar(StarExpression node) => Mark(node, base.VisitStar(node));
        protected override SqlNode VisitParenthesized(ParenthesizedExpression node) => Mark(node, base.VisitParenthesized(node));
        protected override SqlNode VisitUnary(UnaryExpression node) => Mark(node, base.VisitUnary(node));
        protected override SqlNode VisitBinary(BinaryExpression node) => Mark(node, base.VisitBinary(node));
        protected override SqlNode VisitBetween(BetweenExpression node) => Mark(node, base.VisitBetween(node));
        protected override SqlNode VisitIn(InExpression node) => Mark(node, base.VisitIn(node));
        protected override SqlNode VisitIsNull(IsNullExpression node) => Mark(node, base.VisitIsNull(node));
        protected override SqlNode VisitFunctionCall(FunctionCallExpression node) => Mark(node, base.VisitFunctionCall(node));
        protected override SqlNode VisitWindow(WindowExpression node) => Mark(node, base.VisitWindow(node));
        protected override SqlNode VisitExists(ExistsExpression node) => Mark(node, base.VisitExists(node));
        protected override SqlNode VisitSubquery(SubqueryExpression node) => Mark(node, base.VisitSubquery(node));
        protected override SqlNode VisitWhen(WhenClause node) => Mark(node, base.VisitWhen(node));
        protected override SqlNode VisitCase(CaseExpression node) => Mark(node, base.VisitCase(node));
        protected override SqlNode VisitCast(CastExpression node) => Mark(node, base.VisitCast(node));
        protected override SqlNode VisitDataType(SqlDataType node) => Mark(node, base.VisitDataType(node));
        protected override SqlNode VisitTableName(TableName node) => Mark(node, base.VisitTableName(node));
        protected override SqlNode VisitNamedTable(NamedTable node) => Mark(node, base.VisitNamedTable(node));
        protected override SqlNode VisitDerivedTable(DerivedTable node) => Mark(node, base.VisitDerivedTable(node));
        protected override SqlNode VisitJoin(JoinTable node) => Mark(node, base.VisitJoin(node));
        protected override SqlNode VisitSelectItem(SelectItem node) => Mark(node, base.VisitSelectItem(node));
        protected override SqlNode VisitOrderByItem(OrderByItem node) => Mark(node, base.VisitOrderByItem(node));
        protected override SqlNode VisitCommonTableExpression(CommonTableExpression node) => Mark(node, base.VisitCommonTableExpression(node));
        protected override SqlNode VisitAssignment(Assignment node) => Mark(node, base.VisitAssignment(node));

        protected override SqlNode VisitIdentifier(SqlIdentifier node) =>
            Mark(node, node with { Span = new SqlTextSpan(0, 0) });

        protected override SqlNode VisitLiteral(LiteralExpression node) =>
            Mark(node, node with { Span = new SqlTextSpan(0, 0) });

        protected override SqlNode VisitParameter(ParameterExpression node)
        {
            var rewritten = (ParameterExpression)base.VisitParameter(node);
            return Mark(node, rewritten with { Span = new SqlTextSpan(0, 0) });
        }

        private SqlNode Mark(SqlNode original, SqlNode rewritten)
        {
            Visited.Add(original.GetType());
            return rewritten;
        }
    }

    private sealed class IdentityRewriter : SqlRewriter;
}
