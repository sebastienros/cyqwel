using Cyqwel.Ast;

namespace Cyqwel.Visitors;

public abstract partial class SqlVisitor
{
    protected virtual void VisitCreateProcedure(CreateProcedureStatement node) => DefaultVisit(node);
    protected virtual void VisitReplaceProcedure(ReplaceProcedureStatement node) => DefaultVisit(node);
    protected virtual void VisitDropProcedure(DropProcedureStatement node) => DefaultVisit(node);
    protected virtual void VisitCallProcedure(CallProcedureStatement node) => DefaultVisit(node);
    protected virtual void VisitProceduralIf(ProceduralIfStatement node) => DefaultVisit(node);
    protected virtual void VisitProceduralWhile(ProceduralWhileStatement node) => DefaultVisit(node);
    protected virtual void VisitProceduralBreak(ProceduralBreakStatement node) => DefaultVisit(node);
    protected virtual void VisitProceduralContinue(ProceduralContinueStatement node) => DefaultVisit(node);
    protected virtual void VisitProceduralReturn(ProceduralReturnStatement node) => DefaultVisit(node);
    protected virtual void VisitProcedureParameter(ProcedureParameter node) => DefaultVisit(node);
    protected virtual void VisitLocalVariable(LocalVariable node) => DefaultVisit(node);
    protected virtual void VisitProceduralBlock(ProceduralBlock node) => DefaultVisit(node);
    protected virtual void VisitProcedureArgument(ProcedureArgument node) => DefaultVisit(node);
    protected virtual void VisitLocalVariableExpression(LocalVariableExpression node) => DefaultVisit(node);
}
