using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;

namespace Cyqwel;

public sealed class InsertBuilder
{
    private readonly TableName _target;
    private IReadOnlyList<SqlIdentifier>? _columns;
    private readonly List<IReadOnlyList<SqlExpression>> _values = [];
    private SqlQuery? _source;
    private IReadOnlyList<SqlExpression>? _returning;
    private IReadOnlyList<SqlExpression>? _returningInto;

    internal InsertBuilder(string table) => _target = new TableName(table);

    public InsertBuilder Columns(params string[] columns)
    {
        if (columns.Length == 0)
        {
            throw new ArgumentException("At least one column is required.", nameof(columns));
        }

        if (_values.Any(row => row.Count != columns.Length))
        {
            throw new ArgumentException(
                "The number of columns must match every existing VALUES row.",
                nameof(columns));
        }

        _columns = columns.Select(static column => new SqlIdentifier(column)).ToArray();
        return this;
    }

    public InsertBuilder Values(params object?[] values)
    {
        if (_source is not null) throw new InvalidOperationException("An INSERT cannot contain both VALUES and a source query.");
        if (values.Length == 0) throw new ArgumentException("At least one value is required.", nameof(values));
        var expectedCount = _columns?.Count ?? (_values.Count == 0 ? null : _values[0].Count);
        if (expectedCount.HasValue && values.Length != expectedCount.Value)
        {
            throw new ArgumentException(
                "Every VALUES row must match the established column count.",
                nameof(values));
        }

        _values.Add(values.Select(Sql.Coerce).ToArray());
        return this;
    }

    public InsertBuilder From(SqlQuery query)
    {
        if (_values.Count > 0) throw new InvalidOperationException("An INSERT cannot contain both VALUES and a source query.");
        _source = query ?? throw new ArgumentNullException(nameof(query));
        return this;
    }

    public InsertBuilder Returning(params SqlExpression[] expressions)
    {
        _returning = expressions;
        return this;
    }

    public InsertBuilder ReturningInto(params SqlExpression[] expressions)
    {
        _returningInto = expressions;
        return this;
    }

    public InsertStatement Build()
    {
        if (_source is null && _values.Count == 0)
        {
            throw new InvalidOperationException("An INSERT requires at least one VALUES row or a source query.");
        }

        MutationBuilderValidation.ValidateReturningInto(_returning, _returningInto);

        return new InsertStatement(
            _target,
            _columns?.ToArray(),
            _values.Count == 0
                ? null
                : _values.Select(static row => (IReadOnlyList<SqlExpression>)row.ToArray()).ToArray(),
            _source,
            _returning?.ToArray(),
            _returningInto?.ToArray());
    }

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class UpdateBuilder
{
    private readonly NamedTable _target;
    private readonly List<Assignment> _assignments = [];
    private SqlExpression? _where;
    private IReadOnlyList<SqlExpression>? _returning;
    private IReadOnlyList<SqlExpression>? _returningInto;
    private TableSource? _from;

    internal UpdateBuilder(string table, string? alias) => _target = new NamedTable(table, alias);

    public UpdateBuilder Set(string column, object? value)
    {
        _assignments.Add(new Assignment(Sql.Col(column), Sql.Coerce(value)));
        return this;
    }

    public UpdateBuilder Where(SqlExpression predicate)
    {
        _where = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    public UpdateBuilder From(string table, string? alias = null) =>
        From(new NamedTable(table, alias));

    public UpdateBuilder From(SqlQuery query, string alias) =>
        From(new DerivedTable(
            query ?? throw new ArgumentNullException(nameof(query)),
            new SqlIdentifier(alias)));

    public UpdateBuilder From(TableSource source)
    {
        _from = source ?? throw new ArgumentNullException(nameof(source));
        return this;
    }

    public UpdateBuilder Returning(params SqlExpression[] expressions)
    {
        _returning = expressions;
        return this;
    }

    public UpdateBuilder ReturningInto(params SqlExpression[] expressions)
    {
        _returningInto = expressions;
        return this;
    }

    public UpdateStatement Build()
    {
        if (_assignments.Count == 0) throw new InvalidOperationException("An UPDATE requires at least one assignment.");
        MutationBuilderValidation.ValidateReturningInto(_returning, _returningInto);

        return new UpdateStatement(
            _target,
            _assignments.ToArray(),
            _where,
            _returning?.ToArray(),
            _returningInto?.ToArray(),
            _from);
    }

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class DeleteBuilder
{
    private readonly NamedTable _target;
    private SqlExpression? _where;
    private IReadOnlyList<SqlExpression>? _returning;
    private IReadOnlyList<SqlExpression>? _returningInto;
    private TableSource? _using;

    internal DeleteBuilder(string table, string? alias) => _target = new NamedTable(table, alias);

    public DeleteBuilder Where(SqlExpression predicate)
    {
        _where = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    public DeleteBuilder Using(string table, string? alias = null) =>
        Using(new NamedTable(table, alias));

    public DeleteBuilder Using(SqlQuery query, string alias) =>
        Using(new DerivedTable(
            query ?? throw new ArgumentNullException(nameof(query)),
            new SqlIdentifier(alias)));

    public DeleteBuilder Using(TableSource source)
    {
        _using = source ?? throw new ArgumentNullException(nameof(source));
        return this;
    }

    public DeleteBuilder Returning(params SqlExpression[] expressions)
    {
        _returning = expressions;
        return this;
    }

    public DeleteBuilder ReturningInto(params SqlExpression[] expressions)
    {
        _returningInto = expressions;
        return this;
    }

    public DeleteStatement Build()
    {
        MutationBuilderValidation.ValidateReturningInto(_returning, _returningInto);

        return new DeleteStatement(
            _target,
            _where,
            _returning?.ToArray(),
            _returningInto?.ToArray(),
            _using);
    }

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class MergeBuilder
{
    private readonly NamedTable _target;
    private TableSource? _source;
    private SqlExpression? _condition;
    private readonly List<MergeWhenClause> _whenClauses = [];
    private IReadOnlyList<SqlExpression>? _returning;
    private IReadOnlyList<SqlExpression>? _returningInto;

    internal MergeBuilder(string table, string? alias) => _target = new NamedTable(table, alias);

    public MergeBuilder Using(string table, string? alias = null) =>
        Using(new NamedTable(table, alias));

    public MergeBuilder Using(SqlQuery query, string alias) =>
        Using(new DerivedTable(
            query ?? throw new ArgumentNullException(nameof(query)),
            new SqlIdentifier(alias)));

    public MergeBuilder Using(TableSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        return this;
    }

    public MergeBuilder On(SqlExpression condition)
    {
        _condition = condition ?? throw new ArgumentNullException(nameof(condition));
        return this;
    }

    public MergeBuilder WhenMatchedUpdate(params Assignment[] assignments) =>
        AddUpdate(MergeMatchKind.Matched, condition: null, assignments);

    public MergeBuilder WhenMatchedUpdate(
        SqlExpression condition,
        params Assignment[] assignments) =>
        AddUpdate(MergeMatchKind.Matched, condition, assignments);

    public MergeBuilder WhenMatchedUpdate(
        SqlExpression? condition,
        SqlExpression deleteWhere,
        params Assignment[] assignments) =>
        AddUpdate(MergeMatchKind.Matched, condition, assignments, deleteWhere);

    public MergeBuilder WhenNotMatchedBySourceUpdate(params Assignment[] assignments) =>
        AddUpdate(MergeMatchKind.NotMatchedBySource, condition: null, assignments);

    public MergeBuilder WhenNotMatchedBySourceUpdate(
        SqlExpression condition,
        params Assignment[] assignments) =>
        AddUpdate(MergeMatchKind.NotMatchedBySource, condition, assignments);

    public MergeBuilder WhenNotMatchedInsert(
        IReadOnlyList<string>? columns,
        params object?[] values) =>
        AddInsert(condition: null, columns, values);

    public MergeBuilder WhenNotMatchedInsert(
        SqlExpression condition,
        IReadOnlyList<string>? columns,
        params object?[] values) =>
        AddInsert(condition, columns, values);

    public MergeBuilder WhenMatchedDelete(SqlExpression? condition = null) =>
        AddDelete(MergeMatchKind.Matched, condition);

    public MergeBuilder WhenNotMatchedBySourceDelete(SqlExpression? condition = null) =>
        AddDelete(MergeMatchKind.NotMatchedBySource, condition);

    public MergeBuilder Returning(params SqlExpression[] expressions)
    {
        _returning = expressions;
        return this;
    }

    public MergeBuilder ReturningInto(params SqlExpression[] expressions)
    {
        _returningInto = expressions;
        return this;
    }

    public MergeStatement Build()
    {
        if (_source is null) throw new InvalidOperationException("A MERGE requires a source.");
        if (_condition is null) throw new InvalidOperationException("A MERGE requires an ON condition.");
        if (_whenClauses.Count == 0) throw new InvalidOperationException("A MERGE requires at least one WHEN clause.");
        MutationBuilderValidation.ValidateReturningInto(_returning, _returningInto);

        return new MergeStatement(
            _target,
            _source,
            _condition,
            _whenClauses.ToArray(),
            _returning?.ToArray(),
            _returningInto?.ToArray());
    }

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);

    private MergeBuilder AddUpdate(
        MergeMatchKind matchKind,
        SqlExpression? condition,
        IReadOnlyList<Assignment> assignments,
        SqlExpression? deleteWhere = null)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments.Count == 0)
        {
            throw new ArgumentException("At least one assignment is required.", nameof(assignments));
        }

        _whenClauses.Add(new MergeWhenClause(
            matchKind,
            new MergeUpdateAction(assignments.ToArray(), deleteWhere),
            condition));
        return this;
    }

    private MergeBuilder AddInsert(
        SqlExpression? condition,
        IReadOnlyList<string>? columns,
        IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        if (columns is { Count: > 0 } && columns.Count != values.Count)
        {
            throw new ArgumentException("The number of values must match the number of columns.", nameof(values));
        }

        _whenClauses.Add(new MergeWhenClause(
            MergeMatchKind.NotMatched,
            new MergeInsertAction(
                columns?.Select(static column => new SqlIdentifier(column)).ToArray(),
                values.Select(Sql.Coerce).ToArray()),
            condition));
        return this;
    }

    private MergeBuilder AddDelete(MergeMatchKind matchKind, SqlExpression? condition)
    {
        _whenClauses.Add(new MergeWhenClause(matchKind, new MergeDeleteAction(), condition));
        return this;
    }
}

internal static class MutationBuilderValidation
{
    internal static void ValidateReturningInto(
        IReadOnlyList<SqlExpression>? returning,
        IReadOnlyList<SqlExpression>? returningInto)
    {
        if (returningInto is not { Count: > 0 }) return;
        if (returning is not { Count: > 0 })
        {
            throw new InvalidOperationException("RETURNING INTO requires RETURNING expressions.");
        }

        if (returning.Count != returningInto.Count)
        {
            throw new InvalidOperationException(
                "RETURNING and INTO must contain the same number of expressions.");
        }
    }
}

public sealed class CaseBuilder
{
    private readonly SqlExpression? _operand;
    private readonly List<WhenClause> _whens = [];
    private SqlExpression? _else;

    internal CaseBuilder(SqlExpression? operand) => _operand = operand;

    public CaseBuilder When(SqlExpression condition, object? result)
    {
        _whens.Add(new WhenClause(
            condition ?? throw new ArgumentNullException(nameof(condition)),
            Sql.Coerce(result)));
        return this;
    }

    public CaseBuilder Else(object? result)
    {
        _else = Sql.Coerce(result);
        return this;
    }

    public CaseExpression Build()
    {
        if (_whens.Count == 0) throw new InvalidOperationException("A CASE expression requires at least one WHEN clause.");
        return new CaseExpression(_operand, _whens.ToArray(), _else);
    }
}
