namespace Cyqwel.Ast;

public enum ProcedureParameterMode
{
    In,
    Out,
    InOut,
}

public sealed record ProcedureParameter(
    SqlIdentifier Name,
    SqlDataType DataType,
    ProcedureParameterMode Mode = ProcedureParameterMode.In,
    SqlExpression? Default = null) : SqlNode;

public sealed record LocalVariable(
    SqlIdentifier Name,
    SqlDataType DataType,
    SqlExpression? Initializer = null) : SqlNode;

public sealed record ProceduralBlock(
    IReadOnlyList<LocalVariable> Variables,
    IReadOnlyList<SqlStatement> Statements) : SqlStatement;

public sealed record LocalVariableExpression(SqlIdentifier Name) : SqlExpression
{
    public LocalVariableExpression(string name) : this(new SqlIdentifier(name))
    {
    }
}

public sealed record ProceduralIfStatement(
    SqlExpression Condition,
    IReadOnlyList<SqlStatement> Then,
    IReadOnlyList<SqlStatement>? Else = null) : SqlStatement;

public sealed record ProceduralWhileStatement(
    SqlExpression Condition,
    IReadOnlyList<SqlStatement> Statements) : SqlStatement
{
    internal SqlIdentifier? SourceLabel { get; init; }
    internal SqlIdentifier? SourceEndLabel { get; init; }
}

public sealed record ProceduralBreakStatement : SqlStatement
{
    internal SqlIdentifier? SourceTargetLabel { get; init; }
}

public sealed record ProceduralContinueStatement : SqlStatement
{
    internal SqlIdentifier? SourceTargetLabel { get; init; }
}

public sealed record ProceduralReturnStatement : SqlStatement;

public sealed record ProcedureArgument(
    SqlExpression Value,
    SqlIdentifier? Name = null,
    bool IsOutput = false) : SqlNode;

public sealed record CreateProcedureStatement(
    TableName Name,
    IReadOnlyList<ProcedureParameter> Parameters,
    ProceduralBlock Body) : SqlStatement;

public sealed record ReplaceProcedureStatement(
    TableName Name,
    IReadOnlyList<ProcedureParameter> Parameters,
    ProceduralBlock Body) : SqlStatement;

public sealed record DropProcedureStatement(
    TableName Name,
    IReadOnlyList<SqlDataType>? ParameterTypes = null,
    bool IfExists = false) : SqlStatement;

public sealed record CallProcedureStatement(
    TableName Name,
    IReadOnlyList<ProcedureArgument> Arguments) : SqlStatement;

internal static class ProceduralDollarQuotes
{
    public const string Tag = "$cyqwel$";
}
