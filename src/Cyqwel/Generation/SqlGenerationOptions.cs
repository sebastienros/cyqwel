namespace Cyqwel.Generation;

public enum UnsupportedSqlBehavior
{
    Throw,
    Ignore,
}

public enum FunctionNameCase
{
    Preserve,
    Upper,
    Lower,
}

public sealed record SqlGenerationOptions
{
    public static SqlGenerationOptions Default { get; } = new();

    public bool PrettyPrint { get; init; }

    public int IndentSize { get; init; } = 2;

    public bool UppercaseKeywords { get; init; } = true;

    public FunctionNameCase FunctionNameCase { get; init; } = FunctionNameCase.Upper;

    public UnsupportedSqlBehavior UnsupportedBehavior { get; init; } = UnsupportedSqlBehavior.Throw;
}
