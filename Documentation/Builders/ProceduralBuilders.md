# Builders / ProceduralBuilders.cs

Shared base for builders of procedural code.

## ProceduralBuilder&lt;TBuilder&gt; (abstract class)

Curiously-recurring generic base (`where TBuilder : ProceduralBuilder<TBuilder>`) that accumulates a `ProceduralBlock` (`protected Block`).
- Variables: `Variable(...)` (several overloads), `Declare(variables)`, `TableVariable(name, elements)`, `SetVariable(name, value, operator)`.
- Statements: `Statement(statement)` (generic), `If(condition, then, else)`, `While(condition, statements)`, `Break`, `Continue`, `Return(value?)`.
- Utilities: `Print(value)`, `ExecuteSql(command)`, `Transaction(...)`, `SetOption(option, enabled)` (`NOCOUNT`, `XACT_ABORT`).
- `ToSql(dialect?, options?)`: generates SQL for the statement returned by the abstract `BuildStatement()`.

## ProceduralBlockBuilder (sealed class)

Created by `Sql.Block()`. `Build()` returns the `ProceduralBlock`; `BuildStatement()` returns the same block.

```csharp
Sql.Block()
   .Variable("total", new SqlDataType("INT"))
   .SetVariable("total", Sql.Lit(0))
   .Return(Sql.Local("total"))
   .ToSql(SqlDialects.TSql);
```
