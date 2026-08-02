using Cyqwel.Ast;

namespace Cyqwel.Visitors;

internal static partial class SqlNodeChildren
{
    private static IEnumerable<SqlNode> ValuesChildren(ValuesStatement node)
    {
        if (node.CommonTableExpressions is not null)
        {
            foreach (var cte in node.CommonTableExpressions) yield return cte;
        }

        foreach (var row in node.Rows)
        {
            foreach (var value in row) yield return value;
        }

        if (node.OrderBy is not null)
        {
            foreach (var item in node.OrderBy) yield return item;
        }

        if (node.Limit is not null) yield return node.Limit;
        if (node.Offset is not null) yield return node.Offset;
    }

    private static IEnumerable<SqlNode> MergeChildren(MergeStatement node)
    {
        yield return node.Target;
        yield return node.Source;
        yield return node.Condition;
        foreach (var when in node.WhenClauses) yield return when;
        if (node.Returning is not null)
        {
            foreach (var value in node.Returning) yield return value;
        }

        if (node.ReturningInto is not null)
        {
            foreach (var value in node.ReturningInto) yield return value;
        }
    }

    private static IEnumerable<SqlNode> CreateTableChildren(CreateTableStatement node)
    {
        yield return node.Name;
        foreach (var element in node.Elements) yield return element;
        if (node.AsQuery is not null) yield return node.AsQuery;
    }

    private static IEnumerable<SqlNode> CreateViewChildren(CreateViewStatement node)
    {
        yield return node.Name;
        if (node.Columns is not null)
        {
            foreach (var column in node.Columns) yield return column;
        }

        yield return node.Query;
    }

    private static IEnumerable<SqlNode> CreateIndexChildren(CreateIndexStatement node)
    {
        yield return node.Name;
        yield return node.Table;
        foreach (var column in node.Columns) yield return column;
        if (node.Where is not null) yield return node.Where;
    }

    private static IEnumerable<SqlNode> WindowDefinitionChildren(WindowDefinition node)
    {
        yield return node.Name;
        if (node.BaseWindow is not null) yield return node.BaseWindow;
        if (node.PartitionBy is not null)
        {
            foreach (var value in node.PartitionBy) yield return value;
        }

        if (node.OrderBy is not null)
        {
            foreach (var value in node.OrderBy) yield return value;
        }

        if (node.Frame is not null) yield return node.Frame;
    }

    private static IEnumerable<SqlNode> ColumnDefinitionChildren(ColumnDefinition node)
    {
        yield return node.Name;
        yield return node.DataType;
        if (node.Default is not null) yield return node.Default;
        if (node.GeneratedExpression is not null) yield return node.GeneratedExpression;
    }

    private static IEnumerable<SqlNode> ConstraintChildren(
        SqlIdentifier? name,
        IReadOnlyList<SqlIdentifier> columns)
    {
        if (name is not null) yield return name;
        foreach (var column in columns) yield return column;
    }

    private static IEnumerable<SqlNode> ForeignKeyChildren(ForeignKeyConstraint node)
    {
        if (node.Name is not null) yield return node.Name;
        foreach (var column in node.Columns) yield return column;
        yield return node.ReferencedTable;
        foreach (var column in node.ReferencedColumns) yield return column;
    }

    private static IEnumerable<SqlNode> AlterColumnChildren(AlterColumnAction node)
    {
        yield return node.Column;
        if (node.DataType is not null) yield return node.DataType;
        if (node.Default is not null) yield return node.Default;
    }

    private static IEnumerable<SqlNode> SequenceOptionChildren(SequenceOptions node)
    {
        if (node.StartWith is not null) yield return node.StartWith;
        if (node.IncrementBy is not null) yield return node.IncrementBy;
        if (node.MinimumValue is not null) yield return node.MinimumValue;
        if (node.MaximumValue is not null) yield return node.MaximumValue;
        if (node.Cache is not null) yield return node.Cache;
    }
}
