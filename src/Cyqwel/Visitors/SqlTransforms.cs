using Cyqwel.Ast;

namespace Cyqwel.Visitors;

public static class SqlTransforms
{
    public static SelectStatement AddWhere(this SelectStatement select, SqlExpression predicate)
    {
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(predicate);
        return select with
        {
            Where = select.Where is null
                ? predicate
                : new BinaryExpression(select.Where, BinaryOperator.And, predicate),
        };
    }

    public static SelectStatement SetLimit(this SelectStatement select, long limit) =>
        select with { Limit = new LiteralExpression(limit) };

    public static T RenameTable<T>(this T node, string from, string to) where T : SqlNode =>
        new RenameRewriter(from, to, null, null).Visit(node);

    public static T RenameColumn<T>(this T node, string from, string to) where T : SqlNode =>
        new RenameRewriter(null, null, from, to).Visit(node);

    private sealed class RenameRewriter(
        string? tableFrom,
        string? tableTo,
        string? columnFrom,
        string? columnTo) : SqlRewriter
    {
        protected override SqlNode VisitTableName(TableName node)
        {
            var rewritten = (TableName)base.VisitTableName(node);
            if (tableFrom is null || tableTo is null) return rewritten;

            var fullName = (rewritten.IsVariable ? "@" : "") +
                string.Join('.', rewritten.Parts.Select(static part => part.Value));
            if (!fullName.Equals(tableFrom, StringComparison.OrdinalIgnoreCase)) return rewritten;
            return rewritten.IsVariable
                ? Sql.TableVariable(tableTo).Name with { Span = node.Span }
                : new TableName(tableTo) with { Span = node.Span };
        }

        protected override SqlNode VisitColumn(ColumnExpression node)
        {
            var rewritten = (ColumnExpression)base.VisitColumn(node);
            if (columnFrom is null || columnTo is null || rewritten.Parts.Count == 0) return rewritten;

            var last = rewritten.Parts[^1];
            if (!last.Value.Equals(columnFrom, StringComparison.OrdinalIgnoreCase)) return rewritten;

            var parts = rewritten.Parts.ToArray();
            parts[^1] = last with { Value = columnTo };
            return rewritten with { Parts = parts };
        }

        protected override SqlNode VisitUnpivotTable(UnpivotTable node)
        {
            var rewritten = (UnpivotTable)base.VisitUnpivotTable(node);
            if (columnFrom is null || columnTo is null) return rewritten;
            SqlIdentifier[]? columns = null;
            for (var i = 0; i < rewritten.Columns.Count; i++)
            {
                if (!rewritten.Columns[i].Value.Equals(columnFrom, StringComparison.OrdinalIgnoreCase)) continue;
                columns ??= rewritten.Columns.ToArray();
                columns[i] = columns[i] with { Value = columnTo };
            }
            return columns is null ? rewritten : rewritten with { Columns = columns };
        }
    }
}
