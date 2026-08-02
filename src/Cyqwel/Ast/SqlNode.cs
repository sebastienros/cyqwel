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
    public BinaryExpression Like(SqlExpression pattern) => Binary(BinaryOperator.Like, pattern);
    public BinaryExpression ILike(SqlExpression pattern) => Binary(BinaryOperator.ILike, pattern);
    public UnaryExpression Not() => new(UnaryOperator.Not, this);
    public IsNullExpression IsNull() => new(this, false);
    public IsNullExpression IsNotNull() => new(this, true);
    public BetweenExpression Between(SqlExpression lower, SqlExpression upper) => new(this, lower, upper, false);
    public BetweenExpression NotBetween(SqlExpression lower, SqlExpression upper) => new(this, lower, upper, true);
    public InExpression In(params SqlExpression[] values) => new(this, values, null, false);
    public InExpression NotIn(params SqlExpression[] values) => new(this, values, null, true);

    private BinaryExpression Binary(BinaryOperator @operator, SqlExpression right)
    {
        ArgumentNullException.ThrowIfNull(right);
        return new(this, @operator, right);
    }
}

public sealed record SqlDocument(IReadOnlyList<SqlStatement> Statements) : SqlNode
{
    public SqlDocument(params SqlStatement[] statements) : this((IReadOnlyList<SqlStatement>)statements)
    {
    }
}
