using Cyqwel.Ast;

namespace Cyqwel.Visitors;

internal static partial class SqlNodeChildren
{
    public static IEnumerable<SqlNode> Get(SqlNode node)
    {
        switch (node)
        {
            case SqlDocument value:
                return value.Statements;
            case SelectStatement value:
                return SelectChildren(value);
            case ValuesStatement value:
                return ValuesChildren(value);
            case SetOperationStatement value:
                return SetOperationChildren(value);
            case ExplainStatement value:
                return [value.Query];
            case InsertStatement value:
                return InsertChildren(value);
            case UpdateStatement value:
                return UpdateChildren(value);
            case DeleteStatement value:
                return DeleteChildren(value);
            case MergeStatement value:
                return MergeChildren(value);
            case GrantStatement value:
                return GrantChildren(value);
            case SetStatement value:
                return SetChildren(value);
            case CreateTableStatement value:
                return CreateTableChildren(value);
            case AlterTableStatement value:
                return [value.Name, .. value.Actions];
            case DropStatement value:
                return value.Names;
            case TruncateStatement value:
                return value.Tables;
            case CreateViewStatement value:
                return CreateViewChildren(value);
            case CreateIndexStatement value:
                return CreateIndexChildren(value);
            case CreateSequenceStatement value:
                return [value.Name, value.Options];
            case AlterSequenceStatement value:
                return [value.Name, value.Options];
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
            case BooleanTestExpression value:
                return [value.Expression];
            case DistinctFromExpression value:
                return [value.Left, value.Right];
            case RowExpression value:
                return value.Values;
            case CollateExpression value:
                return [value.Expression, value.Collation];
            case ExtractExpression value:
                return [value.Field, value.Expression];
            case IntervalExpression value:
                return [value.Value, value.Unit];
            case SequenceValueExpression value:
                return [value.Sequence];
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
            case TryCastExpression value:
                return [value.Expression, value.DataType];
            case SqlDataType value:
                return value.IntervalEndField is null
                    ? [value.Name]
                    : [value.Name, value.IntervalEndField];
            case TableName value:
                return value.Parts;
            case NamedTable value:
                return value.Alias is null ? [value.Name] : [value.Name, value.Alias];
            case DerivedTable value:
                return [value.Query, value.Alias];
            case JoinTable value:
                return JoinChildren(value);
            case SelectItem value:
                return value.Alias is null ? [value.Expression] : [value.Expression, value.Alias];
            case OrderByItem value:
                return [value.Expression];
            case CommonTableExpression value:
                return CommonTableExpressionChildren(value);
            case Assignment value:
                return [value.Column, value.Value];
            case WindowDefinition value:
                return WindowDefinitionChildren(value);
            case WindowFrame value:
                return value.End is null ? [value.Start] : [value.Start, value.End];
            case WindowFrameBound value:
                return value.Offset is null ? Array.Empty<SqlNode>() : [value.Offset];
            case ConnectByClause value:
                return value.StartWith is null
                    ? [value.Condition]
                    : [value.StartWith, value.Condition];
            case MergeWhenClause value:
                return value.Condition is null ? [value.Action] : [value.Condition, value.Action];
            case MergeUpdateAction value:
                return value.DeleteWhere is null
                    ? value.Assignments
                    : [.. value.Assignments, value.DeleteWhere];
            case MergeInsertAction value:
                return value.Columns is null
                    ? value.Values
                    : [.. value.Columns, .. value.Values];
            case ColumnDefinition value:
                return ColumnDefinitionChildren(value);
            case IndexTableElement value:
                return IndexTableElementChildren(value);
            case PrimaryKeyConstraint value:
                return ConstraintChildren(value.Name, value.Columns);
            case UniqueConstraint value:
                return ConstraintChildren(value.Name, value.Columns);
            case ForeignKeyConstraint value:
                return ForeignKeyChildren(value);
            case CheckConstraint value:
                return value.Name is null ? [value.Condition] : [value.Name, value.Condition];
            case AddColumnAction value:
                return [value.Column];
            case DropColumnAction value:
                return [value.Column];
            case AlterColumnAction value:
                return AlterColumnChildren(value);
            case AddConstraintAction value:
                return [value.Constraint];
            case DropConstraintAction value:
                return [value.Constraint];
            case RenameColumnAction value:
                return [value.Column, value.NewName];
            case RenameTableAction value:
                return [value.NewName];
            case IndexColumn value:
                return [value.Expression];
            case SequenceOptions value:
                return SequenceOptionChildren(value);
            case SqlIdentifier:
            case LiteralExpression:
            case DefaultExpression:
            case MergeDeleteAction:
                return Array.Empty<SqlNode>();
            case TrimExpression value:
                return TrimChildren(value);
            case TypedLiteralExpression value:
                return TypedLiteralChildren(value);
            case HexLiteralExpression value:
                return HexLiteralChildren(value);
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

        if (node.Windows is not null)
        {
            foreach (var window in node.Windows) yield return window;
        }

        if (node.Qualify is not null) yield return node.Qualify;
        if (node.ConnectBy is not null) yield return node.ConnectBy;

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

        if (node.CommonTableExpressions is not null)
        {
            foreach (var cte in node.CommonTableExpressions) yield return cte;
        }

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

        if (node.ReturningInto is not null)
        {
            foreach (var expression in node.ReturningInto) yield return expression;
        }
    }

    private static IEnumerable<SqlNode> UpdateChildren(UpdateStatement node)
    {
        yield return node.Target;
        foreach (var assignment in node.Assignments) yield return assignment;
        if (node.From is not null) yield return node.From;
        if (node.Where is not null) yield return node.Where;

        if (node.Returning is not null)
        {
            foreach (var expression in node.Returning) yield return expression;
        }

        if (node.ReturningInto is not null)
        {
            foreach (var expression in node.ReturningInto) yield return expression;
        }
    }

    private static IEnumerable<SqlNode> DeleteChildren(DeleteStatement node)
    {
        yield return node.Target;
        if (node.Using is not null) yield return node.Using;
        if (node.Where is not null) yield return node.Where;

        if (node.Returning is not null)
        {
            foreach (var expression in node.Returning) yield return expression;
        }

        if (node.ReturningInto is not null)
        {
            foreach (var expression in node.ReturningInto) yield return expression;
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
        if (node.Filter is not null) yield return node.Filter;
        if (node.WithinGroup is not null)
        {
            foreach (var item in node.WithinGroup) yield return item;
        }
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

        if (node.Frame is not null) yield return node.Frame;
        if (node.WindowName is not null) yield return node.WindowName;
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

    private static IEnumerable<SqlNode> JoinChildren(JoinTable node)
    {
        yield return node.Left;
        yield return node.Right;
        if (node.Condition is not null) yield return node.Condition;
        if (node.Using is not null)
        {
            foreach (var column in node.Using) yield return column;
        }
    }

    private static IEnumerable<SqlNode> TrimChildren(TrimExpression node)
    {
        if (node.Character is not null) yield return node.Character;
        yield return node.Source;
    }

    private static IEnumerable<SqlNode> TypedLiteralChildren(TypedLiteralExpression node)
    {
        yield return node.TypeName;
        yield return node.Value;
    }

    private static IEnumerable<SqlNode> HexLiteralChildren(HexLiteralExpression node) => Array.Empty<SqlNode>();

    private static IEnumerable<SqlNode> GrantChildren(GrantStatement node)
    {
        foreach (var item in node.Objects) yield return item;
        foreach (var item in node.Grantees) yield return item;
    }

    private static IEnumerable<SqlNode> SetChildren(SetStatement node)
    {
        foreach (var item in node.Keywords) yield return item;
        foreach (var item in node.Arguments) yield return item;
    }
}
