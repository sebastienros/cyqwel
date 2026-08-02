namespace Cyqwel.Ast;

public sealed record TableName(IReadOnlyList<SqlIdentifier> Parts) : SqlNode
{
    public TableName(string name)
        : this(ParseParts(name))
    {
    }

    private static IReadOnlyList<SqlIdentifier> ParseParts(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Split('.').Select(static part => new SqlIdentifier(part)).ToArray();
    }
}

public abstract record TableSource : SqlNode;

public sealed record NamedTable(TableName Name, SqlIdentifier? Alias = null) : TableSource
{
    public NamedTable(string name, string? alias = null)
        : this(new TableName(name), alias is null ? null : new SqlIdentifier(alias))
    {
    }
}

public sealed record DerivedTable(SqlQuery Query, SqlIdentifier Alias) : TableSource;

public enum JoinKind
{
    Inner,
    Left,
    Right,
    Full,
    Cross,
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
    JoinSyntax Syntax = JoinSyntax.Explicit) : TableSource;

public sealed record SelectItem(SqlExpression Expression, SqlIdentifier? Alias = null) : SqlNode
{
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
    IReadOnlyList<SqlIdentifier>? Columns = null) : SqlNode;

public sealed record Assignment(ColumnExpression Column, SqlExpression Value) : SqlNode;
