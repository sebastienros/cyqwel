# Builders / TSqlMutationBuilders.cs

T-SQL extensions of the mutation builders (second part of the partial classes in `MutationBuilders.cs`).

## InsertBuilder (sealed partial class)
- `With(cte)`: common table expression.
- `DefaultValues()`: `INSERT ... DEFAULT VALUES`.
- `Top(value, percent)`: `INSERT TOP (n) [PERCENT]`.
- `Output(clause)`: `OUTPUT` clause (`TSqlOutputClause`).
- `Option(options)`: `OPTION (...)` query hints.

## UpdateBuilder (sealed partial class)
- `With(cte)`, `Top(value, percent)`, `Output(clause)`, `Option(options)`.
- `Set(column, operator, value)`: compound assignment (`+=`, `-=`, ...).

## DeleteBuilder (sealed partial class)
- `With(cte)`, `Top(value, percent)`, `Output(clause)`, `Option(options)`.
- `From(source)`: `DELETE ... FROM source`.

## MergeBuilder (sealed partial class)
- `With(cte)`, `Top(value, percent)`, `Output(clause)`, `Option(options)`.
