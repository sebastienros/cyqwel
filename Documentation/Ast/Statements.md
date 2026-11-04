# Ast / Statements.cs

DML and query statements.

## SelectStatement
Full `SELECT`: projections, `From`, `Where`, `GroupBy`, `Having`, `OrderBy`, `Limit`, `Offset`, `IsDistinct`, CTEs, `Top` / `IsTopPercent` / `WithTies`, `IsRecursive`, `Windows`, `Qualify`, `ConnectBy`, `OrderSiblings`, and `Into` (`SELECT ... INTO`).

## ValuesStatement
`VALUES (...), (...)` with optional ordering, paging and CTEs.

## SetOperator (enum) / SetOperationStatement
`Union`, `Intersect`, `Except`. Combines two queries with optional `ALL`, ordering and paging.

## ExplainStatement
`EXPLAIN [ANALYZE] query`.

## InsertStatement
`INSERT INTO target (columns) VALUES ... | query | DEFAULT VALUES`, with `Returning`, `ReturningInto`, CTEs, `Top`, T-SQL `Output`.

## UpdateStatement
`UPDATE` with assignments, `Where`, `From`, `Returning`, CTEs, `Top`, `Output`.

## DeleteStatement
`DELETE` with `Where`, `Using` / `From`, `Returning`, CTEs, `Top`, `Output`.

## MergeStatement
`MERGE` with target, source, join condition and `WhenClauses`; supports `Returning`, CTEs, `Top`, `Output`.

## TSqlOutputClause
T-SQL `OUTPUT items [INTO table (columns)]`.

## MergeActionExpression
Represents `$action` in a `MERGE ... OUTPUT`.

## GrantStatement
`GRANT` on objects to grantees (simple form).

## SetStatement
Generic `SET keywords arguments` statement; `ToggleValue` for `ON`/`OFF` options.


