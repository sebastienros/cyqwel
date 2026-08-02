using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;

namespace Cyqwel;

public sealed class SelectBuilder
{
    private SelectStatement _statement;

    internal SelectBuilder(IReadOnlyList<SelectItem> projections)
    {
        ArgumentNullException.ThrowIfNull(projections);
        if (projections.Count == 0) throw new ArgumentException("At least one projection is required.", nameof(projections));
        _statement = new SelectStatement(projections);
    }

    public SelectBuilder Distinct(bool value = true)
    {
        _statement = _statement with { IsDistinct = value };
        return this;
    }

    public SelectBuilder From(string table, string? alias = null)
    {
        _statement = _statement with { From = new NamedTable(table, alias) };
        return this;
    }

    public SelectBuilder From(SqlQuery query, string alias)
    {
        _statement = _statement with { From = new DerivedTable(query, new SqlIdentifier(alias)) };
        return this;
    }

    public SelectBuilder Join(string table, SqlExpression condition, string? alias = null) =>
        AddJoin(JoinKind.Inner, new NamedTable(table, alias), condition);

    public SelectBuilder LeftJoin(string table, SqlExpression condition, string? alias = null) =>
        AddJoin(JoinKind.Left, new NamedTable(table, alias), condition);

    public SelectBuilder RightJoin(string table, SqlExpression condition, string? alias = null) =>
        AddJoin(JoinKind.Right, new NamedTable(table, alias), condition);

    public SelectBuilder FullJoin(string table, SqlExpression condition, string? alias = null) =>
        AddJoin(JoinKind.Full, new NamedTable(table, alias), condition);

    public SelectBuilder CrossJoin(string table, string? alias = null) =>
        AddJoin(JoinKind.Cross, new NamedTable(table, alias), null);

    public SelectBuilder Where(SqlExpression predicate)
    {
        _statement = _statement with { Where = predicate ?? throw new ArgumentNullException(nameof(predicate)) };
        return this;
    }

    public SelectBuilder AndWhere(SqlExpression predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _statement = _statement with
        {
            Where = _statement.Where is null
                ? predicate
                : new BinaryExpression(_statement.Where, BinaryOperator.And, predicate),
        };
        return this;
    }

    public SelectBuilder GroupBy(params SqlExpression[] expressions)
    {
        _statement = _statement with { GroupBy = expressions };
        return this;
    }

    public SelectBuilder Having(SqlExpression predicate)
    {
        _statement = _statement with { Having = predicate ?? throw new ArgumentNullException(nameof(predicate)) };
        return this;
    }

    public SelectBuilder OrderBy(
        SqlExpression expression,
        OrderDirection direction = OrderDirection.Ascending,
        NullOrder nullOrder = NullOrder.Unspecified)
    {
        _statement = _statement with { OrderBy = [new OrderByItem(expression, direction, nullOrder)] };
        return this;
    }

    public SelectBuilder ThenBy(
        SqlExpression expression,
        OrderDirection direction = OrderDirection.Ascending,
        NullOrder nullOrder = NullOrder.Unspecified)
    {
        var items = _statement.OrderBy?.ToList() ?? [];
        items.Add(new OrderByItem(expression, direction, nullOrder));
        _statement = _statement with { OrderBy = items };
        return this;
    }

    public SelectBuilder Limit(long value) => Limit(Sql.Lit(value));

    public SelectBuilder Limit(SqlExpression value)
    {
        _statement = _statement with { Limit = value ?? throw new ArgumentNullException(nameof(value)) };
        return this;
    }

    public SelectBuilder Offset(long value) => Offset(Sql.Lit(value));

    public SelectBuilder Offset(SqlExpression value)
    {
        _statement = _statement with { Offset = value ?? throw new ArgumentNullException(nameof(value)) };
        return this;
    }

    public SelectBuilder Top(long value) => Top(Sql.Lit(value));

    public SelectBuilder Top(SqlExpression value)
    {
        _statement = _statement with { Top = value ?? throw new ArgumentNullException(nameof(value)) };
        return this;
    }

    public SelectBuilder With(string name, SqlQuery query, params string[] columns)
    {
        var expressions = _statement.CommonTableExpressions?.ToList() ?? [];
        expressions.Add(new CommonTableExpression(
            new SqlIdentifier(name),
            query,
            columns.Length == 0
                ? null
                : columns.Select(static column => new SqlIdentifier(column)).ToArray()));
        _statement = _statement with { CommonTableExpressions = expressions };
        return this;
    }

    public SetQueryBuilder Union(SelectBuilder right, bool all = false) =>
        new(new SetOperationStatement(Build(), SetOperator.Union, right.Build(), all));

    public SetQueryBuilder Intersect(SelectBuilder right, bool all = false) =>
        new(new SetOperationStatement(Build(), SetOperator.Intersect, right.Build(), all));

    public SetQueryBuilder Except(SelectBuilder right, bool all = false) =>
        new(new SetOperationStatement(Build(), SetOperator.Except, right.Build(), all));

    public SelectStatement Build() => _statement;

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);

    private SelectBuilder AddJoin(JoinKind kind, TableSource right, SqlExpression? condition)
    {
        if (_statement.From is null)
        {
            throw new InvalidOperationException("FROM must be specified before adding a join.");
        }

        _statement = _statement with
        {
            From = new JoinTable(_statement.From, right, kind, condition),
        };
        return this;
    }
}

public sealed class SetQueryBuilder
{
    private SetOperationStatement _statement;

    internal SetQueryBuilder(SetOperationStatement statement) => _statement = statement;

    public SetQueryBuilder Union(SelectBuilder right, bool all = false)
    {
        _statement = new SetOperationStatement(_statement, SetOperator.Union, right.Build(), all);
        return this;
    }

    public SetQueryBuilder OrderBy(
        SqlExpression expression,
        OrderDirection direction = OrderDirection.Ascending)
    {
        _statement = _statement with { OrderBy = [new OrderByItem(expression, direction)] };
        return this;
    }

    public SetQueryBuilder Limit(long value)
    {
        _statement = _statement with { Limit = Sql.Lit(value) };
        return this;
    }

    public SetQueryBuilder Offset(long value)
    {
        _statement = _statement with { Offset = Sql.Lit(value) };
        return this;
    }

    public SetOperationStatement Build() => _statement;

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}
