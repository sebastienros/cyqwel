# Visitors / SqlTraversal.cs

## SqlTraversal (public static class)

Read-only query helpers over any `SqlNode`.
- `DescendantsAndSelf`, `Descendants`: depth-first enumeration.
- `BreadthFirst`: breadth-first enumeration.
- `FindAll<TNode>()`, `Contains<TNode>()`: typed search.
- `GetTableNames()`, `GetColumnNames()`: names referenced in the tree.

```csharp
var tables = SqlDialects.Generic.Parse("SELECT * FROM a JOIN b ON a.id = b.id").GetTableNames();
```
