# Builders / SelectBuilder.cs

Fluent builders for queries.

## SelectBuilder (sealed class)

Created by `Sql.Select(...)` / `Sql.SelectItems(...)`. Every method returns the builder (fluent) and produces an immutable `SelectStatement` through `Build()`.

- **Projection options**: `Distinct`, `Into(target)` (`SELECT ... INTO`), `Top(value[, percent, withTies])`.
- **FROM**: `From(table, alias)`, `From(source)`, `From(subquery, alias[, columns])`.
- **Joins**: `Join`, `LeftJoin`, `RightJoin`, `FullJoin`, `CrossJoin`, generic `Join(...)` overloads, `JoinUsing(...)`, `NaturalJoin(...)`, `CommaJoin(...)`, `CrossApply(source)`, `OuterApply(source)`.
- **Filtering and grouping**: `Where`, `AndWhere` (combines with `AND`), `GroupBy`, `Having`, `Qualify`.
- **Ordering and paging**: `OrderBy`, `ThenBy`, `Limit`, `Offset` (long or expression), `OrderSiblingsBy`.
- **CTEs and windows**: `With(name, query, columns...)`, `Recursive`, `Window(definition)`, `ConnectBy(...)` (Oracle hierarchy).
- **T-SQL**: `QueryOptions(...)` (`OPTION (...)`), `ResultFormat(...)` (`FOR JSON/XML`).
- **Set operations**: `Union`, `Intersect`, `Except` (with `all`), returning a `SetQueryBuilder`.
- `Explain(analyze)`, `Build()`, `ToSql(dialect?, options?)`.

## SetQueryBuilder (sealed class)

Builder for `UNION` / `INTERSECT` / `EXCEPT` chains. Supports chaining further set operations, `OrderBy`, `ThenBy`, `Limit`, `Offset`, `With`, `Recursive`, `QueryOptions`, `ResultFormat`, `Explain`, `Build()` (returns `SetOperationStatement`) and `ToSql`.
