using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;

namespace Cyqwel;

public sealed class ValuesBuilder
{
    private ValuesStatement _statement;

    internal ValuesBuilder(IReadOnlyList<SqlExpression> row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Count == 0) throw new ArgumentException("At least one value is required.", nameof(row));
        _statement = new ValuesStatement([row]);
    }

    public ValuesBuilder Row(params object?[] values)
    {
        if (values.Length == 0) throw new ArgumentException("At least one value is required.", nameof(values));
        if (values.Length != _statement.Rows[0].Count)
        {
            throw new ArgumentException(
                "Every VALUES row must contain the same number of values.",
                nameof(values));
        }

        var rows = _statement.Rows.ToList();
        rows.Add(values.Select(Sql.Coerce).ToArray());
        _statement = _statement with { Rows = rows };
        return this;
    }

    public ValuesBuilder OrderBy(
        SqlExpression expression,
        OrderDirection direction = OrderDirection.Ascending,
        NullOrder nullOrder = NullOrder.Unspecified)
    {
        _statement = _statement with { OrderBy = [new OrderByItem(expression, direction, nullOrder)] };
        return this;
    }

    public ValuesBuilder ThenBy(
        SqlExpression expression,
        OrderDirection direction = OrderDirection.Ascending,
        NullOrder nullOrder = NullOrder.Unspecified)
    {
        var items = _statement.OrderBy?.ToList() ?? [];
        items.Add(new OrderByItem(expression, direction, nullOrder));
        _statement = _statement with { OrderBy = items };
        return this;
    }

    public ValuesBuilder Limit(long value) => Limit(Sql.Lit(value));

    public ValuesBuilder Limit(SqlExpression value)
    {
        _statement = _statement with
        {
            Limit = value ?? throw new ArgumentNullException(nameof(value)),
        };
        return this;
    }

    public ValuesBuilder Offset(long value) => Offset(Sql.Lit(value));

    public ValuesBuilder Offset(SqlExpression value)
    {
        _statement = _statement with
        {
            Offset = value ?? throw new ArgumentNullException(nameof(value)),
        };
        return this;
    }

    public ValuesBuilder With(string name, SqlQuery query, params string[] columns)
        => With(name, query, CteMaterialization.Unspecified, columns);

    public ValuesBuilder With(
        string name,
        SqlQuery query,
        CteMaterialization materialization,
        params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(query);
        var expressions = _statement.CommonTableExpressions?.ToList() ?? [];
        expressions.Add(new CommonTableExpression(
            new SqlIdentifier(name),
            query,
            columns.Length == 0
                ? null
                : columns.Select(static column => new SqlIdentifier(column)).ToArray(),
            materialization));
        _statement = _statement with { CommonTableExpressions = expressions };
        return this;
    }

    public ValuesBuilder Recursive(bool value = true)
    {
        _statement = _statement with { IsRecursive = value };
        return this;
    }

    public SetQueryBuilder Union(SqlQuery right, bool all = false) =>
        new(new SetOperationStatement(Build(), SetOperator.Union, right, all));

    public SetQueryBuilder Union(SelectBuilder right, bool all = false) =>
        Union(right?.Build() ?? throw new ArgumentNullException(nameof(right)), all);

    public SetQueryBuilder Intersect(SqlQuery right, bool all = false) =>
        new(new SetOperationStatement(Build(), SetOperator.Intersect, right, all));

    public SetQueryBuilder Intersect(SelectBuilder right, bool all = false) =>
        Intersect(right?.Build() ?? throw new ArgumentNullException(nameof(right)), all);

    public SetQueryBuilder Except(SqlQuery right, bool all = false) =>
        new(new SetOperationStatement(Build(), SetOperator.Except, right, all));

    public SetQueryBuilder Except(SelectBuilder right, bool all = false) =>
        Except(right?.Build() ?? throw new ArgumentNullException(nameof(right)), all);

    public ExplainBuilder Explain(bool analyze = false) => new(Build(), analyze);

    public ValuesStatement Build() => _statement;

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class ExplainBuilder
{
    private ExplainStatement _statement;

    internal ExplainBuilder(SqlQuery query, bool analyze = false)
    {
        _statement = new ExplainStatement(
            query ?? throw new ArgumentNullException(nameof(query)),
            analyze);
    }

    public ExplainBuilder Analyze(bool value = true)
    {
        _statement = _statement with { Analyze = value };
        return this;
    }

    public ExplainBuilder Parenthesized(bool value = true)
    {
        _statement = _statement with { IsQueryParenthesized = value };
        return this;
    }

    public ExplainStatement Build() => _statement;

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}
