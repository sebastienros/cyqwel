# Generation / SqlGenerator.Standard.cs

Partial of `SqlGenerator` (see `SqlGenerator.md`).

Contains the writers for standard SQL: SELECT (projection, FROM, joins, WHERE, GROUP BY, HAVING, WINDOW, QUALIFY, ORDER BY, pagination according to `SqlLimitStyle`), VALUES, set operations, EXPLAIN, INSERT/UPDATE/DELETE/MERGE, GRANT/SET, DDL (tables, views, indexes, sequences), and all expression nodes (literals, functions, CASE, CAST, windows, intervals, ...). Identifier quoting and literal/function rendering go through the dialect hooks (`ShouldQuoteIdentifier`, `RenderLiteral`, `RenderFunction`, `GetSetOperator`).
