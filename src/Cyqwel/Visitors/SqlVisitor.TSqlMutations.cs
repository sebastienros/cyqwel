using Cyqwel.Ast;

namespace Cyqwel.Visitors;

public abstract partial class SqlVisitor
{
    protected virtual void VisitAddTableElement(AddTableElementAction node) => DefaultVisit(node);
    protected virtual void VisitTSqlOutput(TSqlOutputClause node) => DefaultVisit(node);
    protected virtual void VisitMergeActionExpression(MergeActionExpression node) => DefaultVisit(node);
    protected virtual void VisitCreateInlineFunction(CreateInlineFunctionStatement node) => DefaultVisit(node);
    protected virtual void VisitCreateSchema(CreateSchemaStatement node) => DefaultVisit(node);
    protected virtual void VisitComputedColumnDefinition(ComputedColumnDefinition node) => DefaultVisit(node);
    protected virtual void VisitDefaultConstraint(DefaultConstraint node) => DefaultVisit(node);
}
