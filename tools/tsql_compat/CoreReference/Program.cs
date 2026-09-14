using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.SqlServer.TransactSql.ScriptDom;

if (args is not [var inputPath, var outputPath])
{
    Console.Error.WriteLine("Usage: CoreReference <corpus.json> <reference-only.json>");
    return 1;
}

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
using var input = JsonDocument.Parse(File.ReadAllText(inputPath));
var parser = TSqlParser.CreateParser(SqlVersion.Sql180, initialQuotedIdentifiers: true);
var generator = new Sql180ScriptGenerator();
var rows = new List<object>();
foreach (var item in input.RootElement.GetProperty("cases").EnumerateArray())
{
    if (item.TryGetProperty("source", out var source) && source.GetString() != "sqlglot")
    {
        continue;
    }

    var id = item.GetProperty("id").GetString()!;
    var sql = item.GetProperty("sql").GetString()!;
    var script = parser.Parse(new StringReader(sql), out var errors);
    var tokens = parser.GetTokenStream(new StringReader(sql), out var tokenErrors);
    var accepted = errors.Count == 0 && tokenErrors.Count == 0;
    var scalar = parser.ParseExpression(new StringReader(sql), out var scalarErrors);
    var predicate = parser.ParseBooleanExpression(new StringReader(sql), out var predicateErrors);
    var significantEnd = tokens.LastOrDefault(token => token.TokenType is not (
        TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile or
        TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment));
    var expressionEnd = significantEnd is null ? 0 : significantEnd.Offset + significantEnd.Text.Length;
    var scalarAccepted = scalarErrors.Count == 0 && scalar is not null
        && scalar.StartOffset + scalar.FragmentLength == expressionEnd;
    var predicateAccepted = predicateErrors.Count == 0 && predicate is not null
        && predicate.StartOffset + predicate.FragmentLength == expressionEnd;
    var statements = new List<object>();
    if (accepted && script is TSqlScript parsed)
    {
        for (var batchIndex = 0; batchIndex < parsed.Batches.Count; batchIndex++)
        {
            var batch = parsed.Batches[batchIndex];
            for (var statementIndex = 0; statementIndex < batch.Statements.Count; statementIndex++)
            {
                var statement = batch.Statements[statementIndex];
                Walk(statement, $"batch[{batchIndex}]/statement[{statementIndex}]", true, statements);
            }
        }
    }

    rows.Add(new
    {
        id,
        sqlSha256 = Hash(sql),
        accepted,
        expressionKind = scalarAccepted ? "scalar" : predicateAccepted ? "predicate" : null,
        errors = errors.Concat(tokenErrors).Select(error => new
        {
            error.Number, error.Offset, error.Line, error.Column, error.Message,
        }).ToArray(),
        batchCount = accepted && script is TSqlScript batches ? batches.Batches.Count : 0,
        tokens = tokens.Where(token => token.TokenType is not (
            TSqlTokenType.WhiteSpace or TSqlTokenType.EndOfFile or
            TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment))
            .Select(token => new { type = token.TokenType.ToString(), token.Text, token.Offset }).ToArray(),
        facts = accepted ? Facts(script) : [],
        statements,
    });

    void Walk(TSqlFragment node, string path, bool topLevel, List<object> target)
    {
        if (node is TSqlStatement statement)
        {
            var text = sql.Substring(statement.StartOffset, statement.FragmentLength);
            var isolated = parser.Parse(new StringReader(text), out var isolatedErrors);
            var isolatedStatements = isolated is TSqlScript isolatedScript
                ? isolatedScript.Batches.SelectMany(batch => batch.Statements).ToArray()
                : [];
            generator.GenerateScript(statement, out var originalCanonical);
            var isolatedCanonical = "";
            if (isolatedStatements.Length == 1)
            {
                generator.GenerateScript(isolatedStatements[0], out isolatedCanonical);
            }

            target.Add(new
            {
                path,
                topLevel,
                type = statement.GetType().Name,
                offset = statement.StartOffset,
                length = statement.FragmentLength,
                line = statement.StartLine,
                sql = text,
                sqlSha256 = Hash(text),
                accepted = isolatedErrors.Count == 0 && isolatedStatements.Length == 1,
                contextStable = isolatedErrors.Count == 0 && isolatedStatements.Length == 1
                    && isolatedStatements[0].GetType() == statement.GetType()
                    && originalCanonical.Trim() == isolatedCanonical.Trim(),
                originalCanonical,
                isolatedCanonical,
                errors = isolatedErrors.Select(error => new
                {
                    error.Number, error.Offset, error.Line, error.Column, error.Message,
                }).ToArray(),
                facts = Facts(statement),
            });
        }

        foreach (var (name, child) in Children(node))
        {
            Walk(child, path + "/" + name, false, target);
        }
    }
}

var result = new
{
    schemaVersion = 1,
    reference = new
    {
        package = "Microsoft.SqlServer.TransactSql.ScriptDom",
        version = "180.107.0",
        parser = "Sql180",
        quotedIdentifiers = true,
        offsetEncoding = "utf-16",
    },
    cases = rows,
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
File.WriteAllText(outputPath, JsonSerializer.Serialize(result, jsonOptions) + "\n");
Console.WriteLine($"Wrote {rows.Count} reference-only inputs to {outputPath}.");
return 0;

static string Hash(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

static IEnumerable<(string Name, TSqlFragment Node)> Children(TSqlFragment node)
{
    foreach (var property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .OrderBy(property => property.Name, StringComparer.Ordinal))
    {
        if (property.GetIndexParameters().Length != 0 || property.Name == nameof(TSqlFragment.ScriptTokenStream))
        {
            continue;
        }

        var value = property.GetValue(node);
        if (value is TSqlFragment child)
        {
            yield return (property.Name, child);
        }
        else if (value is IEnumerable children and not string)
        {
            var index = 0;
            foreach (var element in children)
            {
                if (element is TSqlFragment fragment)
                {
                    yield return ($"{property.Name}[{index}]", fragment);
                }
                index++;
            }
        }
    }
}

static object[] Facts(TSqlFragment root)
{
    var facts = new List<object>();
    Walk(root);
    return facts.ToArray();

    void Walk(TSqlFragment node)
    {
        var values = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.PropertyType.IsEnum || property.PropertyType == typeof(bool))
            {
                var value = property.GetValue(node);
                if (value is not null)
                {
                    values[property.Name] = value.ToString()!;
                }
            }
        }

        facts.Add(new { type = node.GetType().Name, offset = node.StartOffset, length = node.FragmentLength, values });
        foreach (var (_, child) in Children(node))
        {
            Walk(child);
        }
    }
}
