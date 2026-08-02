using Cyqwel.Ast;

namespace Cyqwel.Visitors;

/// <summary>
/// Traverses SQL nodes without modifying them. Override typed methods to analyze selected nodes.
/// </summary>
public abstract class SqlVisitor
{
    public virtual void Visit(SqlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        switch (node)
        {
            case SqlDocument value: VisitDocument(value); break;
            case SelectStatement value: VisitSelect(value); break;
            case SetOperationStatement value: VisitSetOperation(value); break;
            case InsertStatement value: VisitInsert(value); break;
            case UpdateStatement value: VisitUpdate(value); break;
            case DeleteStatement value: VisitDelete(value); break;
            case SqlIdentifier value: VisitIdentifier(value); break;
            case ColumnExpression value: VisitColumn(value); break;
            case StarExpression value: VisitStar(value); break;
            case LiteralExpression value: VisitLiteral(value); break;
            case ParameterExpression value: VisitParameter(value); break;
            case ParenthesizedExpression value: VisitParenthesized(value); break;
            case UnaryExpression value: VisitUnary(value); break;
            case BinaryExpression value: VisitBinary(value); break;
            case BetweenExpression value: VisitBetween(value); break;
            case InExpression value: VisitIn(value); break;
            case IsNullExpression value: VisitIsNull(value); break;
            case FunctionCallExpression value: VisitFunctionCall(value); break;
            case WindowExpression value: VisitWindow(value); break;
            case ExistsExpression value: VisitExists(value); break;
            case SubqueryExpression value: VisitSubquery(value); break;
            case WhenClause value: VisitWhen(value); break;
            case CaseExpression value: VisitCase(value); break;
            case CastExpression value: VisitCast(value); break;
            case SqlDataType value: VisitDataType(value); break;
            case TableName value: VisitTableName(value); break;
            case NamedTable value: VisitNamedTable(value); break;
            case DerivedTable value: VisitDerivedTable(value); break;
            case JoinTable value: VisitJoin(value); break;
            case SelectItem value: VisitSelectItem(value); break;
            case OrderByItem value: VisitOrderByItem(value); break;
            case CommonTableExpression value: VisitCommonTableExpression(value); break;
            case Assignment value: VisitAssignment(value); break;
            default: throw new NotSupportedException($"Unsupported SQL node type '{node.GetType().Name}'.");
        }
    }

    protected virtual void DefaultVisit(SqlNode node)
    {
        foreach (var child in SqlNodeChildren.Get(node))
        {
            Visit(child);
        }
    }

    protected virtual void VisitDocument(SqlDocument node) => DefaultVisit(node);
    protected virtual void VisitSelect(SelectStatement node) => DefaultVisit(node);
    protected virtual void VisitSetOperation(SetOperationStatement node) => DefaultVisit(node);
    protected virtual void VisitInsert(InsertStatement node) => DefaultVisit(node);
    protected virtual void VisitUpdate(UpdateStatement node) => DefaultVisit(node);
    protected virtual void VisitDelete(DeleteStatement node) => DefaultVisit(node);
    protected virtual void VisitIdentifier(SqlIdentifier node) => DefaultVisit(node);
    protected virtual void VisitColumn(ColumnExpression node) => DefaultVisit(node);
    protected virtual void VisitStar(StarExpression node) => DefaultVisit(node);
    protected virtual void VisitLiteral(LiteralExpression node) => DefaultVisit(node);
    protected virtual void VisitParameter(ParameterExpression node) => DefaultVisit(node);
    protected virtual void VisitParenthesized(ParenthesizedExpression node) => DefaultVisit(node);
    protected virtual void VisitUnary(UnaryExpression node) => DefaultVisit(node);
    protected virtual void VisitBinary(BinaryExpression node) => DefaultVisit(node);
    protected virtual void VisitBetween(BetweenExpression node) => DefaultVisit(node);
    protected virtual void VisitIn(InExpression node) => DefaultVisit(node);
    protected virtual void VisitIsNull(IsNullExpression node) => DefaultVisit(node);
    protected virtual void VisitFunctionCall(FunctionCallExpression node) => DefaultVisit(node);
    protected virtual void VisitWindow(WindowExpression node) => DefaultVisit(node);
    protected virtual void VisitExists(ExistsExpression node) => DefaultVisit(node);
    protected virtual void VisitSubquery(SubqueryExpression node) => DefaultVisit(node);
    protected virtual void VisitWhen(WhenClause node) => DefaultVisit(node);
    protected virtual void VisitCase(CaseExpression node) => DefaultVisit(node);
    protected virtual void VisitCast(CastExpression node) => DefaultVisit(node);
    protected virtual void VisitDataType(SqlDataType node) => DefaultVisit(node);
    protected virtual void VisitTableName(TableName node) => DefaultVisit(node);
    protected virtual void VisitNamedTable(NamedTable node) => DefaultVisit(node);
    protected virtual void VisitDerivedTable(DerivedTable node) => DefaultVisit(node);
    protected virtual void VisitJoin(JoinTable node) => DefaultVisit(node);
    protected virtual void VisitSelectItem(SelectItem node) => DefaultVisit(node);
    protected virtual void VisitOrderByItem(OrderByItem node) => DefaultVisit(node);
    protected virtual void VisitCommonTableExpression(CommonTableExpression node) => DefaultVisit(node);
    protected virtual void VisitAssignment(Assignment node) => DefaultVisit(node);
}
