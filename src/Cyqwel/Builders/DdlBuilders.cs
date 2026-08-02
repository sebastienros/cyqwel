using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;

namespace Cyqwel;

public sealed class CreateTableBuilder
{
    private readonly TableName _name;
    private readonly List<TableElement> _elements = [];
    private bool _ifNotExists;
    private bool _isTemporary;
    private SqlQuery? _asQuery;

    internal CreateTableBuilder(string name) => _name = new TableName(name);

    public CreateTableBuilder IfNotExists(bool value = true)
    {
        _ifNotExists = value;
        return this;
    }

    public CreateTableBuilder Temporary(bool value = true)
    {
        _isTemporary = value;
        return this;
    }

    public CreateTableBuilder Add(TableElement element)
    {
        _elements.Add(element ?? throw new ArgumentNullException(nameof(element)));
        return this;
    }

    public CreateTableBuilder Column(ColumnDefinition column) => Add(column);

    public CreateTableBuilder Column(
        string name,
        string dataType,
        params int[] dataTypeArguments) =>
        Column(new ColumnDefinition(
            new SqlIdentifier(name),
            new SqlDataType(dataType, dataTypeArguments)));

    public CreateTableBuilder Constraint(TableConstraint constraint) => Add(constraint);

    public CreateTableBuilder As(SqlQuery query)
    {
        _asQuery = query ?? throw new ArgumentNullException(nameof(query));
        return this;
    }

    public CreateTableStatement Build()
    {
        if (_elements.Count == 0 && _asQuery is null)
        {
            throw new InvalidOperationException("CREATE TABLE requires at least one element or an AS query.");
        }

        return new CreateTableStatement(
            _name,
            _elements.ToArray(),
            _ifNotExists,
            _isTemporary,
            _asQuery);
    }

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class AlterTableBuilder
{
    private readonly TableName _name;
    private readonly List<AlterTableAction> _actions = [];

    internal AlterTableBuilder(string name) => _name = new TableName(name);

    public AlterTableBuilder Add(AlterTableAction action)
    {
        _actions.Add(action ?? throw new ArgumentNullException(nameof(action)));
        return this;
    }

    public AlterTableBuilder AddColumn(ColumnDefinition column) =>
        Add(new AddColumnAction(column));

    public AlterTableBuilder AddColumn(
        string name,
        string dataType,
        params int[] dataTypeArguments) =>
        AddColumn(new ColumnDefinition(
            new SqlIdentifier(name),
            new SqlDataType(dataType, dataTypeArguments)));

    public AlterTableBuilder DropColumn(
        string column,
        bool ifExists = false,
        bool cascade = false) =>
        Add(new DropColumnAction(new SqlIdentifier(column), ifExists, cascade));

    public AlterTableBuilder AlterColumn(
        string column,
        SqlDataType? dataType = null,
        Nullability nullability = Nullability.Unspecified,
        SqlExpression? defaultValue = null,
        bool dropDefault = false)
    {
        var operationCount =
            (dataType is null ? 0 : 1)
            + (nullability == Nullability.Unspecified ? 0 : 1)
            + (defaultValue is null ? 0 : 1)
            + (dropDefault ? 1 : 0);
        if (operationCount != 1)
        {
            throw new ArgumentException("ALTER COLUMN requires exactly one operation.");
        }

        return Add(new AlterColumnAction(
            new SqlIdentifier(column),
            dataType,
            nullability,
            defaultValue,
            dropDefault));
    }

    public AlterTableBuilder AddConstraint(TableConstraint constraint) =>
        Add(new AddConstraintAction(constraint));

    public AlterTableBuilder DropConstraint(
        string constraint,
        bool ifExists = false,
        bool cascade = false) =>
        Add(new DropConstraintAction(new SqlIdentifier(constraint), ifExists, cascade));

    public AlterTableBuilder RenameColumn(string column, string newName) =>
        Add(new RenameColumnAction(new SqlIdentifier(column), new SqlIdentifier(newName)));

    public AlterTableBuilder RenameTo(string newName) =>
        Add(new RenameTableAction(new SqlIdentifier(newName)));

    public AlterTableStatement Build()
    {
        if (_actions.Count == 0)
        {
            throw new InvalidOperationException("ALTER TABLE requires at least one action.");
        }

        return new AlterTableStatement(_name, _actions.ToArray());
    }

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class DropBuilder
{
    private readonly SchemaObjectKind _kind;
    private readonly List<TableName> _names;
    private bool _ifExists;
    private bool _cascade;

    internal DropBuilder(SchemaObjectKind kind, string name)
    {
        _kind = kind;
        _names = [new TableName(name)];
    }

    public DropBuilder And(string name)
    {
        _names.Add(new TableName(name));
        return this;
    }

    public DropBuilder IfExists(bool value = true)
    {
        _ifExists = value;
        return this;
    }

    public DropBuilder Cascade(bool value = true)
    {
        _cascade = value;
        return this;
    }

    public DropStatement Build() => new(_kind, _names.ToArray(), _ifExists, _cascade);

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class TruncateBuilder
{
    private readonly List<TableName> _tables;
    private bool _restartIdentity;
    private bool _cascade;

    internal TruncateBuilder(string table) => _tables = [new TableName(table)];

    public TruncateBuilder And(string table)
    {
        _tables.Add(new TableName(table));
        return this;
    }

    public TruncateBuilder RestartIdentity(bool value = true)
    {
        _restartIdentity = value;
        return this;
    }

    public TruncateBuilder Cascade(bool value = true)
    {
        _cascade = value;
        return this;
    }

    public TruncateStatement Build() => new(_tables.ToArray(), _restartIdentity, _cascade);

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class CreateViewBuilder
{
    private readonly TableName _name;
    private SqlQuery? _query;
    private IReadOnlyList<SqlIdentifier>? _columns;
    private bool _orReplace;
    private bool _isTemporary;
    private ViewSecurity? _security;

    internal CreateViewBuilder(string name) => _name = new TableName(name);

    public CreateViewBuilder As(SqlQuery query)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        return this;
    }

    public CreateViewBuilder Columns(params string[] columns)
    {
        _columns = columns.Select(static column => new SqlIdentifier(column)).ToArray();
        return this;
    }

    public CreateViewBuilder OrReplace(bool value = true)
    {
        _orReplace = value;
        return this;
    }

    public CreateViewBuilder Temporary(bool value = true)
    {
        _isTemporary = value;
        return this;
    }

    public CreateViewBuilder Security(ViewSecurity value)
    {
        _security = value;
        return this;
    }

    public CreateViewStatement Build() =>
        new(
            _name,
            _query ?? throw new InvalidOperationException("CREATE VIEW requires an AS query."),
            _columns?.ToArray(),
            _orReplace,
            _isTemporary,
            _security);

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class CreateIndexBuilder
{
    private readonly TableName _name;
    private readonly TableName _table;
    private readonly List<IndexColumn> _columns = [];
    private bool _isUnique;
    private bool _ifNotExists;
    private SqlExpression? _where;

    internal CreateIndexBuilder(string name, string table)
    {
        _name = new TableName(name);
        _table = new TableName(table);
    }

    public CreateIndexBuilder Column(
        SqlExpression expression,
        OrderDirection direction = OrderDirection.Unspecified,
        NullOrder nullOrder = NullOrder.Unspecified)
    {
        _columns.Add(new IndexColumn(
            expression ?? throw new ArgumentNullException(nameof(expression)),
            direction,
            nullOrder));
        return this;
    }

    public CreateIndexBuilder Column(
        string column,
        OrderDirection direction = OrderDirection.Unspecified,
        NullOrder nullOrder = NullOrder.Unspecified) =>
        Column(Sql.Col(column), direction, nullOrder);

    public CreateIndexBuilder Unique(bool value = true)
    {
        _isUnique = value;
        return this;
    }

    public CreateIndexBuilder IfNotExists(bool value = true)
    {
        _ifNotExists = value;
        return this;
    }

    public CreateIndexBuilder Where(SqlExpression predicate)
    {
        _where = predicate ?? throw new ArgumentNullException(nameof(predicate));
        return this;
    }

    public CreateIndexStatement Build()
    {
        if (_columns.Count == 0)
        {
            throw new InvalidOperationException("CREATE INDEX requires at least one column.");
        }

        return new CreateIndexStatement(
            _name,
            _table,
            _columns.ToArray(),
            _isUnique,
            _ifNotExists,
            _where);
    }

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class CreateSequenceBuilder
{
    private readonly TableName _name;
    private SequenceOptions _options = new();
    private bool _ifNotExists;

    internal CreateSequenceBuilder(string name) => _name = new TableName(name);

    public CreateSequenceBuilder IfNotExists(bool value = true)
    {
        _ifNotExists = value;
        return this;
    }

    public CreateSequenceBuilder StartWith(object value)
    {
        _options = _options with { StartWith = Sql.Coerce(value) };
        return this;
    }

    public CreateSequenceBuilder IncrementBy(object value)
    {
        _options = _options with { IncrementBy = Sql.Coerce(value) };
        return this;
    }

    public CreateSequenceBuilder MinValue(object value)
    {
        _options = _options with { MinimumValue = Sql.Coerce(value) };
        return this;
    }

    public CreateSequenceBuilder MaxValue(object value)
    {
        _options = _options with { MaximumValue = Sql.Coerce(value) };
        return this;
    }

    public CreateSequenceBuilder Cache(object value)
    {
        _options = _options with { Cache = Sql.Coerce(value) };
        return this;
    }

    public CreateSequenceBuilder Cycle(bool value = true)
    {
        _options = _options with { Cycle = value };
        return this;
    }

    public CreateSequenceStatement Build() => new(_name, _options, _ifNotExists);

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class AlterSequenceBuilder
{
    private readonly TableName _name;
    private SequenceOptions _options = new();

    internal AlterSequenceBuilder(string name) => _name = new TableName(name);

    public AlterSequenceBuilder StartWith(object value)
    {
        _options = _options with { StartWith = Sql.Coerce(value) };
        return this;
    }

    public AlterSequenceBuilder IncrementBy(object value)
    {
        _options = _options with { IncrementBy = Sql.Coerce(value) };
        return this;
    }

    public AlterSequenceBuilder MinValue(object value)
    {
        _options = _options with { MinimumValue = Sql.Coerce(value) };
        return this;
    }

    public AlterSequenceBuilder MaxValue(object value)
    {
        _options = _options with { MaximumValue = Sql.Coerce(value) };
        return this;
    }

    public AlterSequenceBuilder Cache(object value)
    {
        _options = _options with { Cache = Sql.Coerce(value) };
        return this;
    }

    public AlterSequenceBuilder Cycle(bool value = true)
    {
        _options = _options with { Cycle = value };
        return this;
    }

    public AlterSequenceStatement Build()
    {
        if (_options == new SequenceOptions())
        {
            throw new InvalidOperationException("ALTER SEQUENCE requires at least one option.");
        }

        return new AlterSequenceStatement(_name, _options);
    }

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}
