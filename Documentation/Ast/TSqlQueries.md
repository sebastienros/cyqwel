# Ast / TSqlQueries.cs

T-SQL-specific query constructs.

## ParenthesizedTable
Parenthesized table source with optional alias.

## DerivedMutationTable
`MERGE ... OUTPUT` used as a table source.

## TableFunction
Table-valued function call in `FROM` with alias and column aliases.

## OpenJsonTable / OpenJsonColumn
`OPENJSON(expr, path) WITH (columns)`; each column has a name, type, optional JSON path and `AsJson`.

## PivotTable / UnpivotTable
`PIVOT (aggregate FOR column IN (values))` and `UNPIVOT (value FOR name IN (columns))`.

## TSqlTableHintKind (enum) / TSqlTableHint / TSqlIndexReference
Table hints (`NOLOCK`, `ROWLOCK`, `UPDLOCK`, `INDEX(...)`, ...). `TSqlTableHint` exposes static singletons (`TSqlTableHint.NoLock`, ...). `TSqlIndexReference` references an index by name or by id.

## TSqlTableSampleUnit (enum) / TSqlTableSample
`TABLESAMPLE [SYSTEM] (amount PERCENT|ROWS)`; the amount must be non-negative and at most 100 for percent.

## TSqlJoinHint (enum)
`Hash`, `Loop`, `Merge`.

## TSqlQueryOptionKind (enum) / TSqlQueryOption
`OPTION (RECOMPILE | MAXDOP n | MAXRECURSION n | OPTIMIZE FOR UNKNOWN | FORCE ORDER | FAST n)`, validated by value range.

## TSqlResultFormatKind / TSqlResultFormatMode (enums) / TSqlResultFormat
`FOR JSON` / `FOR XML` with `AUTO`, `PATH`, `RAW`, root, element name, null inclusion, array wrapper and `TYPE` options; `IsValid` validates option combinations.

