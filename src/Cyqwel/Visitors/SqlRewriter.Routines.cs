using Cyqwel.Ast;

namespace Cyqwel.Visitors;

public abstract partial class SqlRewriter
{
    protected virtual SqlNode VisitBatch(SqlBatch node) =>
        Update(node, VisitList(node.Statements), node.Statements, static (n, value) => n with { Statements = value });

    protected virtual SqlNode VisitDeclare(DeclareStatement node) =>
        Update(node, VisitList(node.Variables), node.Variables, static (n, value) => n with { Variables = value });

    protected virtual SqlNode VisitPrint(PrintStatement node) =>
        Update(node, Visit(node.Value), node.Value, static (n, value) => n with { Value = value });

    protected virtual SqlNode VisitExecuteSql(ExecuteSqlStatement node) =>
        Update(node, Visit(node.Command), node.Command, static (n, value) => n with { Command = value });

    protected virtual SqlNode VisitTableVariableDeclaration(TableVariableDeclarationStatement node)
    {
        var name = Visit(node.Name);
        var elements = VisitList(node.Elements);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(elements, node.Elements)
            ? node : node with { Name = name, Elements = elements };
    }

    protected virtual SqlNode VisitSetVariable(SetVariableStatement node)
    {
        var name = Visit(node.Name);
        var value = Visit(node.Value);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(value, node.Value)
            ? node : node with { Name = name, Value = value };
    }

    protected virtual SqlNode VisitTransaction(TransactionStatement node)
    {
        var name = VisitOptional(node.Name);
        var mark = VisitOptional(node.Mark);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(mark, node.Mark)
            ? node : node with { Name = name, Mark = mark };
    }

    protected virtual SqlNode VisitCreateProcedure(CreateProcedureStatement node)
    {
        var name = Visit(node.Name);
        var parameters = VisitList(node.Parameters);
        var body = Visit(node.Body);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(parameters, node.Parameters)
            && ReferenceEquals(body, node.Body)
                ? node
                : node with { Name = name, Parameters = parameters, Body = body };
    }

    protected virtual SqlNode VisitReplaceProcedure(ReplaceProcedureStatement node)
    {
        var name = Visit(node.Name);
        var parameters = VisitList(node.Parameters);
        var body = Visit(node.Body);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(parameters, node.Parameters)
            && ReferenceEquals(body, node.Body)
                ? node
                : node with { Name = name, Parameters = parameters, Body = body };
    }

    protected virtual SqlNode VisitDropProcedure(DropProcedureStatement node)
    {
        var name = Visit(node.Name);
        var parameterTypes = VisitOptionalList(node.ParameterTypes);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(parameterTypes, node.ParameterTypes)
            ? node
            : node with { Name = name, ParameterTypes = parameterTypes };
    }

    protected virtual SqlNode VisitCallProcedure(CallProcedureStatement node)
    {
        var name = Visit(node.Name);
        var arguments = VisitList(node.Arguments);
        var returnVariable = VisitOptional(node.ReturnVariable);
        return ReferenceEquals(name, node.Name) && ReferenceEquals(arguments, node.Arguments)
            && ReferenceEquals(returnVariable, node.ReturnVariable)
            ? node
            : node with { Name = name, Arguments = arguments, ReturnVariable = returnVariable };
    }

    protected virtual SqlNode VisitProceduralIf(ProceduralIfStatement node)
    {
        var condition = Visit(node.Condition);
        var thenStatements = VisitList(node.Then);
        var elseStatements = VisitOptionalList(node.Else);
        return ReferenceEquals(condition, node.Condition)
            && ReferenceEquals(thenStatements, node.Then)
            && ReferenceEquals(elseStatements, node.Else)
                ? node
                : node with { Condition = condition, Then = thenStatements, Else = elseStatements };
    }

    protected virtual SqlNode VisitProceduralWhile(ProceduralWhileStatement node)
    {
        var condition = Visit(node.Condition);
        var statements = VisitList(node.Statements);
        return ReferenceEquals(condition, node.Condition)
            && ReferenceEquals(statements, node.Statements)
                ? node
                : node with { Condition = condition, Statements = statements };
    }

    protected virtual SqlNode VisitProceduralBreak(ProceduralBreakStatement node) => node;

    protected virtual SqlNode VisitProceduralContinue(ProceduralContinueStatement node) => node;

    protected virtual SqlNode VisitProceduralReturn(ProceduralReturnStatement node)
    {
        var value = VisitOptional(node.Value);
        return ReferenceEquals(value, node.Value) ? node : node with { Value = value };
    }

    protected virtual SqlNode VisitProcedureParameter(ProcedureParameter node)
    {
        var name = Visit(node.Name);
        var dataType = Visit(node.DataType);
        var defaultValue = VisitOptional(node.Default);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(dataType, node.DataType)
            && ReferenceEquals(defaultValue, node.Default)
                ? node
                : node with { Name = name, DataType = dataType, Default = defaultValue };
    }

    protected virtual SqlNode VisitLocalVariable(LocalVariable node)
    {
        var name = Visit(node.Name);
        var dataType = Visit(node.DataType);
        var initializer = VisitOptional(node.Initializer);
        return ReferenceEquals(name, node.Name)
            && ReferenceEquals(dataType, node.DataType)
            && ReferenceEquals(initializer, node.Initializer)
                ? node
                : node with { Name = name, DataType = dataType, Initializer = initializer };
    }

    protected virtual SqlNode VisitProceduralBlock(ProceduralBlock node)
    {
        var variables = VisitList(node.Variables);
        var statements = VisitList(node.Statements);
        return ReferenceEquals(variables, node.Variables) && ReferenceEquals(statements, node.Statements)
            ? node
            : node with { Variables = variables, Statements = statements };
    }

    protected virtual SqlNode VisitProcedureArgument(ProcedureArgument node)
    {
        var value = Visit(node.Value);
        var name = VisitOptional(node.Name);
        return ReferenceEquals(value, node.Value) && ReferenceEquals(name, node.Name)
            ? node
            : node with { Value = value, Name = name };
    }

    protected virtual SqlNode VisitLocalVariableExpression(LocalVariableExpression node)
    {
        var name = Visit(node.Name);
        return ReferenceEquals(name, node.Name) ? node : node with { Name = name };
    }
}
