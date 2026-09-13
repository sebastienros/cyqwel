namespace Cyqwel.Ast;

public sealed record SqlIdentifier(string Value, bool IsQuoted = false) : SqlNode
{
    public bool IsOmitted { get; init; }

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
        return name.Split('.').Select(static part => new SqlIdentifier(part) { IsOmitted = part.Length == 0 }).ToArray();
    }
}

public sealed record StarExpression(IReadOnlyList<SqlIdentifier>? Qualifier = null) : SqlExpression;

public sealed record LiteralExpression(object? Value) : SqlExpression
{
    public bool IsNational { get; init; }
}

public enum CurrentTimestampKind
{
    Default,
    SystemDate,
    Utc,
}

/// <summary>
/// The target dialect's current timestamp. SystemDate preserves Oracle SYSDATE on Oracle.
/// Utc returns UTC date/time fields without a timezone offset; native types and clock precision vary by dialect.
/// </summary>
public sealed record CurrentTimestampExpression(
    CurrentTimestampKind Kind = CurrentTimestampKind.Default) : SqlExpression;

public enum TrimDirection
{
    Leading,
    Trailing,
    Both,
}

public sealed record TrimExpression(
    TrimDirection Direction,
    SqlExpression? Character,
    SqlExpression Source) : SqlExpression;

public sealed record TypedLiteralExpression(
    SqlIdentifier TypeName,
    SqlExpression Value) : SqlExpression;

public sealed record HexLiteralExpression(string Value) : SqlExpression;

public sealed record ParameterExpression(
    string Name,
    char Prefix = '@',
    SqlExpression? DefaultValue = null) : SqlExpression
{
    public bool IsSystemVariable { get; init; }
}

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
    AnsiConcatenate,
}

public sealed record BinaryExpression(
    SqlExpression Left,
    BinaryOperator Operator,
    SqlExpression Right) : SqlExpression;

public enum SqlQuantifier
{
    Any,
    All,
    Some,
}

public sealed record QuantifiedComparisonExpression(
    SqlExpression Left,
    BinaryOperator Operator,
    SqlQuantifier Quantifier,
    SqlQuery Query) : SqlExpression
{
    internal static bool IsValidOperator(BinaryOperator value) => value is
        BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.GreaterThan
        or BinaryOperator.GreaterThanOrEqual or BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual;
}

public sealed record ConvertExpression(
    SqlExpression Expression,
    SqlDataType DataType,
    SqlExpression? Style = null,
    bool IsTry = false) : SqlExpression;

public enum JsonNullHandling
{
    NullOnNull,
    AbsentOnNull,
}

public sealed record JsonArrayAggregateExpression(
    SqlExpression Expression,
    IReadOnlyList<OrderByItem>? OrderBy = null,
    JsonNullHandling? NullHandling = null) : SqlExpression;

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
    public IReadOnlyList<SqlIdentifier>? Qualifiers { get; init; }

    public FunctionCallExpression(string name, params SqlExpression[] arguments)
        : this(new TableName(name).Parts, arguments)
    {
    }

    private FunctionCallExpression(IReadOnlyList<SqlIdentifier> name, IReadOnlyList<SqlExpression> arguments)
        : this(name[^1], arguments)
    {
        Qualifiers = name.Count > 1 ? name.Take(name.Count - 1).ToArray() : null;
    }

    public FunctionCallExpression Distinct(bool value = true) => this with { IsDistinct = value };

    public FunctionCallExpression FilterWhere(SqlExpression predicate) =>
        this with
        {
            Filter = predicate ?? throw new ArgumentNullException(nameof(predicate)),
        };

    public FunctionCallExpression WithinGroupBy(params OrderByItem[] orderBy)
    {
        ArgumentNullException.ThrowIfNull(orderBy);
        if (orderBy.Length == 0)
        {
            throw new ArgumentException("At least one ordering expression is required.", nameof(orderBy));
        }

        return this with { WithinGroup = orderBy };
    }

    public WindowExpression Over(
        IReadOnlyList<SqlExpression>? partitionBy = null,
        IReadOnlyList<OrderByItem>? orderBy = null,
        WindowFrame? frame = null) =>
        new(this, partitionBy, orderBy, frame);

    public WindowExpression Over(string windowName) =>
        new(this, WindowName: new SqlIdentifier(windowName));

    public WindowExpression Over(
        string windowName,
        IReadOnlyList<SqlExpression>? partitionBy,
        IReadOnlyList<OrderByItem>? orderBy = null,
        WindowFrame? frame = null) =>
        new(this, partitionBy, orderBy, frame, new SqlIdentifier(windowName));
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
    public bool IsMaxLength { get; init; }

    public SqlDataType(string name, params int[] arguments)
        : this(new SqlIdentifier(name), arguments.Length == 0 ? null : arguments)
    {
    }
}
