using Cyqwel.Ast;
using Parlot.Fluent;
using static Parlot.Fluent.Parsers;

namespace Cyqwel.Parsing;

public static partial class SqlParser
{
    private static Parser<IReadOnlyList<TSqlQueryOption>?> CreateTSqlQueryOptions(SqlDialectParserOptions syntax)
    {
        if (!syntax.SupportsTSqlExtensions) return Always<IReadOnlyList<TSqlQueryOption>?>(null);

        var integer = Terms.Integer().When((_, value) => value >= 0 && value <= int.MaxValue)
            .Then(value => (int)value);
        var option = Keyword("RECOMPILE").Then(new TSqlQueryOption(TSqlQueryOptionKind.Recompile))
            .Or(Keyword("MAXDOP").SkipAnd(integer).Then(value => new TSqlQueryOption(TSqlQueryOptionKind.MaxDop, value)))
            .Or(Keyword("MAXRECURSION").SkipAnd(integer).Then(value => new TSqlQueryOption(TSqlQueryOptionKind.MaxRecursion, value)))
            .Or(Keyword("FAST").SkipAnd(integer).Then(value => new TSqlQueryOption(TSqlQueryOptionKind.Fast, value)))
            .Or(Keyword("OPTIMIZE").AndSkip(Keyword("FOR")).AndSkip(Keyword("UNKNOWN"))
                .Then(new TSqlQueryOption(TSqlQueryOptionKind.OptimizeForUnknown)))
            .Or(Keyword("FORCE").AndSkip(Keyword("ORDER")).Then(new TSqlQueryOption(TSqlQueryOptionKind.ForceOrder)));
        return Keyword("OPTION").SkipAnd(Between(Terms.Char('('), Separated(Terms.Char(','), option), Terms.Char(')')))
            .When((_, options) => options.All(value => value.IsValid)
                && options.Select(value => value.Kind).Distinct().Count() == options.Count)
            .Then<IReadOnlyList<TSqlQueryOption>?>(value => value)
            .Or(Always<IReadOnlyList<TSqlQueryOption>?>(null));
    }

    private static Parser<TSqlResultFormat?> CreateTSqlResultFormat(
        SqlDialectParserOptions syntax, Parser<SqlExpression> stringLiteral)
    {
        if (!syntax.SupportsTSqlExtensions) return Always<TSqlResultFormat?>(null);

        var text = stringLiteral.Then(value => (LiteralExpression)value);
        var argument = Between(Terms.Char('('), text, Terms.Char(')'));
        var root = Keyword("ROOT").SkipAnd(argument.Optional())
            .Then(value => new ParsedFormatOption("ROOT", value.HasValue ? value.Value : null));
        var jsonOption = root
            .Or(Keyword("INCLUDE_NULL_VALUES").Then(new ParsedFormatOption("INCLUDE_NULL_VALUES")))
            .Or(Keyword("WITHOUT_ARRAY_WRAPPER").Then(new ParsedFormatOption("WITHOUT_ARRAY_WRAPPER")));
        var json = Keyword("JSON").SkipAnd(
                Keyword("AUTO").Then(TSqlResultFormatMode.Auto).Or(Keyword("PATH").Then(TSqlResultFormatMode.Path)))
            .And(ZeroOrMany(Terms.Char(',').SkipAnd(jsonOption)))
            .When((_, value) => value.Item2.Select(option => option.Kind).Distinct().Count() == value.Item2.Count)
            .Then(value => ApplyFormatOptions(new TSqlResultFormat(TSqlResultFormatKind.Json, value.Item1), value.Item2));
        var xmlOption = root.Or(Keyword("TYPE").Then(new ParsedFormatOption("TYPE")));
        var xmlMode = Keyword("AUTO").Then(new TSqlResultFormat(TSqlResultFormatKind.Xml, TSqlResultFormatMode.Auto))
            .Or(Keyword("PATH").Then(TSqlResultFormatMode.Path).Or(Keyword("RAW").Then(TSqlResultFormatMode.Raw))
                .And(argument.Optional()).Then(value => new TSqlResultFormat(TSqlResultFormatKind.Xml,
                    value.Item1, value.Item2.HasValue ? value.Item2.Value : null)));
        var xml = Keyword("XML").SkipAnd(xmlMode)
            .And(ZeroOrMany(Terms.Char(',').SkipAnd(xmlOption)))
            .When((_, value) => value.Item2.Select(option => option.Kind).Distinct().Count() == value.Item2.Count)
            .Then(value => ApplyFormatOptions(value.Item1, value.Item2));
        return Keyword("FOR").SkipAnd(json.Or(xml)).When((_, value) => value.IsValid)
            .Then<TSqlResultFormat?>(value => value)
            .Or(Always<TSqlResultFormat?>(null));
    }

    private static TSqlResultFormat ApplyFormatOptions(TSqlResultFormat format, IReadOnlyList<ParsedFormatOption> options)
    {
        foreach (var option in options)
        {
            format = option.Kind switch
            {
                "ROOT" => format with { HasRoot = true, Root = option.Value },
                "INCLUDE_NULL_VALUES" => format with { IncludeNullValues = true },
                "WITHOUT_ARRAY_WRAPPER" => format with { WithoutArrayWrapper = true },
                "TYPE" => format with { Type = true },
                _ => throw new ArgumentOutOfRangeException(nameof(options)),
            };
        }
        return format;
    }

    private sealed record ParsedFormatOption(string Kind, LiteralExpression? Value = null);
}
