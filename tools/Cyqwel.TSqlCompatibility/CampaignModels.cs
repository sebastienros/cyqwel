using System.Text.Json;
using System.Text.Json.Serialization;
using Cyqwel.Parsing;

namespace Cyqwel.TSqlCompatibility;

public sealed record CorpusSource(
    string Source,
    string Repository,
    string Revision,
    string License,
    string LicensePath);

public sealed record CorpusCase
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required string Path { get; init; }
    public required int Line { get; init; }
    public required string Sql { get; init; }
    public required string Context { get; init; }
}

public sealed record Corpus
{
    public int SchemaVersion { get; init; }
    public required IReadOnlyList<CorpusSource> Sources { get; init; }
    public required IReadOnlyList<CorpusCase> Cases { get; init; }
    public required IReadOnlyList<JsonElement> Exclusions { get; init; }
}

public enum CaseOutcome
{
    ReferenceRejected,
    ReferenceEmpty,
    ReferenceContextChanged,
    ParseRejected,
    GenerationUnsupported,
    GeneratedReferenceRejected,
    ReparseRejected,
    RoundTripChanged,
    AstMismatch,
    ReferenceChanged,
    RoundTripStable,
    Exception,
}

public sealed record ReferenceDiagnostic(int Number, string Message, int Offset, int Line, int Column);

public sealed record AstMismatch(string Check, string Expected, string Actual);

public sealed record CaseResult
{
    public required string Id { get; init; }
    public required string CaseId { get; init; }
    public required string Source { get; init; }
    public required string Path { get; init; }
    public required int SourceLine { get; init; }
    public required string Context { get; init; }
    public required string Scope { get; init; }
    public required string Sql { get; init; }
    public int SqlOffset { get; init; }
    public int SqlLine { get; init; } = 1;
    public string? StatementType { get; set; }
    public CaseOutcome Outcome { get; set; }
    public bool ReferenceAccepted { get; set; }
    public bool Eligible { get; set; }
    public bool? CyqwelParsed { get; set; }
    public string Stage { get; set; } = "reference-parse";
    public IReadOnlyList<ReferenceDiagnostic>? ReferenceErrors { get; set; }
    public SqlParseError? ParseError { get; set; }
    public string? GeneratedSql { get; set; }
    public string? RegeneratedSql { get; set; }
    public string? ReferenceSql { get; set; }
    public string? GeneratedReferenceSql { get; set; }
    public IReadOnlyList<ReferenceDiagnostic>? GeneratedReferenceErrors { get; set; }
    public IReadOnlyList<AstMismatch>? AstMismatches { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
}

public sealed record CampaignSummary(
    string Source,
    string Scope,
    int Candidates,
    int Eligible,
    int Parsed,
    IReadOnlyDictionary<string, int> Outcomes);

public sealed record CampaignReport(
    int SchemaVersion,
    string CyqwelRevision,
    string ScriptDomVersion,
    string ParserVersion,
    bool QuotedIdentifiers,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<CorpusSource> Sources,
    IReadOnlyList<JsonElement> ExtractionExclusions,
    IReadOnlyList<CampaignSummary> Summary,
    IReadOnlyList<CaseResult> Results)
{
    public string? CaseFilter { get; init; }
}

public static class CampaignJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public static string OutcomeName(CaseOutcome outcome) =>
        JsonNamingPolicy.KebabCaseLower.ConvertName(outcome.ToString());
}
