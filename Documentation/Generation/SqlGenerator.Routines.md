# Generation / SqlGenerator.Routines.cs

Partial of `SqlGenerator` (see `SqlGenerator.md`).

Contains the writers for procedural statements: `ProceduralBlock`, `ProceduralIfStatement`, `ProceduralWhileStatement`, `BREAK`/`CONTINUE` (loop labels tracked via `_loopLabels` and `_loopCounter`), `RETURN`, procedure create/replace/drop/call, `DECLARE`, `SET @var`, `PRINT`, `EXEC`, transactions. Tracks `_proceduralContextDepth` to know whether statements are inside a routine body, and calls the dialect's `RoutineRenderer` for dialect-specific syntax.
