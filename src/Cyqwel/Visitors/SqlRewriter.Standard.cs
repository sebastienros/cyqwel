using Cyqwel.Ast;

namespace Cyqwel.Visitors;

public abstract partial class SqlRewriter
{
    protected virtual SqlNode VisitValues(ValuesStatement node)
    {
        var rows = VisitRows(node.Rows)!;
        var orderBy = VisitOptionalList(node.OrderBy);
        var limit = VisitOptional(node.Limit);
        var offset = VisitOptional(node.Offset);
        var ctes = VisitOptionalList(node.CommonTableExpressions);
        return ReferenceEquals(rows, node.Rows)
            && ReferenceEquals(orderBy, node.OrderBy)
            && ReferenceEquals(limit, node.Limit)
            && ReferenceEquals(offset, node.Offset)
            && ReferenceEquals(ctes, node.CommonTableExpressions)
                ? node
                : node with
                {
                    Rows = rows,
                    OrderBy = orderBy,
                    Limit = limit,
                    Offset = offset,
                    CommonTableExpressions = ctes,
                };
    }

    protected virtual SqlNode VisitMerge(MergeStatement node)
    {
        var target = Visit(node.Target);
        var source = Visit(node.Source);
        var condition = Visit(node.Condition);
        var whenClauses = VisitList(node.WhenClauses);
        var returning = VisitOptionalList(node.Returning);
        var returningInto = VisitOptionalList(node.ReturningInto);
        return ReferenceEquals(target, node.Target)
            && ReferenceEquals(source, node.Source)
            && ReferenceEquals(condition, node.Condition)
            && ReferenceEquals(whenClauses, node.WhenClauses)
            && ReferenceEquals(returning, node.Returning)
            && ReferenceEquals(returningInto, node.ReturningInto)
                ? node
                : node with
                {
                    Target = target,
                    Source = source,
                    Condition = condition,
                    WhenClauses = whenClauses,
                    Returning = returning,
                    ReturningInto = returningInto,
                };
    }

    protected virtual SqlNode VisitCreateTable(CreateTableStatement node)
    {
        var name = Visit(node.Name);
        var elements = VisitList(node.Elements);
        var asQuery = VisitOptional(node.AsQuery);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(elements, node.Elements)
            && ReferenceEquals(asQuery, node.AsQuery)
                ? node
                : node with { Name = name, Elements = elements, AsQuery = asQuery };
    }

    protected virtual SqlNode VisitAlterTable(AlterTableStatement node)
    {
        var name = Visit(node.Name);
        var actions = VisitList(node.Actions);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(actions, node.Actions)
            ? node
            : node with { Name = name, Actions = actions };
    }

    protected virtual SqlNode VisitDrop(DropStatement node) =>
        Update(node, VisitList(node.Names), node.Names, static (n, names) => n with { Names = names });

    protected virtual SqlNode VisitTruncate(TruncateStatement node) =>
        Update(node, VisitList(node.Tables), node.Tables, static (n, tables) => n with { Tables = tables });

    protected virtual SqlNode VisitCreateView(CreateViewStatement node)
    {
        var name = Visit(node.Name);
        var query = Visit(node.Query);
        var columns = VisitOptionalList(node.Columns);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(query, node.Query)
            && ReferenceEquals(columns, node.Columns)
                ? node
                : node with { Name = name, Query = query, Columns = columns };
    }

    protected virtual SqlNode VisitCreateIndex(CreateIndexStatement node)
    {
        var name = Visit(node.Name);
        var table = Visit(node.Table);
        var columns = VisitList(node.Columns);
        var where = VisitOptional(node.Where);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(table, node.Table)
            && ReferenceEquals(columns, node.Columns)
            && ReferenceEquals(where, node.Where)
                ? node
                : node with { Name = name, Table = table, Columns = columns, Where = where };
    }

    protected virtual SqlNode VisitCreateSequence(CreateSequenceStatement node)
    {
        var name = Visit(node.Name);
        var options = Visit(node.Options);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(options, node.Options)
            ? node
            : node with { Name = name, Options = options };
    }

    protected virtual SqlNode VisitAlterSequence(AlterSequenceStatement node)
    {
        var name = Visit(node.Name);
        var options = Visit(node.Options);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(options, node.Options)
            ? node
            : node with { Name = name, Options = options };
    }

    protected virtual SqlNode VisitBooleanTest(BooleanTestExpression node) =>
        Update(node, Visit(node.Expression), node.Expression, static (n, value) => n with { Expression = value });

    protected virtual SqlNode VisitDistinctFrom(DistinctFromExpression node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);
        return ReferenceEquals(left, node.Left) && ReferenceEquals(right, node.Right)
            ? node
            : node with { Left = left, Right = right };
    }

    protected virtual SqlNode VisitRow(RowExpression node) =>
        Update(node, VisitList(node.Values), node.Values, static (n, values) => n with { Values = values });

    protected virtual SqlNode VisitDefault(DefaultExpression node) => node;

    protected virtual SqlNode VisitCollate(CollateExpression node)
    {
        var expression = Visit(node.Expression);
        var collation = Visit(node.Collation);
        return ReferenceEquals(expression, node.Expression) && ReferenceEquals(collation, node.Collation)
            ? node
            : node with { Expression = expression, Collation = collation };
    }

    protected virtual SqlNode VisitExtract(ExtractExpression node)
    {
        var field = Visit(node.Field);
        var expression = Visit(node.Expression);
        return ReferenceEquals(field, node.Field) && ReferenceEquals(expression, node.Expression)
            ? node
            : node with { Field = field, Expression = expression };
    }

    protected virtual SqlNode VisitInterval(IntervalExpression node)
    {
        var value = Visit(node.Value);
        var unit = Visit(node.Unit);
        return ReferenceEquals(value, node.Value) && ReferenceEquals(unit, node.Unit)
            ? node
            : node with { Value = value, Unit = unit };
    }

    protected virtual SqlNode VisitSequenceValue(SequenceValueExpression node) =>
        Update(node, Visit(node.Sequence), node.Sequence, static (n, value) => n with { Sequence = value });

    protected virtual SqlNode VisitTryCast(TryCastExpression node)
    {
        var expression = Visit(node.Expression);
        var dataType = Visit(node.DataType);
        return ReferenceEquals(expression, node.Expression) && ReferenceEquals(dataType, node.DataType)
            ? node
            : node with { Expression = expression, DataType = dataType };
    }

    protected virtual SqlNode VisitWindowDefinition(WindowDefinition node)
    {
        var name = Visit(node.Name);
        var baseWindow = VisitOptional(node.BaseWindow);
        var partitionBy = VisitOptionalList(node.PartitionBy);
        var orderBy = VisitOptionalList(node.OrderBy);
        var frame = VisitOptional(node.Frame);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(baseWindow, node.BaseWindow)
            && ReferenceEquals(partitionBy, node.PartitionBy)
            && ReferenceEquals(orderBy, node.OrderBy)
            && ReferenceEquals(frame, node.Frame)
                ? node
                : node with
                {
                    Name = name,
                    BaseWindow = baseWindow,
                    PartitionBy = partitionBy,
                    OrderBy = orderBy,
                    Frame = frame,
                };
    }

    protected virtual SqlNode VisitWindowFrame(WindowFrame node)
    {
        var start = Visit(node.Start);
        var end = VisitOptional(node.End);
        return ReferenceEquals(start, node.Start) && ReferenceEquals(end, node.End)
            ? node
            : node with { Start = start, End = end };
    }

    protected virtual SqlNode VisitWindowFrameBound(WindowFrameBound node)
    {
        var offset = VisitOptional(node.Offset);
        return ReferenceEquals(offset, node.Offset) ? node : node with { Offset = offset };
    }

    protected virtual SqlNode VisitConnectBy(ConnectByClause node)
    {
        var condition = Visit(node.Condition);
        var startWith = VisitOptional(node.StartWith);
        return ReferenceEquals(condition, node.Condition) && ReferenceEquals(startWith, node.StartWith)
            ? node
            : node with { Condition = condition, StartWith = startWith };
    }

    protected virtual SqlNode VisitMergeWhen(MergeWhenClause node)
    {
        var action = Visit(node.Action);
        var condition = VisitOptional(node.Condition);
        return ReferenceEquals(action, node.Action) && ReferenceEquals(condition, node.Condition)
            ? node
            : node with { Action = action, Condition = condition };
    }

    protected virtual SqlNode VisitMergeUpdate(MergeUpdateAction node)
    {
        var assignments = VisitList(node.Assignments);
        var deleteWhere = VisitOptional(node.DeleteWhere);
        return ReferenceEquals(assignments, node.Assignments) && ReferenceEquals(deleteWhere, node.DeleteWhere)
            ? node
            : node with { Assignments = assignments, DeleteWhere = deleteWhere };
    }

    protected virtual SqlNode VisitMergeInsert(MergeInsertAction node)
    {
        var columns = VisitOptionalList(node.Columns);
        var values = VisitList(node.Values);
        return ReferenceEquals(columns, node.Columns) && ReferenceEquals(values, node.Values)
            ? node
            : node with { Columns = columns, Values = values };
    }

    protected virtual SqlNode VisitMergeDelete(MergeDeleteAction node) => node;

    protected virtual SqlNode VisitColumnDefinition(ColumnDefinition node)
    {
        var name = Visit(node.Name);
        var dataType = Visit(node.DataType);
        var defaultValue = VisitOptional(node.Default);
        var generated = VisitOptional(node.GeneratedExpression);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(dataType, node.DataType)
            && ReferenceEquals(defaultValue, node.Default)
            && ReferenceEquals(generated, node.GeneratedExpression)
                ? node
                : node with
                {
                    Name = name,
                    DataType = dataType,
                    Default = defaultValue,
                    GeneratedExpression = generated,
                };
    }

    protected virtual SqlNode VisitIndexTableElement(IndexTableElement node)
    {
        var name = VisitOptional(node.Name);
        var columns = VisitList(node.Columns);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(columns, node.Columns)
                ? node
                : node with { Name = name, Columns = columns };
    }

    protected virtual SqlNode VisitPrimaryKeyConstraint(PrimaryKeyConstraint node)
    {
        var columns = VisitList(node.Columns);
        var name = VisitOptional(node.Name);
        return ReferenceEquals(columns, node.Columns) && ReferenceEquals(name, node.Name)
            ? node
            : node with { Columns = columns, Name = name };
    }

    protected virtual SqlNode VisitUniqueConstraint(UniqueConstraint node)
    {
        var columns = VisitList(node.Columns);
        var name = VisitOptional(node.Name);
        return ReferenceEquals(columns, node.Columns) && ReferenceEquals(name, node.Name)
            ? node
            : node with { Columns = columns, Name = name };
    }

    protected virtual SqlNode VisitForeignKeyConstraint(ForeignKeyConstraint node)
    {
        var columns = VisitList(node.Columns);
        var table = Visit(node.ReferencedTable);
        var referencedColumns = VisitList(node.ReferencedColumns);
        var name = VisitOptional(node.Name);
        return ReferenceEquals(columns, node.Columns)
            && ReferenceEquals(table, node.ReferencedTable)
            && ReferenceEquals(referencedColumns, node.ReferencedColumns)
            && ReferenceEquals(name, node.Name)
                ? node
                : node with
                {
                    Columns = columns,
                    ReferencedTable = table,
                    ReferencedColumns = referencedColumns,
                    Name = name,
                };
    }

    protected virtual SqlNode VisitCheckConstraint(CheckConstraint node)
    {
        var condition = Visit(node.Condition);
        var name = VisitOptional(node.Name);
        return ReferenceEquals(condition, node.Condition) && ReferenceEquals(name, node.Name)
            ? node
            : node with { Condition = condition, Name = name };
    }

    protected virtual SqlNode VisitAddColumn(AddColumnAction node) =>
        Update(node, Visit(node.Column), node.Column, static (n, value) => n with { Column = value });

    protected virtual SqlNode VisitDropColumn(DropColumnAction node) =>
        Update(node, Visit(node.Column), node.Column, static (n, value) => n with { Column = value });

    protected virtual SqlNode VisitAlterColumn(AlterColumnAction node)
    {
        var column = Visit(node.Column);
        var dataType = VisitOptional(node.DataType);
        var defaultValue = VisitOptional(node.Default);
        return ReferenceEquals(column, node.Column)
            && ReferenceEquals(dataType, node.DataType)
            && ReferenceEquals(defaultValue, node.Default)
                ? node
                : node with { Column = column, DataType = dataType, Default = defaultValue };
    }

    protected virtual SqlNode VisitAddConstraint(AddConstraintAction node) =>
        Update(node, Visit(node.Constraint), node.Constraint, static (n, value) => n with { Constraint = value });

    protected virtual SqlNode VisitDropConstraint(DropConstraintAction node) =>
        Update(node, Visit(node.Constraint), node.Constraint, static (n, value) => n with { Constraint = value });

    protected virtual SqlNode VisitRenameColumn(RenameColumnAction node)
    {
        var column = Visit(node.Column);
        var newName = Visit(node.NewName);
        return ReferenceEquals(column, node.Column) && ReferenceEquals(newName, node.NewName)
            ? node
            : node with { Column = column, NewName = newName };
    }

    protected virtual SqlNode VisitRenameTable(RenameTableAction node) =>
        Update(node, Visit(node.NewName), node.NewName, static (n, value) => n with { NewName = value });

    protected virtual SqlNode VisitIndexColumn(IndexColumn node) =>
        Update(node, Visit(node.Expression), node.Expression, static (n, value) => n with { Expression = value });

    protected virtual SqlNode VisitSequenceOptions(SequenceOptions node)
    {
        var start = VisitOptional(node.StartWith);
        var increment = VisitOptional(node.IncrementBy);
        var minimum = VisitOptional(node.MinimumValue);
        var maximum = VisitOptional(node.MaximumValue);
        var cache = VisitOptional(node.Cache);
        return ReferenceEquals(start, node.StartWith)
            && ReferenceEquals(increment, node.IncrementBy)
            && ReferenceEquals(minimum, node.MinimumValue)
            && ReferenceEquals(maximum, node.MaximumValue)
            && ReferenceEquals(cache, node.Cache)
                ? node
                : node with
                {
                    StartWith = start,
                    IncrementBy = increment,
                    MinimumValue = minimum,
                    MaximumValue = maximum,
                    Cache = cache,
                };
    }
}
