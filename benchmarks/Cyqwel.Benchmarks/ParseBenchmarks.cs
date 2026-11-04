using BenchmarkDotNet.Attributes;
using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Parsing;

namespace Cyqwel.Benchmarks;

/// <summary>
/// Measures parsing with a grammar which is already built, the cost of most calls.
/// </summary>
[MemoryDiagnoser]
public class ParseBenchmarks
{
    private SqlQuery _query = null!;

    public static IEnumerable<string> Queries => SqlQueries.All.Keys;

    [ParamsSource(nameof(Queries))]
    public string Query { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        _query = SqlQueries.All[Query];

        // A query which stops parsing would make the benchmark measure the failure path.
        SqlParser.Parse(_query.Sql, _query.Dialect);
    }

    [Benchmark]
    public SqlDocument Parse() => SqlParser.Parse(_query.Sql, _query.Dialect);
}

/// <summary>
/// Measures rejected input, where a failed dialect parser is followed by the permissive one.
/// </summary>
[MemoryDiagnoser]
public class ParseFailureBenchmarks
{
    private const string Invalid = "SELECT id, name FROM users WHERE id = 1 AND ORDER BY";

    [GlobalSetup]
    public void Setup()
    {
        if (SqlParser.TryParse(Invalid, SqlDialects.TSql, out _, out _))
        {
            throw new InvalidOperationException("The query must be rejected.");
        }
    }

    [Benchmark]
    public bool TryParseInvalid() => SqlParser.TryParse(Invalid, SqlDialects.TSql, out _, out _);
}
