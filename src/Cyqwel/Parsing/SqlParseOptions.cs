namespace Cyqwel.Parsing;

public sealed record SqlParseOptions
{
    public static SqlParseOptions Default { get; } = new();

    public int MaximumInputLength { get; init; } = 16 * 1024 * 1024;

    public int MaximumAstNodes { get; init; } = 1_000_000;
}

public enum SqlParseErrorCode
{
    Syntax,
    DialectIncompatible,
    InputTooLarge,
    AstTooLarge,
}

public sealed record SqlParseError(
    string Message,
    int Offset,
    int Line,
    int Column,
    SqlParseErrorCode Code = SqlParseErrorCode.Syntax);

public sealed class SqlParseException : Exception
{
    public SqlParseException(SqlParseError error)
        : base($"{error.Message} (line {error.Line}, column {error.Column})")
    {
        Error = error;
    }

    public SqlParseError Error { get; }
}
