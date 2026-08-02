using Cyqwel.Dialects;
using Cyqwel.Generation;
using Cyqwel.Visitors;

namespace Cyqwel.Ast;

/// <summary>
/// Represents a location in the parsed SQL source.
/// </summary>
public readonly record struct SqlTextSpan(int Start, int Length)
{
    public static SqlTextSpan None => new(-1, 0);

    public bool IsEmpty => Start < 0;
}

/// <summary>
/// Base type for every dialect-neutral SQL syntax node.
/// </summary>
public abstract record SqlNode
{
    public SqlTextSpan Span { get; init; } = SqlTextSpan.None;

    public void Accept(SqlVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.Visit(this);
    }

    public SqlNode Accept(SqlRewriter rewriter)
    {
        ArgumentNullException.ThrowIfNull(rewriter);
        return rewriter.Visit(this);
    }

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        (dialect ?? SqlDialects.Generic).Generate(this, options);
}

public abstract record SqlStatement : SqlNode;

public abstract record SqlQuery : SqlStatement;

public abstract record SqlExpression : SqlNode
{
    public BinaryExpression EqualTo(SqlExpression right) => Binary(BinaryOperator.Equal, right);
    public BinaryExpression NotEqualTo(SqlExpression right) => Binary(BinaryOperator.NotEqual, right);
    public BinaryExpression GreaterThan(SqlExpression right) => Binary(BinaryOperator.GreaterThan, right);
    public BinaryExpression GreaterThanOrEqualTo(SqlExpression right) => Binary(BinaryOperator.GreaterThanOrEqual, right);
    public BinaryExpression LessThan(SqlExpression right) => Binary(BinaryOperator.LessThan, right);
    public BinaryExpression LessThanOrEqualTo(SqlExpression right) => Binary(BinaryOperator.LessThanOrEqual, right);
    public BinaryExpression And(SqlExpression right) => Binary(BinaryOperator.And, right);
    public BinaryExpression Or(SqlExpression right) => Binary(BinaryOperator.Or, right);
    public BinaryExpression Add(SqlExpression right) => Binary(BinaryOperator.Add, right);
    public BinaryExpression Subtract(SqlExpression right) => Binary(BinaryOperator.Subtract, right);
    public BinaryExpression Multiply(SqlExpression right) => Binary(BinaryOperator.Multiply, right);
    public BinaryExpression Divide(SqlExpression right) => Binary(BinaryOperator.Divide, right);
    public BinaryExpression Modulo(SqlExpression right) => Binary(BinaryOperator.Modulo, right);
    public BinaryExpression Concatenate(SqlExpression right) => Binary(BinaryOperator.Concatenate, right);
    public BinaryExpression BitwiseAnd(SqlExpression right) => Binary(BinaryOperator.BitwiseAnd, right);
    public BinaryExpression BitwiseOr(SqlExpression right) => Binary(BinaryOperator.BitwiseOr, right);
    public BinaryExpression BitwiseXor(SqlExpression right) => Binary(BinaryOperator.BitwiseXor, right);
    public BinaryExpression Like(SqlExpression pattern) => Binary(BinaryOperator.Like, pattern);
    public BinaryExpression NotLike(SqlExpression pattern) => Binary(BinaryOperator.NotLike, pattern);
    public BinaryExpression ILike(SqlExpression pattern) => Binary(BinaryOperator.ILike, pattern);
    public BinaryExpression NotILike(SqlExpression pattern) => Binary(BinaryOperator.NotILike, pattern);
    public UnaryExpression Positive() => new(UnaryOperator.Plus, this);
    public UnaryExpression Negate() => new(UnaryOperator.Minus, this);
    public UnaryExpression Not() => new(UnaryOperator.Not, this);
    public UnaryExpression BitwiseNot() => new(UnaryOperator.BitwiseNot, this);
    public UnaryExpression Prior() => new(UnaryOperator.Prior, this);
    public UnaryExpression ConnectByRoot() => new(UnaryOperator.ConnectByRoot, this);
    public IsNullExpression IsNull() => new(this, false);
    public IsNullExpression IsNotNull() => new(this, true);
    public BooleanTestExpression IsTrue() => new(this, BooleanTestKind.True);
    public BooleanTestExpression IsNotTrue() => new(this, BooleanTestKind.True, true);
    public BooleanTestExpression IsFalse() => new(this, BooleanTestKind.False);
    public BooleanTestExpression IsNotFalse() => new(this, BooleanTestKind.False, true);
    public BooleanTestExpression IsUnknown() => new(this, BooleanTestKind.Unknown);
    public BooleanTestExpression IsNotUnknown() => new(this, BooleanTestKind.Unknown, true);
    public DistinctFromExpression IsDistinctFrom(SqlExpression right) => new(this, right);
    public DistinctFromExpression IsNotDistinctFrom(SqlExpression right) => new(this, right, true);
    public BetweenExpression Between(SqlExpression lower, SqlExpression upper) => new(this, lower, upper, false);
    public BetweenExpression NotBetween(SqlExpression lower, SqlExpression upper) => new(this, lower, upper, true);
    public InExpression In(params SqlExpression[] values) =>
        InValues(values, isNegated: false);
    public InExpression NotIn(params SqlExpression[] values) =>
        InValues(values, isNegated: true);
    public InExpression In(SqlQuery query) =>
        new(this, query ?? throw new ArgumentNullException(nameof(query)));
    public InExpression NotIn(SqlQuery query) =>
        new(this, query ?? throw new ArgumentNullException(nameof(query)), true);
    public CollateExpression Collate(string collation) => new(this, new SqlIdentifier(collation));

    private BinaryExpression Binary(BinaryOperator @operator, SqlExpression right)
    {
        ArgumentNullException.ThrowIfNull(right);
        return new(this, @operator, right);
    }

    private InExpression InValues(SqlExpression[] values, bool isNegated)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("At least one IN value is required.", nameof(values));
        }

        return new InExpression(this, values, null, isNegated);
    }
}

public sealed record SqlDocument(IReadOnlyList<SqlStatement> Statements) : SqlNode
{
    public SqlDocument(params SqlStatement[] statements) : this((IReadOnlyList<SqlStatement>)statements)
    {
    }
}
