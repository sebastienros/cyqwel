using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Parsing;
using Parlot.Fluent;

namespace Cyqwel.Benchmarks;

/// <summary>
/// Measures what a process pays once per dialect: building the grammar, then its first parse, which is
/// where a grammar finishes its lazy initialization.
/// </summary>
/// <remarks>
/// The grammar is private and cached, so it is invoked through reflection to build a new one each time. The
/// reflection call is negligible next to what it measures.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 3, warmupCount: 2, iterationCount: 10)]
public class ColdStartBenchmarks
{
    private readonly MethodInfo _create = typeof(SqlParser).GetMethod(
        "CreateDocumentParser",
        BindingFlags.NonPublic | BindingFlags.Static)!;

    private object[] _arguments = [];
    private string _sql = "";

    public static IEnumerable<string> Queries => ["Joins", "TSqlBatch"];

    [ParamsSource(nameof(Queries))]
    public string Query { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        var query = SqlQueries.All[Query];
        var routineGrammar = typeof(SqlDialect)
            .GetProperty("RoutineGrammar", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(query.Dialect)!;
        _arguments = [query.Dialect.ParserOptions, routineGrammar];
        _sql = query.Sql;
    }

    [Benchmark]
    public SqlDocument? BuildAndParseFirst()
    {
        var parser = (Parser<SqlDocument>)_create.Invoke(null, _arguments)!;
        return parser.TryParse(_sql, out var document, out _)
            ? document
            : throw new InvalidOperationException("The query must parse.");
    }
}
