namespace Cyqwel.Parsing;

[Flags]
public enum SqlIdentifierQuoteStyle
{
    None = 0,
    DoubleQuote = 1,
    Backtick = 2,
    Brackets = 4,
}

[Flags]
public enum SqlParameterStyle
{
    None = 0,
    QuestionMark = 1,
    AtNamed = 2,
    ColonNamed = 4,
    DollarNamed = 8,
    DollarNumbered = 16,
}

public enum SqlDoublePipeBehavior
{
    Unsupported,
    Concatenate,
    LogicalOr,
}

[Flags]
public enum SqlCurrentTimestampSyntax
{
    None = 0,
    CurrentTimestampFunction = 1,
    GetDate = 2,
    Now = 4,
    SysDate = 8,
    UtcTimestamp = 16,
    GetUtcDate = 32,
    TimezoneUtc = 64,
    SysExtractUtc = 128,
    DateTimeUtc = 256,
}

// Parser implementation detail. Public callers select a dialect and capabilities,
// not a procedural vendor/style taxonomy.
[Flags]
internal enum RoutineGrammar
{
    None = 0,
    Atomic = 1,
    AtPrefixedBatch = 2,
    DollarQuoted = 4,
    Labeled = 8,
    DeclarationFirst = 16,
    All = Atomic | AtPrefixedBatch | DollarQuoted | Labeled | DeclarationFirst,
}

/// <summary>
/// Configures the reusable Parlot grammar created for a SQL dialect.
/// </summary>
public sealed record SqlDialectParserOptions
{
    public static SqlDialectParserOptions Permissive { get; } = new()
    {
        IdentifierQuotes = SqlIdentifierQuoteStyle.DoubleQuote
            | SqlIdentifierQuoteStyle.Backtick
            | SqlIdentifierQuoteStyle.Brackets,
        ParameterStyles = SqlParameterStyle.QuestionMark
            | SqlParameterStyle.AtNamed
            | SqlParameterStyle.ColonNamed
            | SqlParameterStyle.DollarNamed
            | SqlParameterStyle.DollarNumbered,
        SupportsBackslashStringEscapes = true,
        SupportsNationalStringLiterals = true,
        SupportsTop = true,
        SupportsLimit = true,
        SupportsLimitComma = true,
        SupportsOffsetOnly = true,
        SupportsOffsetFetch = true,
        SupportsReturning = true,
        SupportsReturningInto = true,
        SupportsILike = true,
        SupportsNullOrdering = true,
        SupportsMinus = true,
        SupportsRecursiveCte = true,
        SupportsHierarchicalQueries = true,
        SupportsExplainOptions = true,
        SupportsCreateViewSecurity = true,
        SupportsOracleDataTypes = true,
        SupportsStoredProcedures = true,
        SupportsAnonymousProceduralBlocks = true,
        CurrentTimestampSyntax = SqlCurrentTimestampSyntax.CurrentTimestampFunction
            | SqlCurrentTimestampSyntax.GetDate
            | SqlCurrentTimestampSyntax.Now
            | SqlCurrentTimestampSyntax.SysDate
            | SqlCurrentTimestampSyntax.UtcTimestamp
            | SqlCurrentTimestampSyntax.GetUtcDate
            | SqlCurrentTimestampSyntax.TimezoneUtc
            | SqlCurrentTimestampSyntax.SysExtractUtc
            | SqlCurrentTimestampSyntax.DateTimeUtc,
        DoublePipeBehavior = SqlDoublePipeBehavior.Concatenate,
    };

    public SqlIdentifierQuoteStyle IdentifierQuotes { get; init; } = SqlIdentifierQuoteStyle.DoubleQuote;

    public SqlParameterStyle ParameterStyles { get; init; } = SqlParameterStyle.QuestionMark;

    public bool SupportsParameterDefaults { get; init; }

    public bool SupportsDoubleQuotedStrings { get; init; }

    public bool SupportsBackslashStringEscapes { get; init; }

    public bool SupportsNationalStringLiterals { get; init; }

    public bool DollarSignIsIdentifier { get; init; }

    public bool SupportsTop { get; init; }

    public bool SupportsLimit { get; init; } = true;

    public bool SupportsLimitComma { get; init; }

    public bool SupportsOffsetOnly { get; init; } = true;

    public bool SupportsOffsetFetch { get; init; }

    public bool SupportsReturning { get; init; }

    public bool SupportsReturningInto { get; init; }

    public bool SupportsILike { get; init; }

    public bool SupportsNullOrdering { get; init; } = true;

    public bool SupportsMinus { get; init; }

    public bool SupportsRecursiveCte { get; init; } = true;

    public bool SupportsHierarchicalQueries { get; init; }

    public bool SupportsTableAliasAs { get; init; } = true;

    public bool SupportsExplainOptions { get; init; }

    public bool SupportsCreateViewSecurity { get; init; }

    public bool SupportsOracleDataTypes { get; init; }

    public bool SupportsStoredProcedures { get; init; }

    public bool SupportsAnonymousProceduralBlocks { get; init; }

    /// <summary>
    /// Additional current-timestamp spellings. Bare CURRENT_TIMESTAMP is always supported.
    /// </summary>
    public SqlCurrentTimestampSyntax CurrentTimestampSyntax { get; init; }

    public SqlDoublePipeBehavior DoublePipeBehavior { get; init; } = SqlDoublePipeBehavior.Concatenate;
}
