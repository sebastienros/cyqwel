# Generation / SqlGenerator.RoutineRenderers.cs

Partial of `SqlGenerator` (see `SqlGenerator.md`).

## RoutineRenderer (internal abstract class)

Strategy base class that renders procedural constructs (procedure definitions, calls, blocks, variables) for a family of dialects. It is exposed by the dialect through `SqlDialect.RoutineRenderer` (ANSI, T-SQL, PostgreSQL, MySQL, Oracle variants). `SqlGenerator` delegates to it for every routine statement so that the generator itself stays dialect-neutral.
