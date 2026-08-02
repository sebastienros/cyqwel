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
        DoublePipeBehavior = SqlDoublePipeBehavior.Concatenate,
    };

    public SqlIdentifierQuoteStyle IdentifierQuotes { get; init; } = SqlIdentifierQuoteStyle.DoubleQuote;

    public SqlParameterStyle ParameterStyles { get; init; } = SqlParameterStyle.QuestionMark;

    public bool SupportsParameterDefaults { get; init; }

    public bool SupportsDoubleQuotedStrings { get; init; }

    public bool SupportsBackslashStringEscapes { get; init; }

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

    public SqlDoublePipeBehavior DoublePipeBehavior { get; init; } = SqlDoublePipeBehavior.Concatenate;
}
