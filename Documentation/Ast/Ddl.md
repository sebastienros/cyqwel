# Ast / Ddl.cs

Schema definition statements.

## TableElement (abstract record)
Base of table body elements (columns, constraints, indexes).

## Enums: Nullability, GeneratedColumnKind, IdentityGeneration, IndexClustering, ReferentialAction, SchemaObjectKind, ViewSecurity
- `Nullability`: `Unspecified`, `Null`, `NotNull`.
- `GeneratedColumnKind`: `Virtual`, `Stored`.
- `IdentityGeneration`: `None`, `Always`, `ByDefault`.
- `IndexClustering`: `Unspecified`, `Clustered`, `Nonclustered`.
- `ReferentialAction`: `Cascade`, `Restrict`, `SetNull`, `SetDefault`, `NoAction`.
- `SchemaObjectKind`: `Table`, `View`, `Index`, `Schema`, `Sequence`.
- `ViewSecurity`: `Definer`, `Invoker`.

## ColumnDefinition
Column with type, nullability, default, generated expression, identity (`IdentitySeed`, `IdentityIncrement`), inline `IsPrimaryKey` / `IsUnique`, named constraints, collation and clustering.

## TableConstraint (abstract record)
Base of constraints: optional `Name`, `Clustering`, `ColumnDirections`.

## ComputedColumnDefinition
T-SQL computed column (`AS expression [PERSISTED]`).

## DefaultConstraint / PrimaryKeyConstraint / UniqueConstraint / ForeignKeyConstraint / CheckConstraint
Concrete constraints. The foreign key has `ReferencedTable`, `ReferencedColumns`, `OnDelete`, `OnUpdate`. Extra constructors accept the constraint name.

## IndexTableElement / IndexColumn
Inline index in a table body, and indexed column (expression, direction, null ordering).

## CreateTableStatement
`CREATE [TEMPORARY] TABLE [IF NOT EXISTS] name (elements) | AS query`.

## AlterTableAction (abstract record) and actions
`AddColumnAction`, `AddTableElementAction`, `DropColumnAction`, `AlterColumnAction`, `AddConstraintAction`, `DropConstraintAction`, `RenameColumnAction`, `RenameTableAction`.

## AlterTableStatement
`ALTER TABLE name actions`, with T-SQL `WithCheck`.

## DropStatement / TruncateStatement
`DROP <kind> [IF EXISTS] names [CASCADE]` and `TRUNCATE TABLE ... [RESTART IDENTITY] [CASCADE]`.

## CreateViewStatement
`CREATE [OR REPLACE] [TEMPORARY] VIEW`, optional column list and `Security`; `IsAlter` / `OrAlter` for `ALTER VIEW` and `CREATE OR ALTER`.

## CreateInlineFunctionStatement
T-SQL inline table-valued function (`RETURNS TABLE AS RETURN query`).

## CreateSchemaStatement
`CREATE SCHEMA name`.

## CreateIndexStatement
`CREATE [UNIQUE] INDEX [IF NOT EXISTS] name ON table (columns) [WHERE ...]`.

## SequenceOptions
`START WITH`, `INCREMENT BY`, `MINVALUE`, `MAXVALUE`, `CACHE`, `CYCLE`.

## CreateSequenceStatement / AlterSequenceStatement
Create and alter sequences.


