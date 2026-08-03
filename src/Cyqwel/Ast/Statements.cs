namespace Cyqwel.Ast;

public sealed record SelectStatement(
    IReadOnlyList<SelectItem> Projections,
    TableSource? From = null,
    SqlExpression? Where = null,
    IReadOnlyList<SqlExpression>? GroupBy = null,
    SqlExpression? Having = null,
    IReadOnlyList<OrderByItem>? OrderBy = null,
    SqlExpression? Limit = null,
    SqlExpression? Offset = null,
    bool IsDistinct = false,
    IReadOnlyList<CommonTableExpression>? CommonTableExpressions = null,
    SqlExpression? Top = null,
    bool IsTopPercent = false,
    bool WithTies = false,
    bool IsRecursive = false,
    IReadOnlyList<WindowDefinition>? Windows = null,
    SqlExpression? Qualify = null,
    ConnectByClause? ConnectBy = null,
    bool OrderSiblings = false) : SqlQuery;

public sealed record ValuesStatement(
    IReadOnlyList<IReadOnlyList<SqlExpression>> Rows,
    IReadOnlyList<OrderByItem>? OrderBy = null,
    SqlExpression? Limit = null,
    SqlExpression? Offset = null,
    bool IsRecursive = false,
    IReadOnlyList<CommonTableExpression>? CommonTableExpressions = null) : SqlQuery;

public enum SetOperator
{
    Union,
    Intersect,
    Except,
}

public sealed record SetOperationStatement(
    SqlQuery Left,
    SetOperator Operator,
    SqlQuery Right,
    bool IsAll = false,
    IReadOnlyList<OrderByItem>? OrderBy = null,
    SqlExpression? Limit = null,
    SqlExpression? Offset = null,
    bool IsRecursive = false,
    IReadOnlyList<CommonTableExpression>? CommonTableExpressions = null) : SqlQuery;

public sealed record ExplainStatement(
    SqlQuery Query,
    bool Analyze = false,
    bool IsQueryParenthesized = false) : SqlStatement;

public sealed record InsertStatement(
    TableName Target,
    IReadOnlyList<SqlIdentifier>? Columns,
    IReadOnlyList<IReadOnlyList<SqlExpression>>? Values = null,
    SqlQuery? Source = null,
    IReadOnlyList<SqlExpression>? Returning = null,
    IReadOnlyList<SqlExpression>? ReturningInto = null) : SqlStatement;

public sealed record UpdateStatement(
    NamedTable Target,
    IReadOnlyList<Assignment> Assignments,
    SqlExpression? Where = null,
    IReadOnlyList<SqlExpression>? Returning = null,
    IReadOnlyList<SqlExpression>? ReturningInto = null,
    TableSource? From = null) : SqlStatement;

public sealed record DeleteStatement(
    NamedTable Target,
    SqlExpression? Where = null,
    IReadOnlyList<SqlExpression>? Returning = null,
    IReadOnlyList<SqlExpression>? ReturningInto = null,
    TableSource? Using = null) : SqlStatement;

public sealed record MergeStatement(
    NamedTable Target,
    TableSource Source,
    SqlExpression Condition,
    IReadOnlyList<MergeWhenClause> WhenClauses,
    IReadOnlyList<SqlExpression>? Returning = null,
    IReadOnlyList<SqlExpression>? ReturningInto = null) : SqlStatement;

public sealed record GrantStatement(
    IReadOnlyList<SqlIdentifier> Objects,
    IReadOnlyList<SqlIdentifier> Grantees) : SqlStatement;

public sealed record SetStatement(
    IReadOnlyList<SqlIdentifier> Keywords,
    IReadOnlyList<SqlNode> Arguments) : SqlStatement;
