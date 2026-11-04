# Visitors / SqlRewriter.cs

## SqlRewriter (abstract partial class)

Visitor that returns nodes, used to transform an immutable AST. `Visit(node)` dispatches to `VisitXxx` methods (protected virtual, returning `SqlNode`). Defaults rebuild a node only if a child changed (records `with` expressions), otherwise return the original instance. `Visit<T>(node)` is the typed shortcut.

Partials: `SqlRewriter.Standard.cs`, `SqlRewriter.Routines.cs`, `SqlRewriter.TSqlMutations.cs`, `SqlRewriter.TSqlQueries.cs`.

Used by `SqlDialect` (the nested `DialectRewriter`) and by `SqlTransforms`.
