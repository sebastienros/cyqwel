# Validation / SchemaValidationEngine.cs

## SchemaValidationEngine (internal sealed partial class)

Walks a `SqlDocument` with the supplied catalog and options and collects diagnostics.
- Constructor receives catalog, options, dialect and a diagnostics sink.
- `Validate(document)`: entry point.
- Checks: table/column/function existence and arity, scalar subqueries, ambiguous or unresolved references, type compatibility (comparisons, arithmetic, assignments, set operations), CTE column counts, foreign keys, procedural symbols and loop control, plus style warnings (`SELECT *`, cartesian join, `LIMIT` without `ORDER BY`).
