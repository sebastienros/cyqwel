# Builders / MutationBuilders.cs

Fluent builders for `INSERT`, `UPDATE`, `DELETE` and `MERGE`. All four are `partial`; their T-SQL-specific methods live in `TSqlMutationBuilders.cs`.

## InsertBuilder (sealed partial class)

Created by `Sql.InsertInto(table)`.
- `Columns(names)`, `Values(row)` (call repeatedly for several rows; values are coerced to literals), `From(query)` (`INSERT ... SELECT`).
- `Returning(...)`, `ReturningInto(...)`.
- `Build()` returns an `InsertStatement`; `ToSql(dialect?, options?)`.

## UpdateBuilder (sealed partial class)

Created by `Sql.Update(table, alias)`.
- `Set(column, value)`, `Where(predicate)`.
- `From(table | query | source)`: additional source (`UPDATE ... FROM`).
- `Returning`, `ReturningInto`, `Build()` (`UpdateStatement`), `ToSql`.

## DeleteBuilder (sealed partial class)

Created by `Sql.DeleteFrom(table, alias)`.
- `Where`, `Using(table | query | source)`, `Returning`, `ReturningInto`, `Build()` (`DeleteStatement`), `ToSql`.

## MergeBuilder (sealed partial class)

Created by `Sql.MergeInto(table, alias)`.
- `Using(...)`: source; `On(condition)`: join condition.
- `WhenMatchedUpdate(...)`, `WhenNotMatchedBySourceUpdate(...)`, `WhenNotMatchedInsert(...)`, `WhenMatchedDelete(condition?)`, `WhenNotMatchedBySourceDelete(condition?)`.
- `Returning`, `ReturningInto`, `Build()` (`MergeStatement`), `ToSql`.

## MutationBuilderValidation (internal static class)

`ValidateReturningInto(...)`: checks that `RETURNING ... INTO` is only used with a dialect/shape that allows it.

## CaseBuilder (sealed class)

Created by `Sql.Case(operand?)`.
- `When(condition, result)`, `Else(result)`, `Build()` returns a `CaseExpression`.

```csharp
Sql.Case().When(Sql.Col("age").LessThan(Sql.Lit(18)), "minor").Else("adult").Build();
```
