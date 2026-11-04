# Parsing / SqlParser.TSqlQueries.cs

Partial of `SqlParser` (see `SqlParser.md`). Adds the grammar of T-SQL query extensions, enabled when `SupportsTSqlExtensions` is set: table hints, index references, `TABLESAMPLE`, table functions, `OPENJSON`, `PIVOT`/`UNPIVOT`, `CROSS/OUTER APPLY`, `OPTION (...)` and `FOR JSON/XML`.
