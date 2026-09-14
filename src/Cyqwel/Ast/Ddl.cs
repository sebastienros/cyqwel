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

public enum IndexClustering
{
    Unspecified,
    Clustered,
    Nonclustered,
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
    bool IsUnique = false) : TableElement
{
    public SqlExpression? IdentitySeed { get; init; }
    public SqlExpression? IdentityIncrement { get; init; }
    public SqlIdentifier? DefaultConstraintName { get; init; }
    public SqlIdentifier? Collation { get; init; }
    public SqlIdentifier? KeyConstraintName { get; init; }
    public IndexClustering Clustering { get; init; }
    public IReadOnlyList<TableConstraint>? Constraints { get; init; }
}

public abstract record TableConstraint : TableElement
{
    public SqlIdentifier? Name { get; init; }
    public IndexClustering Clustering { get; init; }
    public IReadOnlyList<OrderDirection>? ColumnDirections { get; init; }
}

public sealed record ComputedColumnDefinition(
    SqlIdentifier Name,
    SqlExpression Expression,
    bool IsPersisted = false,
    Nullability Nullability = Nullability.Unspecified) : TableElement;

public sealed record DefaultConstraint(
    SqlExpression Value,
    SqlIdentifier Column) : TableConstraint;

public sealed record IndexTableElement(
    SqlIdentifier? Name,
    IReadOnlyList<IndexColumn> Columns,
    bool IsUnique = false,
    bool IsKey = false) : TableElement
{
    public IndexClustering Clustering { get; init; }
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

public sealed record AddTableElementAction(TableElement Element) : AlterTableAction;

public sealed record DropColumnAction(
    SqlIdentifier Column,
    bool IfExists = false,
    bool Cascade = false) : AlterTableAction;

public sealed record AlterColumnAction(
    SqlIdentifier Column,
    SqlDataType? DataType = null,
    Nullability Nullability = Nullability.Unspecified,
    SqlExpression? Default = null,
    bool DropDefault = false) : AlterTableAction
{
    public SqlIdentifier? Collation { get; init; }
}

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
    IReadOnlyList<AlterTableAction> Actions) : SqlStatement
{
    public bool? WithCheck { get; init; }
}

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
    ViewSecurity? Security = null) : SqlStatement
{
    public bool IsAlter { get; init; }
    public bool OrAlter { get; init; }
}

public sealed record CreateInlineFunctionStatement(
    TableName Name,
    IReadOnlyList<ProcedureParameter> Parameters,
    SqlQuery Query) : SqlStatement;

public sealed record CreateSchemaStatement(SqlIdentifier Name) : SqlStatement;

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
    SqlExpression? Where = null) : SqlStatement
{
    public IndexClustering Clustering { get; init; }
}

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
