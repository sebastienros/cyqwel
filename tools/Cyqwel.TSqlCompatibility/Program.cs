using System.Text.Json;
using Cyqwel.TSqlCompatibility;

return Run(args);

static int Run(string[] args)
{
    if (args is ["--help"] or [])
    {
        Console.WriteLine("Usage: --corpus <corpus.json> --output <results.json> --revision <Cyqwel commit> [--case <case-id>]");
        return args.Length == 0 ? 1 : 0;
    }

    try
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length ||
                args[i] is not ("--corpus" or "--output" or "--revision" or "--case") ||
                !options.TryAdd(args[i], args[i + 1]))
            {
                throw new ArgumentException($"Invalid or duplicate option: {args[i]}.");
            }
        }

        if (!options.TryGetValue("--corpus", out var corpusPath) ||
            !options.TryGetValue("--output", out var outputPath) ||
            !options.TryGetValue("--revision", out var revision))
        {
            throw new ArgumentException("--corpus, --output, and --revision are required.");
        }

        var corpus = JsonSerializer.Deserialize<Corpus>(
            File.ReadAllText(corpusPath), CampaignJson.Options)
            ?? throw new ArgumentException("The corpus is null.");
        if (options.TryGetValue("--case", out var caseId))
        {
            corpus = corpus with { Cases = corpus.Cases.Where(input => input.Id == caseId).ToArray() };
            if (corpus.Cases.Count == 0)
            {
                throw new ArgumentException($"Unknown case ID: {caseId}.");
            }
        }

        outputPath = Path.GetFullPath(outputPath);
        if (outputPath == Path.GetFullPath(corpusPath))
        {
            throw new ArgumentException("The report must not overwrite its input corpus.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var journalPath = Path.ChangeExtension(outputPath, ".jsonl");
        using var journal = new StreamWriter(journalPath) { AutoFlush = true };
        var journalOptions = new JsonSerializerOptions(CampaignJson.Options) { WriteIndented = false };
        var count = 0;
        var report = new CampaignRunner().Run(corpus, revision, result =>
        {
            journal.WriteLine(JsonSerializer.Serialize(result, journalOptions));
            if (++count % 250 == 0)
            {
                Console.Error.WriteLine($"Evaluated {count} units; last: {result.Id}");
            }
        }) with { CaseFilter = caseId };

        var temporaryPath = outputPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(report, CampaignJson.Options) + "\n");
        File.Move(temporaryPath, outputPath, overwrite: true);
        foreach (var summary in report.Summary)
        {
            Console.WriteLine($"{summary.Source}/{summary.Scope}: {summary.Candidates} candidates, " +
                $"{summary.Eligible} eligible, {summary.Parsed} parsed");
            foreach (var outcome in summary.Outcomes)
            {
                Console.WriteLine($"  {outcome.Key}: {outcome.Value}");
            }
        }

        Console.WriteLine($"Report: {outputPath}");
        return report.Results.Any(result => result.Outcome == CaseOutcome.Exception) ? 2 : 0;
    }
    catch (Exception exception) when (exception is ArgumentException or IOException or JsonException)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}
