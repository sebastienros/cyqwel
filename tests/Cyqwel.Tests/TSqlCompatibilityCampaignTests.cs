using System.Text.Json;
using Cyqwel.TSqlCompatibility;

namespace Cyqwel.Tests;

public class TSqlCompatibilityCampaignTests
{
    [Fact]
    public void Supported_query_has_a_stable_reference_round_trip()
    {
        var result = new CampaignRunner().Evaluate(Input("SELECT 1"));

        Assert.Equal(CaseOutcome.RoundTripStable, result.Outcome);
        Assert.True(result.ReferenceAccepted);
        Assert.True(result.Eligible);
        Assert.True(result.CyqwelParsed);
        Assert.Empty(result.ReferenceErrors!);
        Assert.Empty(result.GeneratedReferenceErrors!);
        Assert.Equal(result.GeneratedSql, result.RegeneratedSql);
        Assert.Equal(result.ReferenceSql, result.GeneratedReferenceSql);
    }

    [Fact]
    public void Reference_rejections_are_not_reported_as_Cyqwel_gaps()
    {
        var result = new CampaignRunner().Evaluate(Input("SELECT FROM"));

        Assert.Equal(CaseOutcome.ReferenceRejected, result.Outcome);
        Assert.False(result.ReferenceAccepted);
        Assert.False(result.Eligible);
        Assert.Null(result.CyqwelParsed);
        Assert.NotEmpty(result.ReferenceErrors!);
    }

    [Theory]
    [InlineData("CREATE PROCEDURE dbo.p")]
    [InlineData("SELECT * FROM\nCREATE )\nCREATE TABLE t (id INT)")]
    public void Invalid_inputs_with_no_reference_tree_keep_their_diagnostics(string sql)
    {
        var result = new CampaignRunner().Evaluate(Input(sql));

        Assert.Equal(CaseOutcome.ReferenceRejected, result.Outcome);
        Assert.False(result.Eligible);
        Assert.NotEmpty(result.ReferenceErrors!);
        Assert.Null(result.ExceptionType);
    }

    [Fact]
    public void Reference_valid_unsupported_syntax_keeps_parse_diagnostics()
    {
        var result = new CampaignRunner().Evaluate(Input("CREATE LOGIN sample WITH PASSWORD = 'ExampleOnly123!'"));

        Assert.Equal(CaseOutcome.ParseRejected, result.Outcome);
        Assert.True(result.ReferenceAccepted);
        Assert.True(result.Eligible);
        Assert.False(result.CyqwelParsed);
        Assert.NotNull(result.ParseError);
    }

    [Fact]
    public void Assignment_style_alias_is_checked_even_when_generated_SQL_is_stable()
    {
        var result = new CampaignRunner().Evaluate(Input("SELECT answer = 42"));

        Assert.Equal(CaseOutcome.RoundTripStable, result.Outcome);
        Assert.Equal(result.GeneratedSql, result.RegeneratedSql);
        Assert.Equal(result.ReferenceSql, result.GeneratedReferenceSql);
        Assert.Empty(result.AstMismatches!);
    }

    [Fact]
    public void Variable_assignment_is_not_confused_with_an_ordinary_projection()
    {
        var result = new CampaignRunner().Evaluate(Input("SELECT @answer = 42"));

        Assert.Equal(CaseOutcome.RoundTripStable, result.Outcome);
        Assert.Empty(result.AstMismatches!);
    }

    [Fact]
    public void Updated_main_preserves_national_strings()
    {
        var result = new CampaignRunner().Evaluate(Input("SELECT N'hello' AS [value]"));

        Assert.Equal(CaseOutcome.RoundTripStable, result.Outcome);
        Assert.Contains("N'hello'", result.GeneratedSql);
        Assert.Empty(result.AstMismatches!);
    }

    [Theory]
    [InlineData("ALTER TABLE dbo.t ADD c INT")]
    [InlineData("CREATE TABLE dbo.t (id INT IDENTITY)")]
    [InlineData("SET IDENTITY_INSERT dbo.t ON")]
    [InlineData("SET STATISTICS TIME ON")]
    public void Native_generation_preserves_valid_TSql(string sql)
    {
        var result = new CampaignRunner().Evaluate(Input(sql));

        Assert.True(result.ReferenceAccepted);
        Assert.True(result.CyqwelParsed);
        Assert.Empty(result.ReferenceErrors!);
        Assert.Empty(result.GeneratedReferenceErrors!);
        Assert.Equal(result.GeneratedSql, result.RegeneratedSql);
    }

    [Fact]
    public void Batch_and_statement_denominators_are_separate_and_preserve_original_text()
    {
        var input = Input("-- header\nSELECT 1;\nGO\nSELECT 2;") with { Line = 10 };
        var report = new CampaignRunner().Run(Corpus(input), "test-revision");

        Assert.Equal(3, report.Results.Count);
        Assert.True(report.Results[0].CyqwelParsed);
        var statements = report.Results.Where(result => result.Scope == "statement").ToArray();
        Assert.Equal(2, statements.Length);
        Assert.All(statements, result =>
        {
            Assert.Equal(CaseOutcome.RoundTripStable, result.Outcome);
            Assert.Equal(input.Sql.Substring(result.SqlOffset, result.Sql.Length), result.Sql);
            Assert.Equal(input.Line + result.SqlLine - 1, result.SourceLine);
        });
        Assert.Equal(11, statements[0].SourceLine);
        Assert.Equal(13, statements[1].SourceLine);
        Assert.Equal(1, report.Summary.Single(summary => summary.Scope == "input").Eligible);
        Assert.Equal(2, report.Summary.Single(summary => summary.Scope == "statement").Eligible);
    }

    [Fact]
    public void Python_test_locations_are_not_offset_by_lines_inside_SQL_literals()
    {
        var input = Input("\nSELECT 1;\nSELECT 2;") with { Source = "sqlglot", Line = 30 };
        var report = new CampaignRunner().Run(Corpus(input), "test-revision");

        Assert.All(report.Results, result => Assert.Equal(30, result.SourceLine));
        Assert.Equal(new[] { 2, 3 }, report.Results
            .Where(result => result.Scope == "statement").Select(result => result.SqlLine));
    }

    [Fact]
    public void Statement_boundaries_preserve_closing_tokens_missing_from_reference_spans()
    {
        var input = Input(
            "TRUNCATE TABLE t WITH (PARTITIONS(1, 2 TO 5));\n-- GO is a comment\nGO\nSELECT 'GO' AS marker;");
        var report = new CampaignRunner().Run(Corpus(input), "test-revision");
        var statements = report.Results.Where(result => result.Scope == "statement").ToArray();

        Assert.Equal(2, statements.Length);
        Assert.All(statements, result => Assert.True(result.ReferenceAccepted));
        Assert.Contains("PARTITIONS(1, 2 TO 5))", statements[0].Sql);
        Assert.Contains("-- GO is a comment", statements[0].Sql);
        Assert.Equal("SELECT 'GO' AS marker;", statements[1].Sql);
        Assert.Equal(CaseOutcome.RoundTripStable, statements[1].Outcome);
    }

    [Fact]
    public void Invalid_scripts_do_not_silently_contribute_recovered_statements()
    {
        var report = new CampaignRunner().Run(
            Corpus(Input("SELECT 1;\nSELECT FROM;")), "test-revision");

        Assert.Equal(CaseOutcome.ReferenceRejected, Assert.Single(report.Results).Outcome);
        Assert.Equal(0, Assert.Single(report.Summary).Eligible);
    }

    [Fact]
    public void Empty_reference_scripts_are_counted_but_not_eligible()
    {
        var report = new CampaignRunner().Run(Corpus(Input("-- comment\nGO\n")), "test-revision");
        var result = Assert.Single(report.Results);

        Assert.Equal(CaseOutcome.ReferenceEmpty, result.Outcome);
        Assert.True(result.ReferenceAccepted);
        Assert.False(result.Eligible);
        Assert.Null(result.CyqwelParsed);
    }

    [Fact]
    public void Stored_procedure_bodies_are_not_split_into_unrelated_top_level_cases()
    {
        var report = new CampaignRunner().Run(
            Corpus(Input("CREATE PROCEDURE p AS BEGIN SELECT 1; SELECT 2; END;\nGO\nSELECT 3;")),
            "test-revision");
        var statements = report.Results.Where(result => result.Scope == "statement").ToArray();

        Assert.Equal(2, statements.Length);
        Assert.Equal("CreateProcedureStatement", statements[0].StatementType);
        Assert.Contains("SELECT 1; SELECT 2;", statements[0].Sql);
        Assert.Equal("SelectStatement", statements[1].StatementType);
    }

    [Fact]
    public void Quoted_identifier_context_changes_are_excluded_from_statement_comparisons()
    {
        var report = new CampaignRunner().Run(
            Corpus(Input("SET QUOTED_IDENTIFIER OFF;\nSELECT \"literal\";")), "test-revision");
        var statement = report.Results.Last();

        Assert.Equal(CaseOutcome.ReferenceContextChanged, statement.Outcome);
        Assert.True(statement.ReferenceAccepted);
        Assert.False(statement.Eligible);
        Assert.Null(statement.CyqwelParsed);
    }

    [Fact]
    public void Corpus_provenance_and_unique_IDs_are_required()
    {
        var input = Input("SELECT 1");
        var runner = new CampaignRunner();

        Assert.Throws<ArgumentException>(() => runner.Run(Corpus(input, input), "test-revision"));
        Assert.Throws<ArgumentException>(() => runner.Run(Corpus(), "test-revision"));
        Assert.Throws<ArgumentException>(() => runner.Run(
            Corpus(input) with { SchemaVersion = 2 }, "test-revision"));
        Assert.Throws<ArgumentException>(() => runner.Run(
            Corpus(input with { Source = "unknown" }), "test-revision"));
    }

    [Fact]
    public void Report_round_trips_with_exclusions_and_machine_readable_outcomes()
    {
        using var document = JsonDocument.Parse("""{"source":"sqlglot","reason":"dynamic expression"}""");
        var corpus = Corpus(Input("SELECT 1")) with { Exclusions = [document.RootElement.Clone()] };
        var report = new CampaignRunner().Run(corpus, "test-revision");
        var json = JsonSerializer.Serialize(report, CampaignJson.Options);
        var restored = JsonSerializer.Deserialize<CampaignReport>(json, CampaignJson.Options)!;

        Assert.Contains("\"round-trip-stable\"", json);
        Assert.Equal("test-revision", restored.CyqwelRevision);
        Assert.Equal("Sql180", restored.ParserVersion);
        Assert.True(restored.QuotedIdentifiers);
        Assert.Equal(2, restored.Results.Count);
        Assert.Equal("dynamic expression", Assert.Single(restored.ExtractionExclusions)
            .GetProperty("reason").GetString());
    }

    private static CorpusCase Input(string sql) => new()
    {
        Id = "case-1",
        Source = "scriptdom",
        Path = "Test/SqlDom/TestScripts/example.sql",
        Line = 1,
        Sql = sql,
        Context = "campaign-test",
    };

    private static Corpus Corpus(params CorpusCase[] cases) => new()
    {
        SchemaVersion = 1,
        Sources =
        [
            new("scriptdom", "microsoft/SqlScriptDOM", new string('a', 40), "MIT", "LICENSE"),
            new("sqlglot", "tobymao/sqlglot", new string('b', 40), "MIT", "LICENSE"),
        ],
        Cases = cases,
        Exclusions = [],
    };
}
