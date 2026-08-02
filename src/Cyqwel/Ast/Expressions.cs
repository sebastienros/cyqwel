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

public sealed record ParameterExpression(
    string Name,
    char Prefix = '@',
    SqlExpression? DefaultValue = null) : SqlExpression;

public sealed record ParenthesizedExpression(SqlExpression Expression) : SqlExpression;

public enum UnaryOperator
{
    Plus,
    Minus,
    Not,
    BitwiseNot,
    Prior,
    ConnectByRoot,
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

public enum BooleanTestKind
{
    True,
    False,
    Unknown,
}

public sealed record BooleanTestExpression(
    SqlExpression Expression,
    BooleanTestKind Kind,
    bool IsNegated = false) : SqlExpression;

public sealed record DistinctFromExpression(
    SqlExpression Left,
    SqlExpression Right,
    bool IsNegated = false) : SqlExpression;

public sealed record RowExpression(IReadOnlyList<SqlExpression> Values) : SqlExpression;

public sealed record DefaultExpression : SqlExpression;

public sealed record CollateExpression(
    SqlExpression Expression,
    SqlIdentifier Collation) : SqlExpression;

public sealed record ExtractExpression(
    SqlIdentifier Field,
    SqlExpression Expression) : SqlExpression;

public sealed record IntervalExpression(
    SqlExpression Value,
    SqlIdentifier Unit) : SqlExpression;

public enum SequenceValueKind
{
    Next,
    Current,
}

public sealed record SequenceValueExpression(
    TableName Sequence,
    SequenceValueKind Kind) : SqlExpression;

public sealed record FunctionCallExpression(
    SqlIdentifier Name,
    IReadOnlyList<SqlExpression> Arguments,
    bool IsDistinct = false,
    SqlExpression? Filter = null,
    IReadOnlyList<OrderByItem>? WithinGroup = null) : SqlExpression
{
    public FunctionCallExpression(string name, params SqlExpression[] arguments)
        : this(new SqlIdentifier(name), arguments)
    {
    }
}

public sealed record WindowExpression(
    SqlExpression Expression,
    IReadOnlyList<SqlExpression>? PartitionBy = null,
    IReadOnlyList<OrderByItem>? OrderBy = null,
    WindowFrame? Frame = null,
    SqlIdentifier? WindowName = null) : SqlExpression;

public sealed record ExistsExpression(SqlQuery Query, bool IsNegated = false) : SqlExpression;

public sealed record SubqueryExpression(SqlQuery Query) : SqlExpression;

public sealed record WhenClause(SqlExpression Condition, SqlExpression Result) : SqlNode;

public sealed record CaseExpression(
    SqlExpression? Operand,
    IReadOnlyList<WhenClause> Whens,
    SqlExpression? Else = null) : SqlExpression;

public sealed record CastExpression(SqlExpression Expression, SqlDataType DataType) : SqlExpression;

public sealed record TryCastExpression(SqlExpression Expression, SqlDataType DataType) : SqlExpression;

public enum SqlDataTypeLengthUnit
{
    Unspecified,
    Byte,
    Char,
}

public enum SqlDataTypeTimeZone
{
    Unspecified,
    WithTimeZone,
    WithLocalTimeZone,
}

public sealed record SqlDataType(
    SqlIdentifier Name,
    IReadOnlyList<int>? Arguments = null,
    SqlDataTypeLengthUnit LengthUnit = SqlDataTypeLengthUnit.Unspecified,
    SqlDataTypeTimeZone TimeZone = SqlDataTypeTimeZone.Unspecified,
    SqlIdentifier? IntervalEndField = null,
    IReadOnlyList<int>? IntervalEndArguments = null) : SqlNode
{
    public SqlDataType(string name, params int[] arguments)
        : this(new SqlIdentifier(name), arguments.Length == 0 ? null : arguments)
    {
    }
}
