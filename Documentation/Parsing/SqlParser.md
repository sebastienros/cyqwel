# Parsing / SqlParser.cs

## SqlParser (public static partial class)

Parses SQL into a `SqlDocument` using Parlot parser combinators. One reusable parser graph is built per `(SqlDialectParserOptions, RoutineGrammar)` pair and cached in a `ConcurrentDictionary<..., Lazy<Parser<SqlDocument>>>`; a permissive parser is built eagerly.

- `Parse(sql, dialect?, options?)`: returns a `SqlDocument`; throws `SqlParseException` on failure.
- `TryParse(sql, ..., out document, out error)`: exception-free variant.
- `CreateDocumentParser(...)` (private): declares keyword terms, then expression, query, DML, DDL and procedural grammars, filtered by the dialect capabilities.

Most callers use `SqlDialect.Parse` / `TryParse`, which delegate here. Input length and AST size are checked against `SqlParseOptions`.

The class is partial: `SqlParser.TSqlQueries.cs` adds the T-SQL query grammar.
