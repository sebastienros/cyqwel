# Visitors / SqlVisitor.cs

## SqlVisitor (abstract partial class)

Read-only visitor over the AST. `Visit(node)` (virtual) dispatches through `node.Accept(visitor)`, and each node type has a protected virtual `VisitXxx` method whose default implementation visits the node's children (via `SqlNodeChildren`). Override only the methods of interest.

Partials: `SqlVisitor.Standard.cs` (standard nodes), `SqlVisitor.Routines.cs` (procedural nodes), `SqlVisitor.TSqlMutations.cs` (T-SQL mutations).

```csharp
sealed class TableCollector : SqlVisitor
{
	public List<string> Names { get; } = new();
	protected override void VisitNamedTable(NamedTable node) { Names.Add(node.Name.ToString()); base.VisitNamedTable(node); }
}
```
