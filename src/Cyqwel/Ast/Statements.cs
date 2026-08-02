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
    bool WithTies = false) : SqlQuery;

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
    SqlExpression? Offset = null) : SqlQuery;

public sealed record InsertStatement(
    TableName Target,
    IReadOnlyList<SqlIdentifier>? Columns,
    IReadOnlyList<IReadOnlyList<SqlExpression>>? Values = null,
    SqlQuery? Source = null,
    IReadOnlyList<SqlExpression>? Returning = null) : SqlStatement;

public sealed record UpdateStatement(
    NamedTable Target,
    IReadOnlyList<Assignment> Assignments,
    SqlExpression? Where = null,
    IReadOnlyList<SqlExpression>? Returning = null) : SqlStatement;

public sealed record DeleteStatement(
    NamedTable Target,
    SqlExpression? Where = null,
    IReadOnlyList<SqlExpression>? Returning = null) : SqlStatement;
