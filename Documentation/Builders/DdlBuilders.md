# Builders / DdlBuilders.cs

Fluent builders for schema statements. All are created through `Sql` and expose `Build()` and `ToSql(dialect?, options?)`.

## CreateTableBuilder (sealed class)
`Sql.CreateTable(name)`. Methods: `IfNotExists`, `Temporary`, `Add(element)`, `Column(definition | name/type...)`, `Constraint(constraint)`, `As(query)` (`CREATE TABLE ... AS`). Builds a `CreateTableStatement`.

## AlterTableBuilder (sealed class)
`Sql.AlterTable(name)`. Methods: `WithCheck`, `Add(action)`, `AddColumn`, `DropColumn`, `AlterColumn`, `AlterColumnType`, `AddConstraint`, `DropConstraint`, `RenameColumn`, `RenameTo`. Builds an `AlterTableStatement`.

## DropBuilder (sealed class)
`Sql.Drop(kind, name)` / `Sql.DropTable(name)`. `And(name)` adds objects, `IfExists`, `Cascade`. Builds a `DropStatement`.

## TruncateBuilder (sealed class)
`Sql.Truncate(table)`. `And(table)`, `RestartIdentity`, `Cascade`. Builds a `TruncateStatement`.

## CreateViewBuilder (sealed class)
`Sql.CreateView(name)`. `As(query)`, `Columns`, `OrReplace`, `OrAlter`, `Alter`, `Temporary`, `Security(ViewSecurity)`. Builds a `CreateViewStatement`.

## CreateIndexBuilder (sealed class)
`Sql.CreateIndex(name, table)`. `Column(...)` (name or expression, with direction), `Unique`, `IfNotExists`, `Where(predicate)` (partial index), `Clustering(IndexClustering)`. Builds a `CreateIndexStatement`.

## CreateSequenceBuilder (sealed class)
`Sql.CreateSequence(name)`. `IfNotExists`, `StartWith`, `IncrementBy`, `MinValue`, `MaxValue`, `Cache`, `Cycle`. Builds a `CreateSequenceStatement`.

## AlterSequenceBuilder (sealed class)
`Sql.AlterSequence(name)`. Same options as `CreateSequenceBuilder` (without `IfNotExists`). Builds an `AlterSequenceStatement`.

```csharp
Sql.CreateTable("users")
   .Column(Sql.DefineColumn("id", new SqlDataType("INTEGER")))
   .Constraint(Sql.PrimaryKey("id"))
   .ToSql(SqlDialects.Sqlite);
```
