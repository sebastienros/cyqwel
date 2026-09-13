using System.Reflection;
using Cyqwel.Ast;
using Cyqwel.Dialects;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using CyqwelSelect = Cyqwel.Ast.SelectStatement;
using ReferenceSelect = Microsoft.SqlServer.TransactSql.ScriptDom.SelectStatement;

namespace Cyqwel.TSqlCompatibility;

public sealed class CampaignRunner
{
    public const SqlVersion ParserVersion = SqlVersion.Sql180;

    public CampaignReport Run(
        Corpus corpus,
        string cyqwelRevision,
        Action<CaseResult>? onResult = null)
    {
        ValidateCorpus(corpus);
        ArgumentException.ThrowIfNullOrWhiteSpace(cyqwelRevision);
        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<CaseResult>();

        foreach (var input in corpus.Cases)
        {
            var result = NewResult(input, "input", input.Sql);
            var script = Evaluate(result);
            Add(result);
            if (!result.ReferenceAccepted || script is null)
            {
                continue;
            }

            var statements = script.Batches.SelectMany(batch => batch.Statements).ToArray();
            for (var statementIndex = 0; statementIndex < statements.Length; statementIndex++)
            {
                var statement = statements[statementIndex];
                var end = statementIndex + 1 < statements.Length
                    ? statements[statementIndex + 1].StartOffset
                    : input.Sql.Length;

                // Some ScriptDom spans omit closing tokens, notably TRUNCATE ... PARTITIONS.
                // Bound original text by the next statement or a real GO token instead.
                for (var tokenIndex = statement.LastTokenIndex + 1;
                     tokenIndex < script.ScriptTokenStream.Count; tokenIndex++)
                {
                    var token = script.ScriptTokenStream[tokenIndex];
                    if (token.Offset >= end)
                    {
                        break;
                    }

                    if (token.TokenType == TSqlTokenType.Go)
                    {
                        end = token.Offset;
                        break;
                    }
                }

                while (end > statement.StartOffset && char.IsWhiteSpace(input.Sql[end - 1]))
                {
                    end--;
                }

                var sql = input.Sql.Substring(statement.StartOffset, end - statement.StartOffset);
                var unit = NewResult(input, "statement", sql) with
                {
                    Id = $"{input.Id}#statement:{statementIndex + 1}",
                    SqlOffset = statement.StartOffset,
                    SqlLine = statement.StartLine,
                    SourceLine = input.Source == "scriptdom"
                        ? input.Line + statement.StartLine - 1
                        : input.Line,
                };
                Evaluate(unit, statement);
                Add(unit);
            }
        }

        var summary = results
            .GroupBy(result => (result.Source, result.Scope))
            .OrderBy(group => group.Key.Source, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Scope, StringComparer.Ordinal)
            .Select(group => new CampaignSummary(
                group.Key.Source,
                group.Key.Scope,
                group.Count(),
                group.Count(result => result.Eligible),
                group.Count(result => result.CyqwelParsed == true),
                new SortedDictionary<string, int>(group
                    .GroupBy(result => CampaignJson.OutcomeName(result.Outcome))
                    .ToDictionary(outcome => outcome.Key, outcome => outcome.Count()),
                    StringComparer.Ordinal)))
            .ToArray();

        return new CampaignReport(
            1,
            cyqwelRevision,
            typeof(TSqlParser).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? typeof(TSqlParser).Assembly.GetName().Version!.ToString(),
            ParserVersion.ToString(),
            true,
            startedAt,
            DateTimeOffset.UtcNow,
            corpus.Sources,
            corpus.Exclusions,
            summary,
            results);

        void Add(CaseResult result)
        {
            results.Add(result);
            onResult?.Invoke(result);
        }
    }

    public CaseResult Evaluate(CorpusCase input)
    {
        var result = NewResult(input, "input", input.Sql);
        Evaluate(result);
        return result;
    }

    private static CaseResult NewResult(CorpusCase input, string scope, string sql) => new()
    {
        Id = input.Id,
        CaseId = input.Id,
        Source = input.Source,
        Path = input.Path,
        SourceLine = input.Line,
        Context = input.Context,
        Scope = scope,
        Sql = sql,
    };

    private static TSqlScript? Evaluate(CaseResult result, TSqlStatement? originalStatement = null)
    {
        TSqlScript? reference = null;
        try
        {
            reference = ParseReference(result.Sql, out var referenceErrors);
            result.ReferenceErrors = referenceErrors;
            if (referenceErrors.Count != 0)
            {
                result.Outcome = CaseOutcome.ReferenceRejected;
                return reference;
            }

            if (reference is null)
            {
                throw new InvalidOperationException("ScriptDom accepted input without returning a script.");
            }

            result.ReferenceAccepted = true;
            var statements = reference.Batches.SelectMany(batch => batch.Statements).ToArray();
            result.StatementType = string.Join(", ", statements
                .Select(statement => statement.GetType().Name).Distinct());
            if (statements.Length == 0)
            {
                result.Outcome = CaseOutcome.ReferenceEmpty;
                return reference;
            }

            result.Stage = "reference-context";
            if (originalStatement is not null &&
                (statements.Length != 1 ||
                 CanonicalSql(originalStatement) != CanonicalSql(statements[0])))
            {
                result.Outcome = CaseOutcome.ReferenceContextChanged;
                return reference;
            }

            result.Eligible = true;
            result.Stage = "cyqwel-parse";
            result.CyqwelParsed = SqlDialects.TSql.TryParse(result.Sql, out var document, out var error);
            result.ParseError = error;
            if (result.CyqwelParsed != true)
            {
                result.Outcome = CaseOutcome.ParseRejected;
                return reference;
            }

            result.Stage = "cyqwel-generate";
            try
            {
                result.GeneratedSql = document!.ToSql(SqlDialects.TSql);
            }
            catch (NotSupportedException exception)
            {
                result.Outcome = CaseOutcome.GenerationUnsupported;
                result.ExceptionType = exception.GetType().FullName;
                result.ExceptionMessage = exception.Message;
                return reference;
            }

            result.Stage = "generated-reference-parse";
            var generatedReference = ParseReference(result.GeneratedSql, out var generatedErrors);
            result.GeneratedReferenceErrors = generatedErrors;
            if (generatedErrors.Count != 0)
            {
                result.Outcome = CaseOutcome.GeneratedReferenceRejected;
                return reference;
            }

            if (generatedReference is null)
            {
                throw new InvalidOperationException("ScriptDom accepted generated SQL without returning a script.");
            }

            result.AstMismatches = CheckProjectionAliases(
                statements, document!, generatedReference.Batches.SelectMany(batch => batch.Statements).ToArray());
            result.Stage = "cyqwel-reparse";
            if (!SqlDialects.TSql.TryParse(result.GeneratedSql, out var reparsed, out var reparseError))
            {
                result.Outcome = CaseOutcome.ReparseRejected;
                result.ParseError = reparseError;
                return reference;
            }

            result.Stage = "cyqwel-regenerate";
            result.RegeneratedSql = reparsed!.ToSql(SqlDialects.TSql);
            if (result.GeneratedSql != result.RegeneratedSql)
            {
                result.Outcome = CaseOutcome.RoundTripChanged;
                return reference;
            }

            result.Stage = "reference-compare";
            result.ReferenceSql = CanonicalSql(reference);
            result.GeneratedReferenceSql = CanonicalSql(generatedReference);
            result.Outcome = result.AstMismatches.Count != 0
                ? CaseOutcome.AstMismatch
                : result.ReferenceSql != result.GeneratedReferenceSql
                    ? CaseOutcome.ReferenceChanged
                    : CaseOutcome.RoundTripStable;
            result.Stage = "complete";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            result.Outcome = CaseOutcome.Exception;
            result.ExceptionType = exception.GetType().FullName;
            result.ExceptionMessage = exception.ToString();
        }

        return reference;
    }

    private static TSqlScript? ParseReference(string sql, out IReadOnlyList<ReferenceDiagnostic> errors)
    {
        using var reader = new StringReader(sql);
        var parser = TSqlParser.CreateParser(ParserVersion, initialQuotedIdentifiers: true);
        var fragment = parser.Parse(reader, out var diagnostics);
        errors = diagnostics.Select(error => new ReferenceDiagnostic(
            error.Number, error.Message, error.Offset, error.Line, error.Column)).ToArray();
        if (fragment is null && errors.Count != 0)
        {
            return null;
        }

        return fragment as TSqlScript
            ?? throw new InvalidOperationException("ScriptDom did not return a TSqlScript.");
    }

    private static string CanonicalSql(TSqlFragment fragment)
    {
        new Sql180ScriptGenerator().GenerateScript(fragment, out var sql, out var errors);
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(
                $"ScriptDom could not generate its own tree: {string.Join("; ", errors.Select(error => error.Message))}");
        }

        return sql.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static IReadOnlyList<AstMismatch> CheckProjectionAliases(
        IReadOnlyList<TSqlStatement> reference,
        SqlDocument document,
        IReadOnlyList<TSqlStatement> generated)
    {
        var mismatches = new List<AstMismatch>();
        if (reference.Count != document.Statements.Count)
        {
            mismatches.Add(new AstMismatch("statement.count",
                reference.Count.ToString(), document.Statements.Count.ToString()));
            return mismatches;
        }
        if (reference.Count != 1 ||
            reference[0] is not ReferenceSelect { QueryExpression: QuerySpecification query } ||
            document.Statements[0] is not CyqwelSelect select)
        {
            return mismatches;
        }
        if (query.SelectElements.Count != select.Projections.Count)
        {
            mismatches.Add(new AstMismatch("projection.count",
                query.SelectElements.Count.ToString(), select.Projections.Count.ToString()));
            return mismatches;
        }
        var generatedQuery = generated.Count == 1 &&
            generated[0] is ReferenceSelect { QueryExpression: QuerySpecification generatedSelect }
                ? generatedSelect : null;

        for (var i = 0; i < query.SelectElements.Count; i++)
        {
            if (query.SelectElements[i] is SelectScalarExpression scalar)
            {
                var expected = scalar.ColumnName?.Value;
                var actual = select.Projections[i].Alias?.Value;
                if (expected != actual)
                {
                    mismatches.Add(new AstMismatch(
                        $"projection[{i}].alias", expected ?? "(none)", actual ?? "(none)"));
                }
                if (select.Projections[i].AssignmentTarget is not null)
                {
                    mismatches.Add(new AstMismatch(
                        $"projection[{i}].kind", "scalar", "variable-assignment"));
                }
            }
            else if (query.SelectElements[i] is SelectSetVariable variable)
            {
                var item = select.Projections[i];
                var target = item.AssignmentTarget is null ? "(none)" : "@" + item.AssignmentTarget.Value;
                if (target != variable.Variable.Name)
                {
                    mismatches.Add(new AstMismatch(
                        $"projection[{i}].assignment", variable.Variable.Name, target));
                }
                var expectedOperator = variable.AssignmentKind switch
                {
                    AssignmentKind.Equals => SqlAssignmentOperator.Assign,
                    AssignmentKind.AddEquals => SqlAssignmentOperator.Add,
                    AssignmentKind.SubtractEquals => SqlAssignmentOperator.Subtract,
                    AssignmentKind.MultiplyEquals => SqlAssignmentOperator.Multiply,
                    AssignmentKind.DivideEquals => SqlAssignmentOperator.Divide,
                    AssignmentKind.ModEquals => SqlAssignmentOperator.Modulo,
                    AssignmentKind.BitwiseAndEquals => SqlAssignmentOperator.BitwiseAnd,
                    AssignmentKind.BitwiseOrEquals => SqlAssignmentOperator.BitwiseOr,
                    AssignmentKind.BitwiseXorEquals => SqlAssignmentOperator.BitwiseXor,
                    AssignmentKind.ConcatEquals => SqlAssignmentOperator.Concatenate,
                    _ => throw new InvalidOperationException($"Unknown assignment kind {variable.AssignmentKind}."),
                };
                if (item.AssignmentOperator != expectedOperator)
                {
                    mismatches.Add(new AstMismatch($"projection[{i}].operator",
                        expectedOperator.ToString(), item.AssignmentOperator.ToString()));
                }
                if (generatedQuery is not null && i < generatedQuery.SelectElements.Count &&
                    generatedQuery.SelectElements[i] is SelectSetVariable generatedVariable)
                {
                    var expectedValue = CanonicalSql(variable.Expression);
                    var actualValue = CanonicalSql(generatedVariable.Expression);
                    if (expectedValue != actualValue)
                    {
                        mismatches.Add(new AstMismatch($"projection[{i}].value", expectedValue, actualValue));
                    }
                }
            }
        }

        return mismatches;
    }

    private static void ValidateCorpus(Corpus corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (corpus.SchemaVersion != 1)
        {
            throw new ArgumentException($"Unsupported corpus schema version: {corpus.SchemaVersion}.");
        }

        if (corpus.Sources is null || corpus.Cases is null || corpus.Exclusions is null)
        {
            throw new ArgumentException("Corpus sources, cases, and exclusions must be arrays, not null.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in corpus.Sources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Source) ||
                !sources.Add(source.Source) || string.IsNullOrWhiteSpace(source.Repository) ||
                source.Revision is not { Length: 40 } || !source.Revision.All(char.IsAsciiHexDigit) ||
                string.IsNullOrWhiteSpace(source.License) || string.IsNullOrWhiteSpace(source.LicensePath))
            {
                throw new ArgumentException("Each corpus source needs a unique name, repository, full commit, and license.");
            }
        }

        if (corpus.Cases.Count == 0)
        {
            throw new ArgumentException("The corpus contains no cases.");
        }

        foreach (var input in corpus.Cases)
        {
            if (input is null)
            {
                throw new ArgumentException("Corpus cases cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(input.Id) || !ids.Add(input.Id))
            {
                throw new ArgumentException($"Empty or duplicate case ID: '{input.Id}'.");
            }

            if (!sources.Contains(input.Source) || string.IsNullOrWhiteSpace(input.Path) ||
                input.Line < 1 || input.Sql is null || input.Context is null)
            {
                throw new ArgumentException($"Invalid provenance or SQL for case '{input.Id}'.");
            }
        }
    }
}
