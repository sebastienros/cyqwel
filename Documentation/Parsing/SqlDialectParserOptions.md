# Parsing / SqlDialectParserOptions.cs

## SqlIdentifierQuoteStyle (flags enum)
`None`, `DoubleQuote`, `Backtick`, `Brackets`: accepted identifier quoting.

## SqlParameterStyle (flags enum)
`QuestionMark` (`?`), `AtNamed` (`@p`), `ColonNamed` (`:p`), `DollarNamed` (`$p`), `DollarNumbered` (`$1`).

## SqlDoublePipeBehavior (enum)
How `||` is parsed: `Unsupported`, `Concatenate`, `LogicalOr` (MySQL).

## SqlCurrentTimestampSyntax (flags enum)
Accepted current-timestamp forms: `CurrentTimestampFunction`, `GetDate`, `Now`, `SysDate`, `UtcTimestamp`, `GetUtcDate`, `TimezoneUtc`, `SysExtractUtc`, `DateTimeUtc`.

## RoutineGrammar (internal flags enum)
Implementation detail selecting the procedural grammar: `Atomic`, `AtPrefixedBatch`, `DollarQuoted`, `Labeled`, `DeclarationFirst`, `All`.

## SqlDialectParserOptions (sealed record)
Describes the capabilities of the grammar built for a dialect. `Permissive` is the instance accepting everything and is used by `SqlDialect.ParserOptions` by default. Main properties:
- Syntax: `IdentifierQuotes`, `ParameterStyles`, `SupportsParameterDefaults`, `SupportsDoubleQuotedStrings`, `SupportsBackslashStringEscapes`, `SupportsNationalStringLiterals`/`Aliases`, `SupportsExpressionIntervalValues`, `DollarSignIsIdentifier`, `DoublePipeBehavior`, `CurrentTimestampSyntax`.
- Paging: `SupportsTop`, `SupportsLimit`, `SupportsLimitComma`, `SupportsOffsetOnly`, `SupportsOffsetFetch`.
- Features: `SupportsReturning`, `SupportsReturningInto`, `SupportsILike`, `SupportsNullOrdering`, `SupportsMinus`, `SupportsRecursiveCte`, `SupportsHierarchicalQueries`, `SupportsTableAliasAs`, `SupportsExplainOptions`, `SupportsCreateViewSecurity`, `SupportsOracleDataTypes`, `SupportsStoredProcedures`, `SupportsAnonymousProceduralBlocks`, `SupportsTSqlExtensions`, `SupportsDerivedTableColumnAliases`.

The record is used as a cache key for compiled parsers, so it must stay immutable.
