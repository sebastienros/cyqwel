namespace Cyqwel.Ast;

public abstract record TableElement : SqlNode;

public enum Nullability
{
    Unspecified,
    Null,
    NotNull,
}

public enum GeneratedColumnKind
{
    Virtual,
    Stored,
}

public enum IdentityGeneration
{
    None,
    Always,
    ByDefault,
}

public sealed record ColumnDefinition(
    SqlIdentifier Name,
    SqlDataType DataType,
    Nullability Nullability = Nullability.Unspecified,
    SqlExpression? Default = null,
    SqlExpression? GeneratedExpression = null,
    GeneratedColumnKind GeneratedKind = GeneratedColumnKind.Virtual,
    IdentityGeneration Identity = IdentityGeneration.None,
    bool IsPrimaryKey = false,
    bool IsUnique = false) : TableElement;

public abstract record TableConstraint : TableElement
{
    public SqlIdentifier? Name { get; init; }
}

public sealed record PrimaryKeyConstraint(
    IReadOnlyList<SqlIdentifier> Columns) : TableConstraint
{
    public PrimaryKeyConstraint(
        IReadOnlyList<SqlIdentifier> columns,
        SqlIdentifier? name)
        : this(columns)
    {
        Name = name;
    }
}

public sealed record UniqueConstraint(
    IReadOnlyList<SqlIdentifier> Columns) : TableConstraint
{
    public UniqueConstraint(
        IReadOnlyList<SqlIdentifier> columns,
        SqlIdentifier? name)
        : this(columns)
    {
        Name = name;
    }
}

public enum ReferentialAction
{
    Unspecified,
    Cascade,
    Restrict,
    SetNull,
    SetDefault,
    NoAction,
}

public sealed record ForeignKeyConstraint(
    IReadOnlyList<SqlIdentifier> Columns,
    TableName ReferencedTable,
    IReadOnlyList<SqlIdentifier> ReferencedColumns,
    ReferentialAction OnDelete = ReferentialAction.Unspecified,
    ReferentialAction OnUpdate = ReferentialAction.Unspecified) : TableConstraint
{
    public ForeignKeyConstraint(
        IReadOnlyList<SqlIdentifier> columns,
        TableName referencedTable,
        IReadOnlyList<SqlIdentifier> referencedColumns,
        ReferentialAction onDelete,
        ReferentialAction onUpdate,
        SqlIdentifier? name)
        : this(columns, referencedTable, referencedColumns, onDelete, onUpdate)
    {
        Name = name;
    }
}

public sealed record CheckConstraint(
    SqlExpression Condition) : TableConstraint
{
    public CheckConstraint(SqlExpression condition, SqlIdentifier? name)
        : this(condition)
    {
        Name = name;
    }
}

public sealed record CreateTableStatement(
    TableName Name,
    IReadOnlyList<TableElement> Elements,
    bool IfNotExists = false,
    bool IsTemporary = false,
    SqlQuery? AsQuery = null) : SqlStatement;

public abstract record AlterTableAction : SqlNode;

public sealed record AddColumnAction(ColumnDefinition Column) : AlterTableAction;

public sealed record DropColumnAction(
    SqlIdentifier Column,
    bool IfExists = false,
    bool Cascade = false) : AlterTableAction;

public sealed record AlterColumnAction(
    SqlIdentifier Column,
    SqlDataType? DataType = null,
    Nullability Nullability = Nullability.Unspecified,
    SqlExpression? Default = null,
    bool DropDefault = false) : AlterTableAction;

public sealed record AddConstraintAction(TableConstraint Constraint) : AlterTableAction;

public sealed record DropConstraintAction(
    SqlIdentifier Constraint,
    bool IfExists = false,
    bool Cascade = false) : AlterTableAction;

public sealed record RenameColumnAction(
    SqlIdentifier Column,
    SqlIdentifier NewName) : AlterTableAction;

public sealed record RenameTableAction(SqlIdentifier NewName) : AlterTableAction;

public sealed record AlterTableStatement(
    TableName Name,
    IReadOnlyList<AlterTableAction> Actions) : SqlStatement;

public enum SchemaObjectKind
{
    Table,
    View,
    Index,
    Schema,
    Sequence,
}

public sealed record DropStatement(
    SchemaObjectKind Kind,
    IReadOnlyList<TableName> Names,
    bool IfExists = false,
    bool Cascade = false) : SqlStatement;

public sealed record TruncateStatement(
    IReadOnlyList<TableName> Tables,
    bool RestartIdentity = false,
    bool Cascade = false) : SqlStatement;

public enum ViewSecurity
{
    Definer,
    Invoker,
}

public sealed record CreateViewStatement(
    TableName Name,
    SqlQuery Query,
    IReadOnlyList<SqlIdentifier>? Columns = null,
    bool OrReplace = false,
    bool IsTemporary = false,
    ViewSecurity? Security = null) : SqlStatement;

public sealed record IndexColumn(
    SqlExpression Expression,
    OrderDirection Direction = OrderDirection.Unspecified,
    NullOrder NullOrder = NullOrder.Unspecified) : SqlNode;

public sealed record CreateIndexStatement(
    TableName Name,
    TableName Table,
    IReadOnlyList<IndexColumn> Columns,
    bool IsUnique = false,
    bool IfNotExists = false,
    SqlExpression? Where = null) : SqlStatement;

public sealed record SequenceOptions(
    SqlExpression? StartWith = null,
    SqlExpression? IncrementBy = null,
    SqlExpression? MinimumValue = null,
    SqlExpression? MaximumValue = null,
    SqlExpression? Cache = null,
    bool? Cycle = null) : SqlNode;

public sealed record CreateSequenceStatement(
    TableName Name,
    SequenceOptions Options,
    bool IfNotExists = false) : SqlStatement;

public sealed record AlterSequenceStatement(
    TableName Name,
    SequenceOptions Options) : SqlStatement;
