using Cyqwel.Ast;
using Cyqwel.Visitors;

namespace Cyqwel.Generation;

public sealed partial class SqlGenerator
{
    private RoutineRenderer Routines => _dialect.RoutineRenderer;

    private bool ShouldOmitStatement(SqlStatement statement) =>
        ShouldOmitProcedure(statement)
        || ShouldOmitProceduralBlock(statement)
        || ShouldOmitTopLevelProceduralStatement(statement);

    private bool ShouldOmitTopLevelProceduralStatement(SqlStatement statement) =>
        _options.UnsupportedBehavior == UnsupportedSqlBehavior.Ignore
        && (statement is ProceduralBreakStatement or ProceduralContinueStatement
            || !Routines.SupportsTopLevelControlFlow
            && statement is (ProceduralIfStatement
                or ProceduralWhileStatement
                or ProceduralReturnStatement));

    private bool ShouldOmitProcedure(SqlStatement statement)
    {
        if (_options.UnsupportedBehavior != UnsupportedSqlBehavior.Ignore) return false;
        if (statement is not (CreateProcedureStatement or ReplaceProcedureStatement
            or DropProcedureStatement or CallProcedureStatement)) return false;
        if (!_dialect.SupportsStoredProcedures) return true;
        return statement switch
        {
            ReplaceProcedureStatement when !Routines.SupportsReplaceProcedure => true,
            CreateProcedureStatement create when !CanRepresent(create.Parameters) => true,
            CreateProcedureStatement create when !CanRepresent(create.Body) => true,
            ReplaceProcedureStatement replace when !CanRepresent(replace.Parameters) => true,
            ReplaceProcedureStatement replace when !CanRepresent(replace.Body) => true,
            CallProcedureStatement call when !CanRepresent(call.Arguments) => true,
            _ => false,
        };
    }

    private bool EnsureProcedureSupported(SqlStatement statement)
    {
        if (ShouldOmitProcedure(statement)) return false;
        if (!_dialect.SupportsStoredProcedures)
        {
            Unsupported($"{_dialect.Name} does not support stored procedures.");
            return _options.UnsupportedBehavior != UnsupportedSqlBehavior.Ignore;
        }

        return true;
    }

    private bool CanRepresent(IReadOnlyList<ProcedureParameter> parameters) =>
        Routines.SupportsParameterDefaults
        || parameters.All(static parameter => parameter.Default is null);

    private bool CanRepresent(IReadOnlyList<ProcedureArgument> arguments) =>
        Routines.SupportsNamedArguments
        || arguments.All(static argument => argument.Name is null);

    private bool CanRepresent(ProceduralBlock body) =>
        !Routines.UsesDollarQuotedBodies
        || !ProceduralBlockContains(body, ProceduralDollarQuotes.Tag);

    private bool ShouldOmitProceduralBlock(SqlStatement statement) =>
        _options.UnsupportedBehavior == UnsupportedSqlBehavior.Ignore
        && statement is ProceduralBlock block
        && (!_dialect.SupportsAnonymousProceduralBlocks || !CanRepresent(block));

    private bool EnsureProceduralBlockSupported(ProceduralBlock block)
    {
        if (ShouldOmitProceduralBlock(block)) return false;
        if (!_dialect.SupportsAnonymousProceduralBlocks)
        {
            Unsupported($"{_dialect.Name} does not support anonymous procedural blocks.");
            return _options.UnsupportedBehavior != UnsupportedSqlBehavior.Ignore;
        }

        if (!CanRepresent(block))
        {
            Unsupported($"The {_dialect.Name} procedural block conflicts with the reserved dollar-quote tag.");
            return false;
        }

        return true;
    }

    private void WriteProceduralBlock(ProceduralBlock block)
    {
        if (!EnsureProceduralBlockSupported(block)) return;
        _proceduralContextDepth++;
        try
        {
            Routines.WriteAnonymousBlock(this, block);
        }
        finally
        {
            _proceduralContextDepth--;
        }
    }

    private bool EnsureProceduralStatementContext(string statement)
    {
        if (_proceduralContextDepth > 0 || Routines.SupportsTopLevelControlFlow) return true;
        Unsupported($"{statement} requires a procedural block in the {_dialect.Name} dialect.");
        return _options.UnsupportedBehavior != UnsupportedSqlBehavior.Ignore;
    }

    private void WriteProcedureDefinition(
        TableName name,
        IReadOnlyList<ProcedureParameter> parameters,
        ProceduralBlock body,
        bool replace)
    {
        SqlStatement operation = replace
            ? new ReplaceProcedureStatement(name, parameters, body)
            : new CreateProcedureStatement(name, parameters, body);
        if (!EnsureProcedureSupported(operation)) return;
        if (replace && !Routines.SupportsReplaceProcedure)
        {
            Unsupported($"{_dialect.Name} cannot replace a stored procedure definition atomically.");
            return;
        }
        if (!CanRepresent(parameters))
        {
            Unsupported($"{_dialect.Name} cannot represent stored procedure parameter defaults.");
            return;
        }
        if (!CanRepresent(body))
        {
            Unsupported($"The {_dialect.Name} procedure body conflicts with the reserved dollar-quote tag.");
            return;
        }

        _proceduralContextDepth++;
        try
        {
            Routines.WriteProcedureDefinition(this, name, parameters, body, replace);
        }
        finally
        {
            _proceduralContextDepth--;
        }
    }

    private void WriteProcedureParameters(IReadOnlyList<ProcedureParameter> parameters)
    {
        _builder.Append('(');
        WriteSeparated(parameters, WriteProcedureParameter);
        _builder.Append(')');
    }

    private void WriteProcedureParameter(ProcedureParameter parameter) =>
        Routines.WriteParameter(this, parameter);

    private void WriteProcedureParameterDefault(ProcedureParameter parameter, string keyword)
    {
        if (parameter.Default is null) return;
        Space();
        Keyword(keyword);
        Space();
        WriteExpression(parameter.Default);
    }

    private void WriteProceduralBlockContents(ProceduralBlock body, bool declarationsInsideBody)
    {
        if (declarationsInsideBody) WriteLocalVariables(body.Variables);
        WriteProceduralStatements(body.Statements);
    }

    private void WriteLocalVariables(IReadOnlyList<LocalVariable> variables)
    {
        foreach (var variable in variables)
        {
            ProceduralLineBreak();
            Routines.WriteLocalVariable(this, variable);
            _builder.Append(';');
        }
    }

    private void WriteProceduralStatements(IReadOnlyList<SqlStatement> statements)
    {
        foreach (var statement in statements)
        {
            ProceduralLineBreak();
            WriteNode(statement);
            _builder.Append(';');
        }
    }

    private void WriteProceduralIf(ProceduralIfStatement statement)
    {
        if (!EnsureProceduralStatementContext("IF")) return;
        Routines.WriteIf(this, statement);
    }

    private void WriteProceduralWhile(ProceduralWhileStatement statement)
    {
        if (!EnsureProceduralStatementContext("WHILE")) return;
        var label = $"cyqwel_loop_{++_loopCounter}";
        _loopLabels.Push(label);
        try
        {
            Routines.WriteWhile(this, statement, label);
        }
        finally
        {
            _loopLabels.Pop();
        }
    }

    private void WriteProceduralLoopControl(bool isContinue)
    {
        if (_loopLabels.Count == 0)
        {
            throw new NotSupportedException(
                $"{(isContinue ? "CONTINUE" : "BREAK")} can only be generated inside a WHILE loop.");
        }
        Routines.WriteLoopControl(this, isContinue, _loopLabels.Peek());
    }

    private void WriteProceduralReturn()
    {
        if (!EnsureProceduralStatementContext("RETURN")) return;
        Routines.WriteReturn(this);
    }

    private void WriteDropProcedure(DropProcedureStatement drop)
    {
        if (!EnsureProcedureSupported(drop)) return;
        Routines.WriteDropProcedure(this, drop);
    }

    private void WriteCallProcedure(CallProcedureStatement call)
    {
        if (!EnsureProcedureSupported(call)) return;
        if (!CanRepresent(call.Arguments))
        {
            Unsupported($"{_dialect.Name} cannot represent named stored procedure arguments.");
            return;
        }
        Routines.WriteCallProcedure(this, call);
    }

    private void WriteProcedureArgument(ProcedureArgument argument) =>
        Routines.WriteArgument(this, argument);

    private void WriteLocalVariableReference(LocalVariableExpression variable) =>
        Routines.WriteLocalReference(this, variable);

    private void ProceduralLineBreak()
    {
        if (_options.PrettyPrint) NewLine();
        else Space();
    }

    private static bool ProceduralBlockContains(ProceduralBlock body, string value) =>
        body.FindAll<SqlIdentifier>()
            .Select(static identifier => identifier.Value)
            .Concat(body.FindAll<LiteralExpression>()
                .Select(static literal => literal.Value?.ToString() ?? string.Empty))
            .Any(content => content.Contains(value, StringComparison.Ordinal));
}
