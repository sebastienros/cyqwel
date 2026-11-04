# Visitors / SqlTransforms.cs

## SqlTransforms (public static class)

Extension helpers for common AST edits (all return new nodes).
- `AddWhere(select, predicate)`: adds a predicate, combining with `AND` if a `WHERE` exists.
- `SetLimit(select, limit)`: sets the row limit.
- `RenameTable<T>(node, from, to)`: renames table references across the tree.
- `RenameColumn<T>(node, from, to)`: renames column references.

```csharp
var rewritten = select.AddWhere(Sql.Col("active").Eq(Sql.Lit(true))).SetLimit(100);
```
