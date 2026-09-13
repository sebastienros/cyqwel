namespace Cyqwel.Ast;

public sealed record TableName(IReadOnlyList<SqlIdentifier> Parts) : SqlNode
{
    public bool IsVariable { get; init; }

    public TableName(string name)
        : this(ParseParts(name))
    {
    }

    private static IReadOnlyList<SqlIdentifier> ParseParts(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Split('.').Select(static part => new SqlIdentifier(part) { IsOmitted = part.Length == 0 }).ToArray();
    }
}

public abstract record TableSource : SqlNode;

public sealed record NamedTable(TableName Name, SqlIdentifier? Alias = null) : TableSource
{
    public IReadOnlyList<TSqlTableHint>? Hints { get; init; }
    public TSqlTableSample? Sample { get; init; }

    public NamedTable(string name, string? alias = null)
        : this(new TableName(name), alias is null ? null : new SqlIdentifier(alias))
    {
    }
}

public sealed record DerivedTable(SqlQuery Query, SqlIdentifier Alias) : TableSource
{
    public IReadOnlyList<SqlIdentifier>? Columns { get; init; }
}

public enum JoinKind
{
    Inner,
    Left,
    Right,
    Full,
    Cross,
    OuterApply,
    CrossApply,
}

public enum JoinSyntax
{
    Explicit,
    Comma,
}

public sealed record JoinTable(
    TableSource Left,
    TableSource Right,
    JoinKind Kind,
    SqlExpression? Condition = null,
    JoinSyntax Syntax = JoinSyntax.Explicit,
    IReadOnlyList<SqlIdentifier>? Using = null,
    bool IsNatural = false) : TableSource
{
    public TSqlJoinHint? Hint { get; init; }
}

public sealed record SelectItem(SqlExpression Expression, SqlIdentifier? Alias = null) : SqlNode
{
    public SqlIdentifier? AssignmentTarget { get; init; }
    public SqlAssignmentOperator AssignmentOperator { get; init; }

    public SelectItem(SqlExpression expression, string? alias)
        : this(expression, alias is null ? null : new SqlIdentifier(alias))
    {
    }
}

public enum OrderDirection
{
    Ascending,
    Descending,
    Unspecified,
}

public enum NullOrder
{
    Unspecified,
    First,
    Last,
}

public sealed record OrderByItem(
    SqlExpression Expression,
    OrderDirection Direction = OrderDirection.Unspecified,
    NullOrder NullOrder = NullOrder.Unspecified) : SqlNode;

public sealed record CommonTableExpression(
    SqlIdentifier Name,
    SqlQuery Query,
    IReadOnlyList<SqlIdentifier>? Columns = null,
    CteMaterialization Materialization = CteMaterialization.Unspecified) : SqlNode;

public enum SqlAssignmentOperator
{
    Assign,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    Concatenate,
}

public sealed record Assignment(ColumnExpression Column, SqlExpression Value) : SqlNode
{
    public SqlAssignmentOperator Operator { get; init; }
}

public enum CteMaterialization
{
    Unspecified,
    Materialized,
    NotMaterialized,
}

public sealed record WindowDefinition(
    SqlIdentifier Name,
    SqlIdentifier? BaseWindow = null,
    IReadOnlyList<SqlExpression>? PartitionBy = null,
    IReadOnlyList<OrderByItem>? OrderBy = null,
    WindowFrame? Frame = null) : SqlNode;

public enum WindowFrameUnit
{
    Rows,
    Range,
    Groups,
}

public enum WindowFrameBoundKind
{
    UnboundedPreceding,
    Preceding,
    CurrentRow,
    Following,
    UnboundedFollowing,
}

public sealed record WindowFrameBound(
    WindowFrameBoundKind Kind,
    SqlExpression? Offset = null) : SqlNode;

public sealed record WindowFrame(
    WindowFrameUnit Unit,
    WindowFrameBound Start,
    WindowFrameBound? End = null) : SqlNode;

public sealed record ConnectByClause(
    SqlExpression Condition,
    SqlExpression? StartWith = null,
    bool NoCycle = false) : SqlNode;

public enum MergeMatchKind
{
    Matched,
    NotMatched,
    NotMatchedBySource,
}

public abstract record MergeAction : SqlNode;

public sealed record MergeUpdateAction(
    IReadOnlyList<Assignment> Assignments,
    SqlExpression? DeleteWhere = null) : MergeAction;

public sealed record MergeInsertAction(
    IReadOnlyList<SqlIdentifier>? Columns,
    IReadOnlyList<SqlExpression> Values) : MergeAction;

public sealed record MergeDeleteAction : MergeAction;

public sealed record MergeWhenClause(
    MergeMatchKind MatchKind,
    MergeAction Action,
    SqlExpression? Condition = null) : SqlNode;
