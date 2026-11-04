# Cyqwel Code Documentation Index

Cyqwel is a .NET 8 library to **parse, build, validate, transform and generate SQL** across dialects. SQL text is parsed into a dialect-neutral AST, which can be rewritten, validated, and rendered back to SQL for a target dialect (transpilation).

Each page below documents one source file of `src/Cyqwel`, with the same folder layout.

## Where to start

| I want to... | Read |
|---|---|
| Build SQL with a fluent API | [Sql](Builders/Sql.md), then [SelectBuilder](Builders/SelectBuilder.md) |
| Parse SQL text | [SqlParser](Parsing/SqlParser.md), [SqlDialect](Dialects/SqlDialect.md) |
| Convert SQL from one dialect to another | [SqlDialect](Dialects/SqlDialect.md), [SqlGenerator](Generation/SqlGenerator.md) |
| Understand the AST | [SqlNode](Ast/SqlNode.md), [Expressions](Ast/Expressions.md), [Statements](Ast/Statements.md) |
| Inspect or modify an AST | [SqlTraversal](Visitors/SqlTraversal.md), [SqlTransforms](Visitors/SqlTransforms.md), [SqlRewriter](Visitors/SqlRewriter.md) |
| Validate SQL against a schema | [SqlValidator](Validation/SqlValidator.md), [SqlValidationModels](Validation/SqlValidationModels.md) |

## Processing pipeline

```
SQL text --(Parsing)--> AST (Ast) --(Visitors: rewrite/inspect)--> AST
						   ^                                         |
		Builders (fluent) -+                                         v
											  Generation --> SQL text (per Dialect)
						   Validation checks an AST against a schema catalog
```

Suggested reading order: **Ast → Dialects → Parsing → Generation → Builders → Visitors → Validation**.

---

## Ast: the syntax tree

| File | Content |
|---|---|
| [SqlNode](Ast/SqlNode.md) | Base types: source spans, `SqlNode`, `SqlStatement`, `SqlQuery`, `SqlExpression`, `SqlDocument`, `SqlBatch` |
| [Expressions](Ast/Expressions.md) | Expression nodes: identifiers, columns, literals, operators, functions, windows, casts, data types |
| [Statements](Ast/Statements.md) | SELECT, VALUES, set operations, EXPLAIN, INSERT/UPDATE/DELETE/MERGE, GRANT, SET |
| [Clauses](Ast/Clauses.md) | Table sources, joins, projections, ordering, CTEs, window frames, CONNECT BY, MERGE actions |
| [Ddl](Ast/Ddl.md) | CREATE/ALTER/DROP/TRUNCATE for tables, views, indexes, sequences, constraints |
| [Routines](Ast/Routines.md) | Procedural SQL: procedures, parameters, variables, blocks, IF/WHILE, transactions |
| [TSqlQueries](Ast/TSqlQueries.md) | T-SQL specifics: table hints, OPENJSON, PIVOT/UNPIVOT, TABLESAMPLE, query options, FOR JSON/XML |

## Dialects: target database flavors

| File | Content |
|---|---|
| [SqlDialect](Dialects/SqlDialect.md) | `SqlDialect` base class, built-in dialects (generic, T-SQL, SQLite, PostgreSQL, MySQL, Oracle), registry and dialect builder |

## Parsing: SQL text to AST

| File | Content |
|---|---|
| [SqlParser](Parsing/SqlParser.md) | Main Parlot-based parser: `Parse` / `TryParse` |
| [SqlParser.TSqlQueries](Parsing/SqlParser.TSqlQueries.md) | T-SQL query grammar extensions |
| [SqlParseOptions](Parsing/SqlParseOptions.md) | Input limits, error codes, `SqlParseError`, `SqlParseException` |
| [SqlDialectParserOptions](Parsing/SqlDialectParserOptions.md) | Per-dialect syntax capabilities (quotes, parameters, paging, features) |
| [TSqlScriptParsers](Parsing/TSqlScriptParsers.md) | `GO` batch separator and script splitting |

## Generation: AST to SQL text

| File | Content |
|---|---|
| [SqlGenerator](Generation/SqlGenerator.md) | Core generator and overview of its partial files |
| [SqlGenerationOptions](Generation/SqlGenerationOptions.md) | Pretty print, indentation, keyword/function casing, unsupported behavior |
| [SqlGenerator.Standard](Generation/SqlGenerator.Standard.md) | Standard SQL queries, DML, DDL, expressions |
| [SqlGenerator.Routines](Generation/SqlGenerator.Routines.md) | Procedural statements |
| [SqlGenerator.RoutineRenderers](Generation/SqlGenerator.RoutineRenderers.md) | Dialect-specific routine rendering strategy |
| [SqlGenerator.TSqlMutations](Generation/SqlGenerator.TSqlMutations.md) | T-SQL DML/DDL extras (OUTPUT, TOP...) |
| [SqlGenerator.TSqlQueries](Generation/SqlGenerator.TSqlQueries.md) | T-SQL query extras (hints, OPENJSON, PIVOT...) |

## Builders: fluent API

| File | Content |
|---|---|
| [Sql](Builders/Sql.md) | Static entry point and factory for all builders and expressions |
| [SelectBuilder](Builders/SelectBuilder.md) | `SelectBuilder` and `SetQueryBuilder` (UNION/INTERSECT/EXCEPT) |
| [QueryBuilders](Builders/QueryBuilders.md) | `ValuesBuilder`, `ExplainBuilder` |
| [MutationBuilders](Builders/MutationBuilders.md) | INSERT, UPDATE, DELETE, MERGE builders and `CaseBuilder` |
| [TSqlMutationBuilders](Builders/TSqlMutationBuilders.md) | T-SQL additions to the mutation builders |
| [DdlBuilders](Builders/DdlBuilders.md) | Table, view, index, sequence, drop and truncate builders |
| [CreateSchemaBuilder](Builders/CreateSchemaBuilder.md) | `CREATE SCHEMA` builder |
| [CreateInlineFunctionBuilder](Builders/CreateInlineFunctionBuilder.md) | T-SQL inline table-valued function builder |
| [ProceduralBuilders](Builders/ProceduralBuilders.md) | Base procedural builder and block builder |
| [ProcedureBuilders](Builders/ProcedureBuilders.md) | Create, replace, drop and call procedure builders |

## Visitors: traversing and rewriting the AST

| File | Content |
|---|---|
| [SqlTraversal](Visitors/SqlTraversal.md) | Search and enumeration helpers (descendants, `FindAll`, table/column names) |
| [SqlTransforms](Visitors/SqlTransforms.md) | Ready-made edits: `AddWhere`, `SetLimit`, `RenameTable`, `RenameColumn` |
| [SqlVisitor](Visitors/SqlVisitor.md) | Read-only visitor base class |
| [SqlVisitor.Standard](Visitors/SqlVisitor.Standard.md) | Visit methods for standard nodes |
| [SqlVisitor.Routines](Visitors/SqlVisitor.Routines.md) | Visit methods for procedural nodes |
| [SqlVisitor.TSqlMutations](Visitors/SqlVisitor.TSqlMutations.md) | Visit methods for T-SQL mutation nodes |
| [SqlRewriter](Visitors/SqlRewriter.md) | Rewriting visitor base class (returns new nodes) |
| [SqlRewriter.Standard](Visitors/SqlRewriter.Standard.md) | Rewrite methods for standard nodes |
| [SqlRewriter.Routines](Visitors/SqlRewriter.Routines.md) | Rewrite methods for procedural nodes |
| [SqlRewriter.TSqlMutations](Visitors/SqlRewriter.TSqlMutations.md) | Rewrite methods for T-SQL mutation nodes |
| [SqlRewriter.TSqlQueries](Visitors/SqlRewriter.TSqlQueries.md) | Rewrite methods for T-SQL query nodes |
| [SqlNodeChildren](Visitors/SqlNodeChildren.md) | Internal child enumeration shared by visitors and traversal |
| [SqlNodeChildren.Standard](Visitors/SqlNodeChildren.Standard.md) | Child enumeration for standard nodes |

## Validation: checks on parsed SQL

| File | Content |
|---|---|
| [SqlValidator](Validation/SqlValidator.md) | Public entry point `SqlValidator.Validate` |
| [SqlValidationModels](Validation/SqlValidationModels.md) | Options, results, diagnostics, schema catalog, type families, diagnostic codes |
| [SchemaValidationEngine](Validation/SchemaValidationEngine.md) | Internal engine: references, types, procedural checks, warnings |
| [SchemaValidationEngine.TSqlMutations](Validation/SchemaValidationEngine.TSqlMutations.md) | T-SQL mutation validation |

## Properties

| File | Content |
|---|---|
| [AssemblyInfo](Properties/AssemblyInfo.md) | `InternalsVisibleTo` for the test project |

## Conventions

- **Partial classes**: a type split across `Name.cs`, `Name.Standard.cs`, `Name.Routines.cs`, `Name.TSql*.cs` is documented on the base page, with one short page per partial.
- **Immutability**: AST nodes are records; transformations return new nodes.
- **Entry points**: `Sql` (build), `SqlDialect` (parse/generate), `SqlValidator` (validate).
