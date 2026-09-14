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

public sealed record ProceduralReturnStatement(SqlExpression? Value = null) : SqlStatement;

public sealed record PrintStatement(SqlExpression Value) : SqlStatement;

public sealed record ExecuteSqlStatement(SqlExpression Command) : SqlStatement;

public sealed record DeclareStatement(IReadOnlyList<LocalVariable> Variables) : SqlStatement;

public sealed record TableVariableDeclarationStatement(
    SqlIdentifier Name,
    IReadOnlyList<TableElement> Elements) : SqlStatement;

public sealed record SetVariableStatement(
    SqlIdentifier Name,
    SqlExpression Value,
    SqlAssignmentOperator Operator = SqlAssignmentOperator.Assign) : SqlStatement;

public enum TransactionKind
{
    Begin,
    Commit,
    Rollback,
}

public sealed record TransactionStatement(
    TransactionKind Kind,
    SqlExpression? Name = null,
    bool IsWork = false,
    bool HasMark = false,
    SqlExpression? Mark = null) : SqlStatement
{
    public bool? DelayedDurability { get; init; }

    internal bool HasValidModifiers =>
        (Kind == TransactionKind.Begin || !HasMark && Mark is null)
        && (Kind != TransactionKind.Begin || !IsWork)
        && (!IsWork || Name is null)
        && (Mark is null || HasMark)
        && (DelayedDurability is null || Kind == TransactionKind.Commit);
}

public enum ApplicationSetOption
{
    NoCount,
    XactAbort,
}

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
    IReadOnlyList<ProcedureArgument> Arguments) : SqlStatement
{
    public SqlIdentifier? ReturnVariable { get; init; }
}

internal static class ProceduralDollarQuotes
{
    public const string Tag = "$cyqwel$";
}

internal static class DynamicSqlCommands
{
    public static bool IsSupported(SqlExpression expression) => expression switch
    {
        LiteralExpression { Value: string } or ParameterExpression or LocalVariableExpression => true,
        BinaryExpression { Operator: BinaryOperator.Add } addition =>
            IsSupported(addition.Left) && IsSupported(addition.Right),
        _ => false,
    };
}
