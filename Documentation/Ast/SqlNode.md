# Ast / SqlNode.cs

Root types of the AST and the fluent expression helpers.

## SqlTextSpan (readonly record struct)

Location (`Start`, `Length`) of a node in the parsed source. `SqlTextSpan.None` (`Start = -1`) means "no location"; `IsEmpty` is true in that case.

## SqlNode (abstract record)

Base type of every node.
- `Span`: source location (set by the parser).
- `Accept(SqlVisitor)`: read-only traversal.
- `Accept(SqlRewriter)`: returns a transformed node.
- `ToSql(dialect?, options?)`: generates SQL (generic dialect by default).

```csharp
string sql = node.ToSql(SqlDialects.PostgreSql);
```

## SqlStatement (abstract record)

Base type of statements. Carries optional T-SQL `QueryOptions` (`OPTION (...)`).

## SqlQuery (abstract record)

Base type of statements that return rows (`SELECT`, `VALUES`, set operations). Carries the optional `ResultFormat` (`FOR JSON` / `FOR XML`).

## SqlExpression (abstract record)

Base type of expressions with a fluent API to compose predicates and operators:
- Comparisons: `EqualTo`, `NotEqualTo`, `GreaterThan`, `GreaterThanOrEqualTo`, `LessThan`, `LessThanOrEqualTo`.
- Logic and arithmetic: `And`, `Or`, `Add`, `Subtract`, `Multiply`, `Divide`, `Modulo`, `Concatenate`, `BitwiseAnd/Or/Xor`.
- Patterns: `Like`, `NotLike`, `ILike`, `NotILike`.
- Unary: `Positive`, `Negate`, `Not`, `BitwiseNot`, `Prior`, `ConnectByRoot`.
- Tests: `IsNull`, `IsNotNull`, `IsTrue`, `IsNotTrue`, `IsFalse`, `IsNotFalse`, `IsUnknown`, `IsNotUnknown`, `IsDistinctFrom`, `IsNotDistinctFrom`.
- Ranges and sets: `Between`, `NotBetween`, `In` / `NotIn` (value list, must not be empty, or subquery).
- `Collate(collation)`.

## SqlDocument (sealed record)

Result of parsing: the list of `Statements`, plus optional `Batches` (T-SQL `GO`-separated). Has a `params SqlStatement[]` constructor.

## SqlBatch (sealed record)

A group of statements with an `IsTerminated` flag (batch ended by a separator).


