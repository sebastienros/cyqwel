using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cyqwel.Dialects;
using Cyqwel.TSqlCompatibility;
using Cyqwel.Visitors;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Cyqwel.Tests;

public sealed class TSqlCoreFixtureTests
{
    private const string ProfileHash = "5dbea22035ebeecf9a623c98f123a7e1a723bf7f57a971df5d828525184c5253";
    private const string FixtureHash = "bc06eab66a62fd8c566db938512e5447b8ed11ea04de37bd7d35a5353511eb52";
    private const string LicenseHash = "b0bf909b472ddf9bd4eab30f6e6fd8b33bf3997ce038b882fe0663f291958d2d";
    private const string CoreIdsHash = "ae0343164b731756a72fea0241b404481aab44cc285e5dba5a3bf9937ed285ab";
    private const string OriginalIdsHash = "8e01b1d2aaf680a72147292a4fac2da4e44f70830ce6cad6eade013a780147b1";
    private const string AllIdsHash = "460c7780f9df71cd3d937c637a70aaabfe2ca31f5b6c66ae88a17894c5519184";
    private const string UpstreamRevision = "5cfb5997a99010940138670adf3d6b34ac5a0a08";
    private static readonly Lazy<FrozenData> Data = new(Load);

    // Pinned test_tsql.py: write_sql at 505; explicit write["tsql"] expectations at the other six calls.
    private static readonly Dictionary<string, string> SqlGlotBooleanWriteExpectations = new(StringComparer.Ordinal)
    {
        ["sqlglot:tests/dialects/test_tsql.py:505:8:validate_identity.sql:0"] =
            "SELECT val FROM (VALUES ((1), (0), (NULL))) AS t(val)",
        ["sqlglot:tests/dialects/test_tsql.py:1112:8:validate_all.sql:0/core/predicate-probe"] =
            "a = 1",
        ["sqlglot:tests/dialects/test_tsql.py:1114:8:validate_all.sql:0/core/predicate-probe"] =
            "a <> 0",
        ["sqlglot:tests/dialects/test_tsql.py:1120:8:validate_all.sql:0/core/expression-probe"] =
            "CASE WHEN a IN (1) THEN 'y' ELSE 'n' END",
        ["sqlglot:tests/dialects/test_tsql.py:1125:8:validate_all.sql:0/core/expression-probe"] =
            "CASE WHEN NOT a IN (0) THEN 'y' ELSE 'n' END",
        ["sqlglot:tests/dialects/test_tsql.py:1130:8:validate_all.sql:0"] =
            "SELECT 1, 0",
        ["sqlglot:tests/dialects/test_tsql.py:1132:8:validate_all.sql:0"] =
            "SELECT 1 AS a, 0 AS b",
    };

    public static IEnumerable<object[]> CoreCases() =>
        Data.Value.Profile.CoreIds.Select(id => new object[] { id });

    public static IEnumerable<object[]> BooleanBaselineCases() =>
        SqlGlotBooleanWriteExpectations.Keys.Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(CoreCases))]
    public void Every_frozen_core_case_is_a_required_positive_round_trip(string id)
    {
        var data = Data.Value;
        var fixture = data.Cases[id];
        var entry = data.Entries[id];
        Assert.Equal("core", entry.Disposition);
        Assert.Equal(entry.SqlSha256, Hash(fixture.Sql));

        var source = data.Profile.Source;
        var report = new CampaignRunner().Run(new Corpus
        {
            SchemaVersion = 1,
            Sources = [new CorpusSource(
                source.Source, source.Repository, source.Revision, source.License, source.UpstreamLicensePath)],
            Cases = [new CorpusCase
            {
                Id = fixture.Id, Source = fixture.Source, Path = fixture.Path,
                Line = fixture.Line, Sql = fixture.Sql, Context = fixture.Context,
            }],
            Exclusions = [],
        }, "offline-sqlglot-core-v1");
        var result = Assert.Single(report.Results, result => result.Scope == "input");
        Assert.True(result.ReferenceAccepted, $"{id}: frozen reference-valid input was rejected.");
        Assert.True(
            result.Outcome is CaseOutcome.RoundTripStable or CaseOutcome.ReferenceChanged,
            $"{id}: {result.Outcome} at {result.Stage}; " +
            JsonSerializer.Serialize(new { result.ParseError, result.AstMismatches, result.ExceptionMessage }));
        Assert.True(result.CyqwelParsed);
        Assert.Empty(result.AstMismatches ?? []);
        var generated = Assert.IsType<string>(result.GeneratedSql);

        var expectedReference = ParseReference(fixture.Sql);
        var generatedReference = ParseReference(generated);
        Assert.Equal(entry.ReferenceBatchCount, expectedReference.Batches.Count);
        AssertSemanticEquality(id, expectedReference, generatedReference);

        var document = SqlDialects.TSql.Parse(fixture.Sql);
        Assert.Equal(generated, document.ToSql(SqlDialects.TSql));
        Assert.Equal(generated, document.Accept(new IdentityRewriter()).ToSql(SqlDialects.TSql));
        Assert.Equal(generated, SqlDialects.TSql.Parse(generated).ToSql(SqlDialects.TSql));
    }

    [Fact]
    public void Frozen_profile_accounts_for_exact_original_and_derived_membership()
    {
        var data = Data.Value;
        var profile = data.Profile;
        Assert.Equal(720, profile.OriginalCount);
        Assert.Equal(211, profile.DerivedCount);
        Assert.Equal(511, profile.CoreCount);
        Assert.Equal(0, profile.ContextSensitiveCount);
        Assert.Equal(931, data.Cases.Count);
        Assert.Equal(data.Cases.Keys.Order(StringComparer.Ordinal), data.Entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(UpstreamRevision, profile.Source.Revision);
        Assert.Equal("MIT", profile.Source.License);
        Assert.Equal("LICENSE", profile.Source.UpstreamLicensePath);
        Assert.Equal(LicenseHash, profile.Source.LicenseSha256);
        Assert.Equal(CoreIdsHash, profile.CoreIdsSha256);
        Assert.Equal(OriginalIdsHash, profile.OriginalIdsSha256);
        Assert.Equal(AllIdsHash, profile.AllIdsSha256);
        Assert.Equal(CoreIdsHash, HashIds(profile.CoreIds));
        var discoveredIds = CoreCases().Select(row => Assert.IsType<string>(Assert.Single(row))).ToArray();
        Assert.Equal(profile.CoreIds, discoveredIds);
        Assert.Equal(CoreIdsHash, HashIds(discoveredIds));
        Assert.Equal(AllIdsHash, HashIds(data.Cases.Keys));
        Assert.Equal(OriginalIdsHash, HashIds(data.Cases.Values.Where(c => c.Kind == "input").Select(c => c.Id)));
        Assert.Equal(
            profile.CoreIds,
            data.Entries.Values.Where(entry => entry.Disposition == "core")
                .Select(entry => entry.Id).Order(StringComparer.Ordinal));
        AssertCounts(profile.OriginalDispositions, data.Entries.Values.Where(e => e.Kind == "input"),
            new() { ["core"] = 316, ["deferred"] = 160, ["not-statement"] = 185, ["reference-rejected"] = 59 });
        AssertCounts(profile.DerivedDispositions, data.Entries.Values.Where(e => e.Kind != "input"),
            new() { ["core"] = 195, ["deferred"] = 8, ["reference-rejected"] = 8 });

        foreach (var entry in data.Entries.Values)
        {
            var fixture = data.Cases[entry.Id];
            Assert.Contains(entry.Disposition, new[] { "core", "deferred", "not-statement", "reference-rejected" });
            Assert.NotEmpty(entry.Reason);
            Assert.NotEmpty(entry.Requirements);
            Assert.Equal(entry.SqlSha256, fixture.SqlSha256);
            Assert.Equal(entry.SqlSha256, Hash(fixture.Sql));
            Assert.Equal(entry.Kind, fixture.Kind);
            Assert.Equal(entry.SourceId, fixture.SourceId);
            Assert.Equal("sqlglot", fixture.Source);
            Assert.Equal("tests/dialects/test_tsql.py", fixture.Path);
            Assert.True(fixture.Line > 0);
            if (entry.Disposition == "core")
            {
                Assert.True(entry.ReferenceAccepted);
            }

            if (fixture.Kind == "input")
            {
                Assert.Equal(fixture.Id, fixture.SourceId);
                continue;
            }

            var parent = data.Cases[fixture.SourceId];
            var parentEntry = data.Entries[parent.Id];
            var derivation = Assert.IsType<JsonElement>(fixture.Derivation);
            Assert.Equal("input", parent.Kind);
            Assert.Equal(parent.SqlSha256, derivation.GetProperty("originalSqlSha256").GetString());
            Assert.Equal(parent.Path, fixture.Path);
            Assert.Equal(parent.Line, fixture.Line);
            if (fixture.Kind == "statement-slice")
            {
                Assert.True(parentEntry.ReferenceAccepted);
                Assert.DoesNotContain(parentEntry.Disposition, new[] { "not-statement", "reference-rejected" });
                Assert.Equal("utf-16", derivation.GetProperty("offsetEncoding").GetString());
                Assert.Equal(fixture.Sql, parent.Sql.Substring(
                    derivation.GetProperty("offset").GetInt32(), derivation.GetProperty("length").GetInt32()));
                if (entry.Disposition == "core")
                {
                    Assert.True(derivation.GetProperty("contextStable").GetBoolean());
                }
            }
            else
            {
                Assert.Equal("not-statement", parentEntry.Disposition);
                Assert.Equal(fixture.Sql,
                    derivation.GetProperty("prefix").GetString() + parent.Sql +
                    derivation.GetProperty("suffix").GetString());
            }
        }
    }

    [Fact]
    public void Reference_dispositions_are_reproducible_offline_without_partial_recovery()
    {
        foreach (var fixture in Data.Value.Cases.Values)
        {
            var entry = Data.Value.Entries[fixture.Id];
            var parser = TSqlParser.CreateParser(SqlVersion.Sql180, initialQuotedIdentifiers: true);
            var reference = parser.Parse(new StringReader(fixture.Sql), out var errors);
            Assert.True(entry.ReferenceAccepted == (errors.Count == 0),
                $"{fixture.Id}: reference validity changed: {string.Join("; ", errors.Select(error => error.Message))}");
            if (entry.ReferenceAccepted)
            {
                var script = Assert.IsType<TSqlScript>(reference);
                Assert.Equal(entry.ReferenceBatchCount, script.Batches.Count);
                Assert.Equal(entry.ReferenceStatementTypes,
                    script.Batches.SelectMany(batch => batch.Statements).Select(statement => statement.GetType().Name));
            }
        }
    }

    [Theory]
    [InlineData("SELECT x FROM dbo.t", "SELECT [x] FROM [dbo].[t]")]
    [InlineData("SELECT (x + 1)", "SELECT x + 1")]
    [InlineData("SELECT GETDATE()", "SELECT CURRENT_TIMESTAMP")]
    [InlineData("SELECT DATEPART(qq, x)", "SELECT DATEPART(QUARTER, x)")]
    [InlineData("SELECT CAST(x AS NUMERIC(10, 2))", "SELECT CAST(x AS DECIMAL(10, 2))")]
    [InlineData("CREATE TABLE t (id INT IDENTITY)", "CREATE TABLE t (id INT IDENTITY(1, 1))")]
    [InlineData("SELECT DATETIMEFROMPARTS(2013, 04, 05, 12, 00, 00, 0)", "SELECT DATETIMEFROMPARTS(2013, 4, 5, 12, 0, 0, 0)")]
    [InlineData("SELECT object_id('dbo.t')", "SELECT OBJECT_ID('dbo.t')")]
    [InlineData("IF 1 = 1 EXEC('SELECT 1')", "IF 1 = 1 BEGIN EXEC('SELECT 1'); END")]
    [InlineData("CREATE PROC p AS SELECT 1; SELECT 2", "CREATE PROC p AS BEGIN SELECT 1; SELECT 2; END")]
    [InlineData("SELECT TRIM(BOTH 'a' FROM x)", "SELECT TRIM('a' FROM x)")]
    [InlineData("SELECT * FROM t FOR XML PATH, TYPE, ROOT('r')", "SELECT * FROM t FOR XML PATH, ROOT('r'), TYPE")]
    [InlineData("SELECT 1 WHERE a != b", "SELECT 1 WHERE a <> b")]
    public void Only_documented_structural_equivalences_ignore_canonical_spelling(string left, string right) =>
        AssertSemanticEquality("normalization-control", ParseReference(left), ParseReference(right));

    [Theory]
    [InlineData("SELECT a = 1", "SELECT a")]
    [InlineData("SELECT @a = 1", "SELECT 1 AS [@a]")]
    [InlineData("CREATE TABLE t (id INT IDENTITY(10, 2))", "CREATE TABLE t (id INT IDENTITY(1, 1))")]
    [InlineData("SET NOCOUNT ON", "SET NOCOUNT OFF")]
    [InlineData("SELECT N'x'", "SELECT 'x'")]
    [InlineData("SELECT TRUE", "SELECT 1")]
    [InlineData("SELECT (a + b) * c", "SELECT a + b * c")]
    [InlineData("SELECT qq", "SELECT QUARTER")]
    [InlineData("SELECT DATEPART(quarter, x)", "SELECT DATEPART(month, x)")]
    [InlineData("SELECT * FROM t OPTION(MAXDOP 1)", "SELECT * FROM t")]
    [InlineData("SELECT * FROM OPENJSON(@j) WITH (x INT '$.x')", "SELECT * FROM OPENJSON(@j)")]
    [InlineData("INSERT INTO t OUTPUT inserted.id INTO @rows SELECT 1", "INSERT INTO t OUTPUT inserted.id SELECT 1")]
    [InlineData("SELECT 1\nGO\nSELECT 2", "SELECT 1; SELECT 2")]
    [InlineData("CREATE PROC p AS SELECT 1;\nGO\nSELECT 2", "CREATE PROC p AS SELECT 1; SELECT 2")]
    [InlineData("SELECT TRIM(LEADING 'a' FROM x)", "SELECT TRIM('a' FROM x)")]
    [InlineData("SELECT * FROM t FOR XML PATH, ROOT('first')", "SELECT * FROM t FOR XML PATH, ROOT('second')")]
    [InlineData("IF @x = 1 BEGIN IF @y = 1 SELECT 1; END ELSE SELECT 2", "IF @x = 1 IF @y = 1 SELECT 1 ELSE SELECT 2")]
    [InlineData("SELECT * FROM tvfTest(1)", "SELECT * FROM TVFTEST(1)")]
    [InlineData("SELECT FALSE", "SELECT 0")]
    [InlineData("SELECT * FROM t TABLESAMPLE (10 PERCENT)", "SELECT * FROM t")]
    [InlineData("SELECT * FROM t TABLESAMPLE (10 PERCENT)", "SELECT * FROM t TABLESAMPLE (20 PERCENT)")]
    [InlineData("SELECT * FROM t TABLESAMPLE (10 PERCENT)", "SELECT * FROM t TABLESAMPLE (10 ROWS)")]
    [InlineData("SELECT * FROM t WITH (INDEX(ix_a))", "SELECT * FROM t WITH (INDEX(ix_b))")]
    [InlineData("SELECT * FROM t WITH (INDEX(1, 2))", "SELECT * FROM t WITH (INDEX(2, 1))")]
    [InlineData("CREATE SCHEMA firstSchema", "CREATE SCHEMA secondSchema")]
    [InlineData("COMMIT TRANSACTION tx WITH (DELAYED_DURABILITY = ON)", "COMMIT TRANSACTION tx WITH (DELAYED_DURABILITY = OFF)")]
    [InlineData("COMMIT TRANSACTION tx WITH (DELAYED_DURABILITY = ON)", "COMMIT TRANSACTION tx")]
    [InlineData("SELECT JSON_ARRAYAGG(c NULL ON NULL)", "SELECT JSON_ARRAYAGG(c ABSENT ON NULL)")]
    [InlineData("SELECT JSON_ARRAYAGG(c ORDER BY c)", "SELECT JSON_ARRAYAGG(c)")]
    [InlineData("SELECT JSON_ARRAYAGG(c ORDER BY c ASC)", "SELECT JSON_ARRAYAGG(c ORDER BY c DESC)")]
    [InlineData("SELECT REPLACE(REPLACE(x, 'a', 'b'), 'b', 'c')", "SELECT REPLACE(x, 'b', 'c')")]
    [InlineData("EXEC('SELECT @x')", "EXEC('SELECT @y')")]
    public void Semantic_audits_detect_information_loss(string left, string right) =>
        Assert.NotEqual(Fingerprint(ParseReference(left)), Fingerprint(ParseReference(right)));

    [Theory]
    [MemberData(nameof(BooleanBaselineCases))]
    public void Boolean_baselines_follow_pinned_numeric_expectations_without_mutating_evidence(string id)
    {
        Assert.Equal(7, SqlGlotBooleanWriteExpectations.Count);
        Assert.Contains(id, Data.Value.Profile.CoreIds);
        var fixture = Data.Value.Cases[id];
        var original = ParseReference(fixture.Sql);
        var originalFingerprint = Fingerprint(original);
        var expectedSql = SqlGlotBooleanWriteExpectations[id];
        if (fixture.Kind != "input")
        {
            var derivation = Assert.IsType<JsonElement>(fixture.Derivation);
            expectedSql = derivation.GetProperty("prefix").GetString() + expectedSql +
                derivation.GetProperty("suffix").GetString();
        }

        var upstreamValues = ReferenceValues(ParseReference(expectedSql));
        var originalValues = ReferenceValues(original);
        var baseline = SourceSemanticBaseline(id, original);
        var baselineValues = ReferenceValues(baseline);
        Assert.NotEmpty(originalValues.Booleans);
        Assert.Empty(upstreamValues.Booleans);
        Assert.Empty(baselineValues.Booleans);
        Assert.Equal(upstreamValues.Integers, baselineValues.Integers);
        Assert.Equal(originalValues.Integers.Count + originalValues.Booleans.Count, baselineValues.Integers.Count);
        Assert.Equal(originalFingerprint, Fingerprint(original));
        Assert.NotEqual(originalFingerprint, Fingerprint(baseline));
    }

    [Theory]
    [MemberData(nameof(BooleanBaselineCases))]
    public void Boolean_intent_cannot_be_attached_to_different_input_or_generated_identifiers(string id)
    {
        Assert.Throws<InvalidDataException>(() =>
            SourceSemanticBaseline(id, ParseReference("SELECT TRUE FROM dbo.ordinary_columns")));
        Assert.Throws<InvalidDataException>(() =>
            SourceSemanticBaseline(id, ParseReference("SELECT [TRUE], \"FALSE\"")));

        var original = ParseReference(Data.Value.Cases[id].Sql);
        AssertSemanticEquality(id, original, SourceSemanticBaseline(id, original));
        var mismatch = Assert.Throws<Xunit.Sdk.FailException>(() => AssertSemanticEquality(id, original, original));
        Assert.Contains("semantic reference AST mismatch", mismatch.Message);
    }

    [Theory]
    [InlineData("SELECT TRUE", "SELECT 1")]
    [InlineData("SELECT FALSE", "SELECT 0")]
    [InlineData("SELECT TRUE, FALSE FROM dbo.ordinary_columns", "SELECT 1, 0 FROM dbo.ordinary_columns")]
    [InlineData("SELECT [TRUE], \"FALSE\"", "SELECT 1, 0")]
    [InlineData("SELECT t.TRUE, dbo.t.FALSE", "SELECT 1, 0")]
    public void Unattributed_boolean_names_retain_column_semantics(string sql, string numericSql)
    {
        var source = ParseReference(sql);
        foreach (var id in new[] { "ordinary-column-control", "sqlglot:tests/dialects/test_tsql.py:12:8:validate_all.sql:0" })
        {
            var baseline = SourceSemanticBaseline(id, source);
            Assert.Same(source, baseline);
            Assert.NotEqual(Fingerprint(baseline), Fingerprint(ParseReference(numericSql)));
        }
    }

    [Theory]
    [InlineData("SELECT [TRUE], [FALSE]")]
    [InlineData("SELECT \"TRUE\", \"FALSE\"")]
    [InlineData("SELECT t.TRUE, t.FALSE")]
    [InlineData("SELECT dbo.t.TRUE, dbo.t.FALSE")]
    [InlineData("SELECT 'TRUE', N'FALSE', @TRUE, @FALSE")]
    public void Quoted_multipart_and_noncolumn_tokens_are_never_boolean_value_candidates(string sql) =>
        Assert.Empty(ReferenceValues(ParseReference(sql)).Booleans);

    private static void AssertCounts(
        IReadOnlyDictionary<string, int> declared, IEnumerable<CoreEntry> entries,
        Dictionary<string, int> expected)
    {
        var actual = entries.GroupBy(entry => entry.Disposition).ToDictionary(group => group.Key, group => group.Count());
        Assert.Equal(expected.OrderBy(pair => pair.Key), declared.OrderBy(pair => pair.Key));
        Assert.Equal(expected.OrderBy(pair => pair.Key), actual.OrderBy(pair => pair.Key));
    }

    private static TSqlScript ParseReference(string sql)
    {
        var parser = TSqlParser.CreateParser(SqlVersion.Sql180, initialQuotedIdentifiers: true);
        var result = parser.Parse(new StringReader(sql), out var errors);
        Assert.True(errors.Count == 0, $"Generated/reference SQL is invalid: {string.Join("; ", errors.Select(e => e.Message))}\n{sql}");
        return Assert.IsType<TSqlScript>(result);
    }

    private static void AssertSemanticEquality(string id, TSqlFragment expected, TSqlFragment actual)
    {
        var left = Fingerprint(SourceSemanticBaseline(id, expected));
        var right = Fingerprint(actual);
        if (left == right)
        {
            return;
        }

        var index = 0;
        while (index < Math.Min(left.Length, right.Length) && left[index] == right[index])
        {
            index++;
        }
        var start = Math.Max(0, index - 100);
        Assert.Fail($"{id}: semantic reference AST mismatch at {index}.\n" +
            $"Expected: {left.Substring(start, Math.Min(240, left.Length - start))}\n" +
            $"Actual:   {right.Substring(start, Math.Min(240, right.Length - start))}");
    }

    private static string Fingerprint(TSqlFragment fragment) =>
        JsonSerializer.Serialize(Normalize(fragment));

    private static TSqlFragment SourceSemanticBaseline(string id, TSqlFragment source)
    {
        if (!SqlGlotBooleanWriteExpectations.ContainsKey(id))
        {
            return source;
        }

        var sql = string.Concat(source.ScriptTokenStream.Select(token => token.Text));
        if (Hash(sql) != Data.Value.Cases[id].SqlSha256)
        {
            throw new InvalidDataException($"SQLGlot Boolean intent is restricted to the unchanged frozen input: {id}.");
        }

        var values = ReferenceValues(source);
        Assert.NotEmpty(values.Booleans);
        var baseline = new StringBuilder(sql);
        foreach (var (identifier, integer) in values.Booleans.OrderByDescending(value => value.Identifier.StartOffset))
        {
            Assert.Equal(identifier.Value, sql.Substring(identifier.StartOffset, identifier.FragmentLength));
            baseline.Remove(identifier.StartOffset, identifier.FragmentLength).Insert(identifier.StartOffset, integer);
        }

        // Reparse only a semantic-baseline copy; the original parser input, reference AST and generated output stay untouched.
        return ParseReference(baseline.ToString());
    }

    private static BooleanReferenceValues ReferenceValues(TSqlFragment source)
    {
        var values = new BooleanReferenceValues();
        source.Accept(values);
        return values;
    }

    private static readonly HashSet<string> LocationProperties =
    [
        "StartOffset", "FragmentLength", "StartLine", "StartColumn",
        "FirstTokenIndex", "LastTokenIndex", "ScriptTokenStream",
    ];
    private static readonly Dictionary<string, string> DateParts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["year"] = "year", ["yy"] = "year", ["yyyy"] = "year",
        ["quarter"] = "quarter", ["qq"] = "quarter", ["q"] = "quarter",
        ["month"] = "month", ["mm"] = "month", ["m"] = "month",
        ["dayofyear"] = "dayofyear", ["dy"] = "dayofyear", ["y"] = "dayofyear",
        ["day"] = "day", ["dd"] = "day", ["d"] = "day",
        ["week"] = "week", ["wk"] = "week", ["ww"] = "week",
        ["weekday"] = "weekday", ["dw"] = "weekday", ["w"] = "weekday",
        ["hour"] = "hour", ["hh"] = "hour",
        ["minute"] = "minute", ["mi"] = "minute", ["n"] = "minute",
        ["second"] = "second", ["ss"] = "second", ["s"] = "second",
        ["millisecond"] = "millisecond", ["ms"] = "millisecond",
        ["microsecond"] = "microsecond", ["mcs"] = "microsecond",
        ["nanosecond"] = "nanosecond", ["ns"] = "nanosecond",
        ["tzoffset"] = "tzoffset", ["tz"] = "tzoffset",
        ["iso_week"] = "iso_week", ["isowk"] = "iso_week", ["isoww"] = "iso_week",
    };
    private static readonly HashSet<string> DatePartFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATEPART", "DATENAME", "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATETRUNC",
    };
    private static readonly HashSet<XmlForClauseOptions> SimpleXmlOptions =
    [
        XmlForClauseOptions.Auto, XmlForClauseOptions.Raw, XmlForClauseOptions.Path,
        XmlForClauseOptions.Type, XmlForClauseOptions.Root,
    ];

    private static object? Normalize(object? value)
    {
        if (value is TSqlFragment node)
        {
            var properties = node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetIndexParameters().Length == 0 && !LocationProperties.Contains(property.Name))
                .OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
            if (node is ParameterlessCall { ParameterlessCallType: ParameterlessCallType.CurrentTimestamp }
                && properties.Where(property => property.Name != nameof(ParameterlessCall.ParameterlessCallType))
                    .All(property => IsEmpty(property.GetValue(node))))
            {
                return new Dictionary<string, object?> { ["$type"] = "CurrentTimestamp" };
            }
            if (node is FunctionCall { CallTarget: null, Parameters.Count: 0 } timestamp
                && timestamp.FunctionName.Value.Equals("GETDATE", StringComparison.OrdinalIgnoreCase)
                && timestamp.OverClause is null && timestamp.WithinGroupClause is null
                && timestamp.UniqueRowFilter == UniqueRowFilter.NotSpecified
                && properties.Where(property => property.Name is not ("FunctionName" or "Parameters" or "UniqueRowFilter"))
                    .All(property => IsEmpty(property.GetValue(node)) || property.GetValue(node) is false))
            {
                return new Dictionary<string, object?> { ["$type"] = "CurrentTimestamp" };
            }

            var transparent = node.GetType().Name switch
            {
                "ParenthesisExpression" or "BooleanParenthesisExpression" => "Expression",
                "QueryParenthesisExpression" => "QueryExpression",
                _ => null,
            };
            if (transparent is not null && properties.Where(p => p.Name != transparent).All(p => IsEmpty(p.GetValue(node))))
            {
                return Normalize(properties.Single(p => p.Name == transparent).GetValue(node));
            }

            var result = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["$type"] = node.GetType().Name };
            foreach (var property in properties)
            {
                if (node is Identifier && property.Name == nameof(Identifier.QuoteType))
                {
                    continue;
                }
                if (node is SqlDataTypeReference && property.Name == nameof(SqlDataTypeReference.Name))
                {
                    // Built-in kind and parameters retain meaning; Name only preserves alias spelling.
                    continue;
                }

                var child = property.GetValue(node);
                if (node is IntegerLiteral && property.Name == nameof(IntegerLiteral.Value) && child is string integer)
                {
                    child = BigInteger.Parse(integer, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                }
                else if (node is SqlDataTypeReference && child is SqlDataTypeOption.Numeric)
                {
                    child = SqlDataTypeOption.Decimal;
                }
                else if (node is IdentityOptions && child is null && property.Name is "IdentitySeed" or "IdentityIncrement")
                {
                    child = new IntegerLiteral { Value = "1" };
                }
                if (node is FunctionCall { CallTarget: null } builtin && property.Name == nameof(FunctionCall.FunctionName)
                    && (DatePartFunctions.Contains(builtin.FunctionName.Value)
                        || builtin.FunctionName.Value.Equals("OBJECT_ID", StringComparison.OrdinalIgnoreCase)
                        || builtin.FunctionName.Value.Equals("TRIM", StringComparison.OrdinalIgnoreCase)))
                {
                    result[property.Name] = Normalize(new Identifier { Value = builtin.FunctionName.Value.ToUpperInvariant() });
                    continue;
                }
                if (node is FunctionCall { CallTarget: null } trim
                    && trim.FunctionName.Value.Equals("TRIM", StringComparison.OrdinalIgnoreCase)
                    && property.Name == nameof(FunctionCall.TrimOptions)
                    && child is Identifier trimOption && trimOption.Value.Equals("BOTH", StringComparison.OrdinalIgnoreCase))
                {
                    child = null;
                }
                if (node is FunctionCall { CallTarget: null } call && DatePartFunctions.Contains(call.FunctionName.Value))
                {
                    if (property.Name == nameof(FunctionCall.Parameters) && call.Parameters.Count > 0)
                    {
                        var parameters = call.Parameters.Select(parameter => Normalize(parameter)).ToArray();
                        var name = call.Parameters[0] switch
                        {
                            ColumnReferenceExpression { MultiPartIdentifier.Identifiers.Count: 1 } column =>
                                column.MultiPartIdentifier.Identifiers[0].Value,
                            IdentifierLiteral literal => literal.Value,
                            _ => null,
                        };
                        if (name is not null && DateParts.TryGetValue(name, out var canonical))
                        {
                            parameters[0] = new Dictionary<string, object?> { ["$datepart"] = canonical };
                        }
                        result[property.Name] = parameters;
                        continue;
                    }
                }
                if (node is StatementList statementList && property.Name == nameof(StatementList.Statements))
                {
                    result[property.Name] = NormalizeStatements(statementList.Statements);
                    continue;
                }
                if (child is TSqlStatement controlBody && (
                    node is IfStatement && property.Name is "ThenStatement" or "ElseStatement"
                    || node is WhileStatement && property.Name == "Statement"))
                {
                    result[property.Name] = NormalizeStatements([controlBody]);
                    continue;
                }
                if (node is XmlForClause xml && property.Name == nameof(XmlForClause.Options)
                    && xml.Options.All(option => SimpleXmlOptions.Contains(option.OptionKind))
                    && xml.Options.Select(option => option.OptionKind).Distinct().Count() == xml.Options.Count)
                {
                    result[property.Name] = xml.Options.OrderBy(option => option.OptionKind).Select(option => Normalize(option)).ToArray();
                    continue;
                }
                result[property.Name] = Normalize(child);
            }
            return result;
        }
        if (value is BooleanComparisonType.NotEqualToExclamation)
        {
            value = BooleanComparisonType.NotEqualToBrackets;
        }
        if (value is Enum enumeration)
        {
            return enumeration.GetType().Name + "." + enumeration;
        }
        if (value is IEnumerable sequence and not string)
        {
            return sequence.Cast<object?>().Select(Normalize).ToArray();
        }
        return value;
    }

    private static bool IsEmpty(object? value) =>
        value is null || value is ICollection { Count: 0 };

    private static object?[] NormalizeStatements(IEnumerable<TSqlStatement> statements)
    {
        var result = new List<object?>();
        foreach (var statement in statements)
        {
            if (statement is BeginEndBlockStatement block)
            {
                // BEGIN/END groups statements but introduces no variable scope; batches are never flattened.
                result.AddRange(NormalizeStatements(block.StatementList.Statements));
            }
            else
            {
                result.Add(Normalize(statement));
            }
        }
        return result.ToArray();
    }

    private static FrozenData Load()
    {
        var profileBytes = ReadOffline("tools/tsql_compat/core-profile.json", "Cyqwel.Tests.TSqlCore.Profile.json");
        var fixtureBytes = ReadOffline("tests/Cyqwel.Tests/Fixtures/TSqlCore/sqlglot.json", "Cyqwel.Tests.TSqlCore.Corpus.json");
        var licenseBytes = ReadOffline("tests/Cyqwel.Tests/Fixtures/TSqlCore/licenses/SQLGlot.LICENSE", "Cyqwel.Tests.TSqlCore.SQLGlot.LICENSE");
        Assert.Equal(ProfileHash, Hash(profileBytes));
        Assert.Equal(FixtureHash, Hash(fixtureBytes));
        Assert.Equal(LicenseHash, Hash(licenseBytes));
        var profile = JsonSerializer.Deserialize<CoreProfile>(profileBytes, CampaignJson.Options)
            ?? throw new InvalidDataException("The frozen core profile is null.");
        var fixtures = JsonSerializer.Deserialize<CoreFixtures>(fixtureBytes, CampaignJson.Options)
            ?? throw new InvalidDataException("The offline core fixture file is null.");
        return new FrozenData(profile, fixtures.Cases.ToDictionary(c => c.Id), profile.Entries.ToDictionary(e => e.Id));
    }

    private static byte[] ReadOffline(string relativePath, string resourceName)
    {
        using var resource = typeof(TSqlCoreFixtureTests).Assembly.GetManifestResourceStream(resourceName);
        if (resource is not null)
        {
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            return buffer.ToArray();
        }

        var configuredRoot = Environment.GetEnvironmentVariable("CYQWEL_TSQL_CORE_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            if (!File.Exists(Path.Combine(configuredRoot, "Cyqwel.slnx")))
            {
                throw new DirectoryNotFoundException("CYQWEL_TSQL_CORE_ROOT must identify the campaign checkout.");
            }
            return File.ReadAllBytes(Path.Combine(configuredRoot, relativePath));
        }

        // Normal checkout-based CI needs no project edits; isolated or packaged tests can use the root or resources.
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Cyqwel.slnx")))
            {
                directory = directory.Parent;
            }
            if (directory is not null)
            {
                return File.ReadAllBytes(Path.Combine(directory.FullName, relativePath));
            }
        }
        throw new FileNotFoundException($"Missing embedded resource {resourceName} and repository fixture {relativePath}.");
    }

    private static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string HashIds(IEnumerable<string> ids) =>
        Hash(string.Concat(ids.Order(StringComparer.Ordinal).Select(id => id + "\n")));

    private sealed class IdentityRewriter : SqlRewriter;
    private sealed class BooleanReferenceValues : TSqlFragmentVisitor
    {
        public List<(Identifier Identifier, string Integer)> Booleans { get; } = [];
        public List<string> Integers { get; } = [];

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            if (node.ColumnType != ColumnType.Regular || node.MultiPartIdentifier?.Identifiers.Count != 1)
            {
                return;
            }
            var identifier = node.MultiPartIdentifier.Identifiers[0];
            if (identifier.QuoteType != QuoteType.NotQuoted)
            {
                return;
            }
            var integer = identifier.Value.ToUpperInvariant() switch { "TRUE" => "1", "FALSE" => "0", _ => null };
            if (integer is not null)
            {
                Booleans.Add((identifier, integer));
            }
        }

        public override void ExplicitVisit(IntegerLiteral node) => Integers.Add(node.Value);
    }

    private sealed record FrozenData(
        CoreProfile Profile, IReadOnlyDictionary<string, CoreFixture> Cases,
        IReadOnlyDictionary<string, CoreEntry> Entries);
    private sealed record CoreProfile(
        int OriginalCount, int DerivedCount, int CoreCount, int ContextSensitiveCount,
        string CoreIdsSha256, string OriginalIdsSha256, string AllIdsSha256,
        CoreSource Source, IReadOnlyList<string> CoreIds, IReadOnlyList<CoreEntry> Entries,
        IReadOnlyDictionary<string, int> OriginalDispositions,
        IReadOnlyDictionary<string, int> DerivedDispositions);
    private sealed record CoreSource(
        string Source, string Repository, string Revision, string License,
        string UpstreamLicensePath, string LicenseSha256);
    private sealed record CoreEntry(
        string Id, string SourceId, string Kind, string SqlSha256, string Disposition,
        IReadOnlyList<string> Requirements, string Reason, bool ReferenceAccepted,
        IReadOnlyList<string> ReferenceStatementTypes, int ReferenceBatchCount);
    private sealed record CoreFixtures(IReadOnlyList<CoreFixture> Cases);
    private sealed record CoreFixture(
        string Id, string SourceId, string Source, string Path, int Line, string Sql,
        string Context, string Kind, string SqlSha256, JsonElement? Derivation);
}
