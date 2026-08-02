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

    public SelectBuilder From(TableSource source)
    {
        _statement = _statement with { From = source ?? throw new ArgumentNullException(nameof(source)) };
        return this;
    }

    public SelectBuilder From(SqlQuery query, string alias)
    {
        ArgumentNullException.ThrowIfNull(query);
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

    public SelectBuilder Join(
        TableSource source,
        SqlExpression condition,
        JoinKind kind = JoinKind.Inner) =>
        AddJoin(kind, source, condition);

    public SelectBuilder Join(
        SqlQuery query,
        string alias,
        SqlExpression condition,
        JoinKind kind = JoinKind.Inner) =>
        AddJoin(
            kind,
            new DerivedTable(
                query ?? throw new ArgumentNullException(nameof(query)),
                new SqlIdentifier(alias)),
            condition);

    public SelectBuilder JoinUsing(
        string table,
        IReadOnlyList<string> columns,
        string? alias = null,
        JoinKind kind = JoinKind.Inner) =>
        JoinUsing(new NamedTable(table, alias), columns, kind);

    public SelectBuilder JoinUsing(
        TableSource source,
        IReadOnlyList<string> columns,
        JoinKind kind = JoinKind.Inner)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
        {
            throw new ArgumentException("At least one USING column is required.", nameof(columns));
        }

        return AddJoin(
            kind,
            source,
            condition: null,
            usingColumns: columns.Select(static column => new SqlIdentifier(column)).ToArray());
    }

    public SelectBuilder NaturalJoin(
        string table,
        string? alias = null,
        JoinKind kind = JoinKind.Inner) =>
        NaturalJoin(new NamedTable(table, alias), kind);

    public SelectBuilder NaturalJoin(TableSource source, JoinKind kind = JoinKind.Inner) =>
        AddJoin(kind, source, condition: null, isNatural: true);

    public SelectBuilder CommaJoin(string table, string? alias = null) =>
        CommaJoin(new NamedTable(table, alias));

    public SelectBuilder CommaJoin(TableSource source) =>
        AddJoin(JoinKind.Cross, source, condition: null, syntax: JoinSyntax.Comma);

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
        if (_statement.Top is not null && (_statement.IsTopPercent || _statement.WithTies))
        {
            throw new InvalidOperationException("LIMIT cannot be combined with TOP PERCENT or WITH TIES.");
        }

        _statement = _statement with
        {
            Limit = value ?? throw new ArgumentNullException(nameof(value)),
        };
        return this;
    }

    public SelectBuilder Offset(long value) => Offset(Sql.Lit(value));

    public SelectBuilder Offset(SqlExpression value)
    {
        if (_statement.Top is not null && (_statement.IsTopPercent || _statement.WithTies))
        {
            throw new InvalidOperationException("OFFSET cannot be combined with TOP PERCENT or WITH TIES.");
        }

        _statement = _statement with { Offset = value ?? throw new ArgumentNullException(nameof(value)) };
        return this;
    }

    public SelectBuilder Top(long value) => Top(Sql.Lit(value));

    public SelectBuilder Top(SqlExpression value)
        => Top(value, percent: false, withTies: false);

    public SelectBuilder Top(long value, bool percent, bool withTies = false) =>
        Top(Sql.Lit(value), percent, withTies);

    public SelectBuilder Top(SqlExpression value, bool percent, bool withTies = false)
    {
        if ((percent || withTies) && (_statement.Limit is not null || _statement.Offset is not null))
        {
            throw new InvalidOperationException(
                "TOP PERCENT or WITH TIES cannot be combined with LIMIT or OFFSET.");
        }

        _statement = _statement with
        {
            Top = value ?? throw new ArgumentNullException(nameof(value)),
            IsTopPercent = percent,
            WithTies = withTies,
        };
        return this;
    }

    public SelectBuilder With(string name, SqlQuery query, params string[] columns)
        => With(name, query, CteMaterialization.Unspecified, columns);

    public SelectBuilder With(
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

    public SelectBuilder Recursive(bool value = true)
    {
        _statement = _statement with { IsRecursive = value };
        return this;
    }

    public SelectBuilder Window(WindowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var windows = _statement.Windows?.ToList() ?? [];
        windows.Add(definition);
        _statement = _statement with { Windows = windows };
        return this;
    }

    public SelectBuilder Window(
        string name,
        string? baseWindow = null,
        IReadOnlyList<SqlExpression>? partitionBy = null,
        IReadOnlyList<OrderByItem>? orderBy = null,
        WindowFrame? frame = null) =>
        Window(new WindowDefinition(
            new SqlIdentifier(name),
            baseWindow is null ? null : new SqlIdentifier(baseWindow),
            partitionBy,
            orderBy,
            frame));

    public SelectBuilder Qualify(SqlExpression predicate)
    {
        _statement = _statement with
        {
            Qualify = predicate ?? throw new ArgumentNullException(nameof(predicate)),
        };
        return this;
    }

    public SelectBuilder ConnectBy(
        SqlExpression condition,
        SqlExpression? startWith = null,
        bool noCycle = false)
    {
        _statement = _statement with
        {
            ConnectBy = new ConnectByClause(
                condition ?? throw new ArgumentNullException(nameof(condition)),
                startWith,
                noCycle),
        };
        return this;
    }

    public SelectBuilder OrderSiblingsBy(
        SqlExpression expression,
        OrderDirection direction = OrderDirection.Ascending,
        NullOrder nullOrder = NullOrder.Unspecified)
    {
        _statement = _statement with
        {
            OrderBy = [new OrderByItem(expression, direction, nullOrder)],
            OrderSiblings = true,
        };
        return this;
    }

    public SetQueryBuilder Union(SelectBuilder right, bool all = false) =>
        Union(right?.Build() ?? throw new ArgumentNullException(nameof(right)), all);

    public SetQueryBuilder Union(SqlQuery right, bool all = false) =>
        new(new SetOperationStatement(Build(), SetOperator.Union, right, all));

    public SetQueryBuilder Intersect(SelectBuilder right, bool all = false) =>
        Intersect(right?.Build() ?? throw new ArgumentNullException(nameof(right)), all);

    public SetQueryBuilder Intersect(SqlQuery right, bool all = false) =>
        new(new SetOperationStatement(Build(), SetOperator.Intersect, right, all));

    public SetQueryBuilder Except(SelectBuilder right, bool all = false) =>
        Except(right?.Build() ?? throw new ArgumentNullException(nameof(right)), all);

    public SetQueryBuilder Except(SqlQuery right, bool all = false) =>
        new(new SetOperationStatement(Build(), SetOperator.Except, right, all));

    public ExplainBuilder Explain(bool analyze = false) => new(Build(), analyze);

    public SelectStatement Build()
    {
        if (_statement.WithTies && _statement.OrderBy is not { Count: > 0 })
        {
            throw new InvalidOperationException("TOP WITH TIES requires ORDER BY.");
        }

        return _statement;
    }

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);

    private SelectBuilder AddJoin(
        JoinKind kind,
        TableSource right,
        SqlExpression? condition,
        JoinSyntax syntax = JoinSyntax.Explicit,
        IReadOnlyList<SqlIdentifier>? usingColumns = null,
        bool isNatural = false)
    {
        ArgumentNullException.ThrowIfNull(right);
        if (_statement.From is null)
        {
            throw new InvalidOperationException("FROM must be specified before adding a join.");
        }

        if (kind == JoinKind.Cross && syntax == JoinSyntax.Explicit
            && (condition is not null || usingColumns is { Count: > 0 } || isNatural))
        {
            throw new ArgumentException("A CROSS JOIN cannot have ON, USING, or NATURAL modifiers.", nameof(kind));
        }

        _statement = _statement with
        {
            From = new JoinTable(
                _statement.From,
                right,
                kind,
                condition,
                syntax,
                usingColumns,
                isNatural),
        };
        return this;
    }
}

public sealed class SetQueryBuilder
{
    private SetOperationStatement _statement;

    internal SetQueryBuilder(SetOperationStatement statement) => _statement = statement;

    public SetQueryBuilder Union(SelectBuilder right, bool all = false)
        => Union(right?.Build() ?? throw new ArgumentNullException(nameof(right)), all);

    public SetQueryBuilder Union(SqlQuery right, bool all = false)
    {
        AddOperation(SetOperator.Union, right, all);
        return this;
    }

    public SetQueryBuilder Intersect(SelectBuilder right, bool all = false)
        => Intersect(right?.Build() ?? throw new ArgumentNullException(nameof(right)), all);

    public SetQueryBuilder Intersect(SqlQuery right, bool all = false)
    {
        AddOperation(SetOperator.Intersect, right, all);
        return this;
    }

    public SetQueryBuilder Except(SelectBuilder right, bool all = false)
        => Except(right?.Build() ?? throw new ArgumentNullException(nameof(right)), all);

    public SetQueryBuilder Except(SqlQuery right, bool all = false)
    {
        AddOperation(SetOperator.Except, right, all);
        return this;
    }

    public SetQueryBuilder OrderBy(
        SqlExpression expression,
        OrderDirection direction = OrderDirection.Ascending)
    {
        _statement = _statement with { OrderBy = [new OrderByItem(expression, direction)] };
        return this;
    }

    public SetQueryBuilder ThenBy(
        SqlExpression expression,
        OrderDirection direction = OrderDirection.Ascending,
        NullOrder nullOrder = NullOrder.Unspecified)
    {
        var items = _statement.OrderBy?.ToList() ?? [];
        items.Add(new OrderByItem(expression, direction, nullOrder));
        _statement = _statement with { OrderBy = items };
        return this;
    }

    public SetQueryBuilder Limit(long value)
        => Limit(Sql.Lit(value));

    public SetQueryBuilder Limit(SqlExpression value)
    {
        _statement = _statement with
        {
            Limit = value ?? throw new ArgumentNullException(nameof(value)),
        };
        return this;
    }

    public SetQueryBuilder Offset(long value)
        => Offset(Sql.Lit(value));

    public SetQueryBuilder Offset(SqlExpression value)
    {
        _statement = _statement with
        {
            Offset = value ?? throw new ArgumentNullException(nameof(value)),
        };
        return this;
    }

    public SetQueryBuilder With(string name, SqlQuery query, params string[] columns)
        => With(name, query, CteMaterialization.Unspecified, columns);

    public SetQueryBuilder With(
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

    public SetQueryBuilder Recursive(bool value = true)
    {
        _statement = _statement with { IsRecursive = value };
        return this;
    }

    public ExplainBuilder Explain(bool analyze = false) => new(Build(), analyze);

    public SetOperationStatement Build() => _statement;

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);

    private void AddOperation(SetOperator @operator, SqlQuery right, bool all)
    {
        ArgumentNullException.ThrowIfNull(right);
        _statement = new SetOperationStatement(_statement, @operator, right, all);
    }
}
