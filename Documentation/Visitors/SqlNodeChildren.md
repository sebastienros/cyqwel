# Visitors / SqlNodeChildren.cs

## SqlNodeChildren (internal static partial class)

`Get(node)` returns the direct child nodes of any AST node. It is the single source of truth for tree structure, used by `SqlVisitor` and `SqlTraversal`. Partial `SqlNodeChildren.Standard.cs` holds the per-node child enumerations for standard nodes.
