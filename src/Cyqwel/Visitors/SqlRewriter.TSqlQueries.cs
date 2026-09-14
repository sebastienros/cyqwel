using Cyqwel.Ast;

namespace Cyqwel.Visitors;

public abstract partial class SqlRewriter
{
    protected virtual SqlNode VisitTSqlTableSample(TSqlTableSample node) =>
        Update(node, Visit(node.Amount), node.Amount, static (sample, amount) => sample with { Amount = amount });

    protected virtual SqlNode VisitTSqlTableHint(TSqlTableHint node) =>
        UpdateOptional(node, VisitOptionalList(node.Indexes), node.Indexes, static (hint, indexes) => hint with { Indexes = indexes });

    protected virtual SqlNode VisitTSqlIndexReference(TSqlIndexReference node)
    {
        var name = VisitOptional(node.Name);
        return ReferenceEquals(name, node.Name) ? node : node with { Name = name };
    }

    protected virtual SqlNode VisitDerivedMutationTable(DerivedMutationTable node)
    {
        var statement = Visit(node.Statement);
        var alias = Visit(node.Alias);
        var columns = VisitOptionalList(node.Columns);
        return ReferenceEquals(statement, node.Statement) && ReferenceEquals(alias, node.Alias)
            && ReferenceEquals(columns, node.Columns)
            ? node : node with { Statement = statement, Alias = alias, Columns = columns };
    }

    protected virtual SqlNode VisitParenthesizedTable(ParenthesizedTable node)
    {
        var source = Visit(node.Source);
        var alias = VisitOptional(node.Alias);
        return ReferenceEquals(source, node.Source) && ReferenceEquals(alias, node.Alias)
            ? node : node with { Source = source, Alias = alias };
    }

    protected virtual SqlNode VisitTableFunction(TableFunction node)
    {
        var function = Visit(node.Function);
        var alias = VisitOptional(node.Alias);
        var columns = VisitOptionalList(node.Columns);
        return ReferenceEquals(function, node.Function) && ReferenceEquals(alias, node.Alias)
            && ReferenceEquals(columns, node.Columns)
            ? node : node with { Function = function, Alias = alias, Columns = columns };
    }

    protected virtual SqlNode VisitOpenJsonTable(OpenJsonTable node)
    {
        var expression = Visit(node.Expression);
        var path = VisitOptional(node.Path);
        var schema = VisitOptionalList(node.Schema);
        var alias = VisitOptional(node.Alias);
        return ReferenceEquals(expression, node.Expression) && ReferenceEquals(path, node.Path)
            && ReferenceEquals(schema, node.Schema) && ReferenceEquals(alias, node.Alias)
            ? node : node with { Expression = expression, Path = path, Schema = schema, Alias = alias };
    }

    protected virtual SqlNode VisitOpenJsonColumn(OpenJsonColumn node)
    {
        var name = Visit(node.Name);
        var type = Visit(node.DataType);
        var path = VisitOptional(node.Path);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(type, node.DataType) && ReferenceEquals(path, node.Path)
            ? node : node with { Name = name, DataType = type, Path = path };
    }

    protected virtual SqlNode VisitPivotTable(PivotTable node)
    {
        var source = Visit(node.Source);
        var aggregate = Visit(node.Aggregate);
        var column = Visit(node.Column);
        var values = VisitList(node.Values);
        var alias = VisitOptional(node.Alias);
        return ReferenceEquals(source, node.Source) && ReferenceEquals(aggregate, node.Aggregate)
            && ReferenceEquals(column, node.Column) && ReferenceEquals(values, node.Values) && ReferenceEquals(alias, node.Alias)
            ? node : node with { Source = source, Aggregate = aggregate, Column = column, Values = values, Alias = alias };
    }

    protected virtual SqlNode VisitUnpivotTable(UnpivotTable node)
    {
        var source = Visit(node.Source);
        var valueColumn = Visit(node.ValueColumn);
        var nameColumn = Visit(node.NameColumn);
        var columns = VisitList(node.Columns);
        var alias = VisitOptional(node.Alias);
        return ReferenceEquals(source, node.Source) && ReferenceEquals(valueColumn, node.ValueColumn)
            && ReferenceEquals(nameColumn, node.NameColumn) && ReferenceEquals(columns, node.Columns) && ReferenceEquals(alias, node.Alias)
            ? node : node with { Source = source, ValueColumn = valueColumn, NameColumn = nameColumn, Columns = columns, Alias = alias };
    }

    protected virtual SqlNode VisitTSqlResultFormat(TSqlResultFormat node)
    {
        var element = VisitOptional(node.ElementName);
        var root = VisitOptional(node.Root);
        return ReferenceEquals(element, node.ElementName) && ReferenceEquals(root, node.Root)
            ? node : node with { ElementName = element, Root = root };
    }

    protected virtual SqlNode VisitTSqlQueryOption(TSqlQueryOption node) => node;
}
