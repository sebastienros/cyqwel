# Builders / ProcedureBuilders.cs

Builders for stored procedure definition, removal and invocation.

## ProcedureDefinitionBuilder&lt;TBuilder&gt; (abstract class)

Base of procedure definition builders; extends `ProceduralBuilder<TBuilder>`. Exposes protected `Name`, `Parameters`, `Body` and `Parameter(...)` overloads (a `ProcedureParameter`, or name + type + mode/default).

## CreateProcedureBuilder (sealed class)

`Sql.CreateProcedure(name)`. `Build()` returns a `CreateProcedureStatement`.

## ReplaceProcedureBuilder (sealed class)

`Sql.ReplaceProcedure(name)` (`CREATE OR REPLACE` semantics). `Build()` returns a `ReplaceProcedureStatement`.

## DropProcedureBuilder (sealed class)

`Sql.DropProcedure(name)`. `ParameterType(type, args)` (identifies overloads), `IfExists`. Builds a `DropProcedureStatement`.

## CallProcedureBuilder (sealed class)

`Sql.CallProcedure(name)`.
- `Argument(value, output)`, `NamedArgument(name, value, output)`: values may be `SqlExpression` or CLR values.
- `ReturnInto(name)`: captures the return value in a variable (T-SQL).
- Builds a `CallProcedureStatement`.
