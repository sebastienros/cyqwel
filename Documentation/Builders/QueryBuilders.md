# Builders / QueryBuilders.cs

Builders for `VALUES` queries and `EXPLAIN`.

## ValuesBuilder (sealed class)

Created by `Sql.Values(...)`. Builds a `ValuesStatement`.
- `Row(values)`: adds another row.
- `OrderBy`, `ThenBy`, `Limit`, `Offset`: ordering and paging.
- `With(...)`, `Recursive()`: CTE support.
- `Union`, `Intersect`, `Except`: returns a `SetQueryBuilder`.
- `Explain(analyze)`, `Build()`, `ToSql(dialect?, options?)`.

```csharp
Sql.Values(1, "a").Row(2, "b").ToSql();
```

## ExplainBuilder (sealed class)

Wraps a query into an `ExplainStatement`.
- `Analyze(value)`: `EXPLAIN ANALYZE`.
- `Parenthesized(value)`: wraps the query in parentheses.
- `Build()`, `ToSql(dialect?, options?)`.
