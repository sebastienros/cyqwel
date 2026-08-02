namespace Cyqwel.Ast;

public sealed record SqlIdentifier(string Value, bool IsQuoted = false) : SqlNode
{
    public override string ToString() => Value;
}

public sealed record ColumnExpression(IReadOnlyList<SqlIdentifier> Parts) : SqlExpression
{
    public ColumnExpression(string name)
        : this(ParseParts(name))
    {
    }

    public ColumnExpression(string qualifier, string name)
        : this([new SqlIdentifier(qualifier), new SqlIdentifier(name)])
    {
    }

    private static IReadOnlyList<SqlIdentifier> ParseParts(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Split('.').Select(static part => new SqlIdentifier(part)).ToArray();
    }
}

public sealed record StarExpression(IReadOnlyList<SqlIdentifier>? Qualifier = null) : SqlExpression;

public sealed record LiteralExpression(object? Value) : SqlExpression;

public sealed record ParameterExpression(string Name, char Prefix = '@') : SqlExpression;

public enum UnaryOperator
{
    Plus,
    Minus,
    Not,
    BitwiseNot,
}

public sealed record UnaryExpression(UnaryOperator Operator, SqlExpression Operand) : SqlExpression;

public enum BinaryOperator
{
    Or,
    And,
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Like,
    NotLike,
    ILike,
    NotILike,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Concatenate,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
}

public sealed record BinaryExpression(
    SqlExpression Left,
    BinaryOperator Operator,
    SqlExpression Right) : SqlExpression;

public sealed record BetweenExpression(
    SqlExpression Expression,
    SqlExpression Lower,
    SqlExpression Upper,
    bool IsNegated = false) : SqlExpression;

public sealed record InExpression(
    SqlExpression Expression,
    IReadOnlyList<SqlExpression> Values,
    SqlQuery? Query = null,
    bool IsNegated = false) : SqlExpression
{
    public InExpression(SqlExpression expression, SqlQuery query, bool isNegated = false)
        : this(expression, Array.Empty<SqlExpression>(), query, isNegated)
    {
    }
}

public sealed record IsNullExpression(SqlExpression Expression, bool IsNegated = false) : SqlExpression;

public sealed record FunctionCallExpression(
    SqlIdentifier Name,
    IReadOnlyList<SqlExpression> Arguments,
    bool IsDistinct = false) : SqlExpression
{
    public FunctionCallExpression(string name, params SqlExpression[] arguments)
        : this(new SqlIdentifier(name), arguments)
    {
    }
}

public sealed record ExistsExpression(SqlQuery Query, bool IsNegated = false) : SqlExpression;

public sealed record SubqueryExpression(SqlQuery Query) : SqlExpression;

public sealed record WhenClause(SqlExpression Condition, SqlExpression Result) : SqlNode;

public sealed record CaseExpression(
    SqlExpression? Operand,
    IReadOnlyList<WhenClause> Whens,
    SqlExpression? Else = null) : SqlExpression;

public sealed record CastExpression(SqlExpression Expression, SqlDataType DataType) : SqlExpression;

public sealed record SqlDataType(
    SqlIdentifier Name,
    IReadOnlyList<int>? Arguments = null) : SqlNode
{
    public SqlDataType(string name, params int[] arguments)
        : this(new SqlIdentifier(name), arguments.Length == 0 ? null : arguments)
    {
    }
}
