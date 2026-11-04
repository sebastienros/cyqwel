# Generation / SqlGenerator.cs

## SqlGenerator (sealed partial class)

Converts an AST (`SqlNode`) into SQL text for a given `SqlDialect` and `SqlGenerationOptions`. The class is split across several files by feature area:

| File | Content |
|---|---|
| `SqlGenerator.cs` | Core: constructor, `Generate`, root dispatch (`WriteNode`), documents, batches, queries, DML, DDL, expressions, formatting helpers |
| `SqlGenerator.Standard.cs` | Standard SQL constructs |
| `SqlGenerator.Routines.cs` | Procedural code (blocks, IF/WHILE, procedures) |
| `SqlGenerator.RoutineRenderers.cs` | Dialect-specific routine rendering strategies |
| `SqlGenerator.TSqlMutations.cs` | T-SQL DML/DDL extras (OUTPUT, TOP, ...) |
| `SqlGenerator.TSqlQueries.cs` | T-SQL query extras (hints, OPENJSON, PIVOT, ...) |

**Public API**
- `SqlGenerator(dialect, options?)`: throws `ArgumentNullException` if the dialect is null; options default to `SqlGenerationOptions.Default`.
- `Generate(node)`: renders any root node (document, batch, statement, expression, table source, T-SQL option/hint/format). Unsupported root nodes raise `NotSupportedException`. A pooled `StringBuilder` is used and internal state (indentation, loop labels) is reset after each call. Query result formats and query options are appended after the statement.

**Behavior notes**
- A `SqlDocument` with batches renders each batch separated by a newline; unterminated non-final batches get a terminator. Without batches, T-SQL procedure statements are separated by `GO`.
- The generator is not thread-safe: use one instance per call (as `SqlDialect.Generate` does).

Most callers use `node.ToSql(dialect, options)` or `dialect.Generate(node, options)` rather than the generator directly.
