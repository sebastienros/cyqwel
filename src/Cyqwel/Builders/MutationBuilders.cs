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

    internal InsertBuilder(string table) => _target = new TableName(table);

    public InsertBuilder Columns(params string[] columns)
    {
        _columns = columns.Select(static column => new SqlIdentifier(column)).ToArray();
        return this;
    }

    public InsertBuilder Values(params object?[] values)
    {
        if (_source is not null) throw new InvalidOperationException("An INSERT cannot contain both VALUES and a source query.");
        if (_columns is not null && values.Length != _columns.Count)
        {
            throw new ArgumentException("The number of values must match the number of columns.", nameof(values));
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

    public InsertStatement Build()
    {
        if (_source is null && _values.Count == 0)
        {
            throw new InvalidOperationException("An INSERT requires at least one VALUES row or a source query.");
        }

        return new InsertStatement(
            _target,
            _columns?.ToArray(),
            _values.Count == 0
                ? null
                : _values.Select(static row => (IReadOnlyList<SqlExpression>)row.ToArray()).ToArray(),
            _source,
            _returning?.ToArray());
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

    public UpdateBuilder Returning(params SqlExpression[] expressions)
    {
        _returning = expressions;
        return this;
    }

    public UpdateStatement Build()
    {
        if (_assignments.Count == 0) throw new InvalidOperationException("An UPDATE requires at least one assignment.");
        return new UpdateStatement(_target, _assignments.ToArray(), _where, _returning?.ToArray());
    }

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class DeleteBuilder
{
    private readonly NamedTable _target;
    private SqlExpression? _where;
    private IReadOnlyList<SqlExpression>? _returning;

    internal DeleteBuilder(string table, string? alias) => _target = new NamedTable(table, alias);

    public DeleteBuilder Where(SqlExpression predicate)
    {
        _where = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    public DeleteBuilder Returning(params SqlExpression[] expressions)
    {
        _returning = expressions;
        return this;
    }

    public DeleteStatement Build() => new(_target, _where, _returning?.ToArray());

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
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
