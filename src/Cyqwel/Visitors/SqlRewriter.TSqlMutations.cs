using Cyqwel.Ast;

namespace Cyqwel.Visitors;

public abstract partial class SqlRewriter
{
    protected virtual SqlNode VisitAddTableElement(AddTableElementAction node) =>
        Update(node, Visit(node.Element), node.Element, static (n, element) => n with { Element = element });

    protected virtual SqlNode VisitTSqlOutput(TSqlOutputClause node)
    {
        var items = VisitList(node.Items);
        var into = VisitOptional(node.Into);
        var columns = VisitOptionalList(node.Columns);
        return ReferenceEquals(items, node.Items) && ReferenceEquals(into, node.Into) && ReferenceEquals(columns, node.Columns)
            ? node : node with { Items = items, Into = into, Columns = columns };
    }

    protected virtual SqlNode VisitMergeActionExpression(MergeActionExpression node) => node;

    protected virtual SqlNode VisitCreateSchema(CreateSchemaStatement node) =>
        Update(node, Visit(node.Name), node.Name, static (n, name) => n with { Name = name });

    protected virtual SqlNode VisitCreateInlineFunction(CreateInlineFunctionStatement node)
    {
        var name = Visit(node.Name);
        var parameters = VisitList(node.Parameters);
        var query = Visit(node.Query);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(parameters, node.Parameters) && ReferenceEquals(query, node.Query)
            ? node : node with { Name = name, Parameters = parameters, Query = query };
    }

    protected virtual SqlNode VisitComputedColumnDefinition(ComputedColumnDefinition node)
    {
        var name = Visit(node.Name);
        var expression = Visit(node.Expression);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(expression, node.Expression)
            ? node : node with { Name = name, Expression = expression };
    }

    protected virtual SqlNode VisitDefaultConstraint(DefaultConstraint node)
    {
        var name = VisitOptional(node.Name);
        var value = Visit(node.Value);
        var column = Visit(node.Column);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(value, node.Value) && ReferenceEquals(column, node.Column)
            ? node : node with { Name = name, Value = value, Column = column };
    }
}
