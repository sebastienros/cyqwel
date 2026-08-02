using Cyqwel.Ast;

namespace Cyqwel.Visitors;

internal static class SqlNodeChildren
{
    public static IEnumerable<SqlNode> Get(SqlNode node)
    {
        switch (node)
        {
            case SqlDocument value:
                return value.Statements;
            case SelectStatement value:
                return SelectChildren(value);
            case SetOperationStatement value:
                return SetOperationChildren(value);
            case InsertStatement value:
                return InsertChildren(value);
            case UpdateStatement value:
                return UpdateChildren(value);
            case DeleteStatement value:
                return DeleteChildren(value);
            case ColumnExpression value:
                return value.Parts;
            case StarExpression value:
                return value.Qualifier ?? Array.Empty<SqlIdentifier>();
            case ParenthesizedExpression value:
                return [value.Expression];
            case UnaryExpression value:
                return [value.Operand];
            case BinaryExpression value:
                return [value.Left, value.Right];
            case BetweenExpression value:
                return [value.Expression, value.Lower, value.Upper];
            case InExpression value:
                return InChildren(value);
            case IsNullExpression value:
                return [value.Expression];
            case FunctionCallExpression value:
                return FunctionChildren(value);
            case WindowExpression value:
                return WindowChildren(value);
            case ExistsExpression value:
                return [value.Query];
            case SubqueryExpression value:
                return [value.Query];
            case WhenClause value:
                return [value.Condition, value.Result];
            case CaseExpression value:
                return CaseChildren(value);
            case CastExpression value:
                return [value.Expression, value.DataType];
            case SqlDataType value:
                return [value.Name];
            case TableName value:
                return value.Parts;
            case NamedTable value:
                return value.Alias is null ? [value.Name] : [value.Name, value.Alias];
            case DerivedTable value:
                return [value.Query, value.Alias];
            case JoinTable value:
                return value.Condition is null
                    ? [value.Left, value.Right]
                    : [value.Left, value.Right, value.Condition];
            case SelectItem value:
                return value.Alias is null ? [value.Expression] : [value.Expression, value.Alias];
            case OrderByItem value:
                return [value.Expression];
            case CommonTableExpression value:
                return CommonTableExpressionChildren(value);
            case Assignment value:
                return [value.Column, value.Value];
            case SqlIdentifier:
            case LiteralExpression:
                return Array.Empty<SqlNode>();
            case ParameterExpression value:
                return value.DefaultValue is null
                    ? Array.Empty<SqlNode>()
                    : [value.DefaultValue];
            default:
                throw new NotSupportedException($"Unsupported SQL node type '{node.GetType().Name}'.");
        }
    }

    private static IEnumerable<SqlNode> SelectChildren(SelectStatement node)
    {
        if (node.CommonTableExpressions is not null)
        {
            foreach (var cte in node.CommonTableExpressions) yield return cte;
        }

        if (node.Top is not null) yield return node.Top;
        foreach (var projection in node.Projections) yield return projection;
        if (node.From is not null) yield return node.From;
        if (node.Where is not null) yield return node.Where;

        if (node.GroupBy is not null)
        {
            foreach (var expression in node.GroupBy) yield return expression;
        }

        if (node.Having is not null) yield return node.Having;

        if (node.OrderBy is not null)
        {
            foreach (var item in node.OrderBy) yield return item;
        }

        if (node.Limit is not null) yield return node.Limit;
        if (node.Offset is not null) yield return node.Offset;
    }

    private static IEnumerable<SqlNode> SetOperationChildren(SetOperationStatement node)
    {
        yield return node.Left;
        yield return node.Right;

        if (node.OrderBy is not null)
        {
            foreach (var item in node.OrderBy) yield return item;
        }

        if (node.Limit is not null) yield return node.Limit;
        if (node.Offset is not null) yield return node.Offset;
    }

    private static IEnumerable<SqlNode> InsertChildren(InsertStatement node)
    {
        yield return node.Target;

        if (node.Columns is not null)
        {
            foreach (var column in node.Columns) yield return column;
        }

        if (node.Values is not null)
        {
            foreach (var row in node.Values)
            {
                foreach (var expression in row) yield return expression;
            }
        }

        if (node.Source is not null) yield return node.Source;

        if (node.Returning is not null)
        {
            foreach (var expression in node.Returning) yield return expression;
        }
    }

    private static IEnumerable<SqlNode> UpdateChildren(UpdateStatement node)
    {
        yield return node.Target;
        foreach (var assignment in node.Assignments) yield return assignment;
        if (node.Where is not null) yield return node.Where;

        if (node.Returning is not null)
        {
            foreach (var expression in node.Returning) yield return expression;
        }
    }

    private static IEnumerable<SqlNode> DeleteChildren(DeleteStatement node)
    {
        yield return node.Target;
        if (node.Where is not null) yield return node.Where;

        if (node.Returning is not null)
        {
            foreach (var expression in node.Returning) yield return expression;
        }
    }

    private static IEnumerable<SqlNode> InChildren(InExpression node)
    {
        yield return node.Expression;
        foreach (var value in node.Values) yield return value;
        if (node.Query is not null) yield return node.Query;
    }

    private static IEnumerable<SqlNode> FunctionChildren(FunctionCallExpression node)
    {
        yield return node.Name;
        foreach (var argument in node.Arguments) yield return argument;
    }

    private static IEnumerable<SqlNode> WindowChildren(WindowExpression node)
    {
        yield return node.Expression;
        if (node.PartitionBy is not null)
        {
            foreach (var expression in node.PartitionBy) yield return expression;
        }

        if (node.OrderBy is not null)
        {
            foreach (var item in node.OrderBy) yield return item;
        }
    }

    private static IEnumerable<SqlNode> CaseChildren(CaseExpression node)
    {
        if (node.Operand is not null) yield return node.Operand;
        foreach (var when in node.Whens) yield return when;
        if (node.Else is not null) yield return node.Else;
    }

    private static IEnumerable<SqlNode> CommonTableExpressionChildren(CommonTableExpression node)
    {
        yield return node.Name;

        if (node.Columns is not null)
        {
            foreach (var column in node.Columns) yield return column;
        }

        yield return node.Query;
    }
}
