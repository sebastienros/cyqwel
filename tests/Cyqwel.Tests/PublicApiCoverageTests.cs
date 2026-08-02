using Cyqwel.Ast;
using Cyqwel.Dialects;

namespace Cyqwel.Tests;

public class PublicApiCoverageTests
{
    [Fact]
    public void Select_builder_covers_all_fluent_clauses()
    {
        var derived = Sql.Select("id").From("source").Build();
        var cte = Sql.Select("id").From("history").Build();
        var builder = Sql.SelectItems(
                new SelectItem(Sql.Col("d.id"), "identifier"),
                new SelectItem(Sql.CountStar(), "total"))
            .Distinct()
            .From(derived, "d")
            .Join("inner_table", Sql.Col("d.id").EqualTo(Sql.Col("inner_table.id")))
            .LeftJoin("left_table", Sql.Col("d.id").EqualTo(Sql.Col("left_table.id")), "l")
            .RightJoin("right_table", Sql.Col("d.id").EqualTo(Sql.Col("right_table.id")))
            .FullJoin("full_table", Sql.Col("d.id").EqualTo(Sql.Col("full_table.id")))
            .CrossJoin("cross_table", "c")
            .Where(Sql.Col("d.active").EqualTo(Sql.Lit(true)))
            .AndWhere(Sql.Col("d.score").GreaterThan(Sql.Lit(10)))
            .GroupBy(Sql.Col("d.id"))
            .Having(Sql.CountStar().GreaterThan(Sql.Lit(1)))
            .OrderBy(Sql.Col("d.id"), OrderDirection.Descending, NullOrder.Last)
            .ThenBy(Sql.Col("d.name"))
            .Limit(Sql.Lit(25))
            .Offset(Sql.Lit(5))
            .Top(Sql.Lit(100))
            .With("history_cte", cte, "id");

        var query = builder.Build();

        Assert.True(query.IsDistinct);
        Assert.IsType<JoinTable>(query.From);
        Assert.IsType<BinaryExpression>(query.Where);
        Assert.Single(query.GroupBy!);
        Assert.NotNull(query.Having);
        Assert.Equal(2, query.OrderBy!.Count);
        Assert.Single(query.CommonTableExpressions!);
        Assert.Equal(25, Assert.IsType<int>(Assert.IsType<LiteralExpression>(query.Limit).Value));
        Assert.Equal(5, Assert.IsType<int>(Assert.IsType<LiteralExpression>(query.Offset).Value));
        Assert.Equal(100, Assert.IsType<int>(Assert.IsType<LiteralExpression>(query.Top).Value));
    }

    [Fact]
    public void Select_and_set_builders_cover_overloads_and_operations()
    {
        var selectedExpressions = Sql.Select(Sql.Col("id"), Sql.Func("UPPER", Sql.Col("name")))
            .From("users")
            .Limit(10)
            .Offset(2)
            .Top(20);
        var selectedStrings = Sql.Select("id", "name").From("archive");

        var union = selectedExpressions.Union(selectedStrings, all: true)
            .Union(Sql.Select("id", "name").From("older"))
            .OrderBy(Sql.Col("id"))
            .Limit(5)
            .Offset(1);
        var intersect = Sql.Select("id").From("a")
            .Intersect(Sql.Select("id").From("b"), all: true);
        var except = Sql.Select("id").From("a")
            .Except(Sql.Select("id").From("b"));

        Assert.Contains("UNION ALL", union.ToSql());
        Assert.Equal(SetOperator.Intersect, intersect.Build().Operator);
        Assert.Equal(SetOperator.Except, except.Build().Operator);
    }

    [Fact]
    public void Select_builder_covers_initial_and_validation_paths()
    {
        Assert.Throws<ArgumentException>(() => Sql.Select(Array.Empty<string>()));
        Assert.Throws<InvalidOperationException>(() =>
            Sql.Select("id").Join("other", Sql.Lit(true)));
        Assert.Throws<ArgumentNullException>(() => Sql.Select("id").Where(null!));
        Assert.Throws<ArgumentNullException>(() => Sql.Select("id").AndWhere(null!));
        Assert.Throws<ArgumentNullException>(() => Sql.Select("id").Having(null!));
        Assert.Throws<ArgumentNullException>(() => Sql.Select("id").Limit(null!));
        Assert.Throws<ArgumentNullException>(() => Sql.Select("id").Offset(null!));
        Assert.Throws<ArgumentNullException>(() => Sql.Select("id").Top(null!));

        var predicate = Sql.Col("active").EqualTo(Sql.Lit(true));
        var cte = Sql.Select("id").From("source").Build();
        var query = Sql.Select("id")
            .Distinct(false)
            .AndWhere(predicate)
            .ThenBy(Sql.Col("id"))
            .With("first", cte)
            .With("second", cte, "id")
            .Build();

        Assert.Same(predicate, query.Where);
        Assert.Single(query.OrderBy!);
        Assert.Null(query.CommonTableExpressions![0].Columns);
        Assert.Single(query.CommonTableExpressions[1].Columns!);
    }

    [Fact]
    public void Mutation_builders_cover_success_and_validation_paths()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.InsertInto("users").Build());
        Assert.Throws<ArgumentException>(() => Sql.InsertInto("users").Columns("id").Values(1, 2));
        Assert.Throws<ArgumentNullException>(() => Sql.InsertInto("users").From(null!));

        var valuesInsert = Sql.InsertInto("users")
            .Columns("id")
            .Values(1)
            .Returning(Sql.Col("id"));
        Assert.Single(valuesInsert.Build().Returning!);
        Assert.Throws<InvalidOperationException>(() => valuesInsert.From(Sql.Select("id").From("source").Build()));

        var queryInsert = Sql.InsertInto("users")
            .Columns("id")
            .From(Sql.Select("id").From("source").Build());
        Assert.Throws<InvalidOperationException>(() => queryInsert.Values(1));
        Assert.NotNull(queryInsert.Build().Source);
        Assert.Single(Sql.InsertInto("log").Values(1, "created").Build().Values!);

        Assert.Throws<InvalidOperationException>(() => Sql.Update("users").Build());
        Assert.Throws<ArgumentNullException>(() => Sql.Update("users").Where(null!));
        var update = Sql.Update("users", "u")
            .Set("name", "Ada")
            .Set("score", Sql.Col("score").Add(Sql.Lit(1)))
            .Where(Sql.Col("id").EqualTo(Sql.Param("id")))
            .Returning(Sql.Col("id"))
            .Build();
        Assert.Equal(2, update.Assignments.Count);
        Assert.Single(update.Returning!);

        Assert.Throws<ArgumentNullException>(() => Sql.DeleteFrom("users").Where(null!));
        var delete = Sql.DeleteFrom("users", "u")
            .Where(Sql.Col("id").EqualTo(Sql.Lit(1)))
            .Returning(Sql.Col("id"))
            .Build();
        Assert.Single(delete.Returning!);

        Assert.Throws<InvalidOperationException>(() => Sql.Case().Build());
        Assert.Throws<ArgumentNullException>(() => Sql.Case().When(null!, 1));
        var @case = Sql.Case(Sql.Col("status"))
            .When(Sql.Lit("active").EqualTo(Sql.Lit("active")), 1)
            .Else(0)
            .Build();
        Assert.NotNull(@case.Operand);
    }

    [Fact]
    public void Expression_helpers_cover_every_operator()
    {
        var left = Sql.Col("value");
        var right = Sql.Lit(1);
        var expressions = new SqlExpression[]
        {
            left.EqualTo(right),
            left.NotEqualTo(right),
            left.GreaterThan(right),
            left.GreaterThanOrEqualTo(right),
            left.LessThan(right),
            left.LessThanOrEqualTo(right),
            left.And(right),
            left.Or(right),
            left.Add(right),
            left.Subtract(right),
            left.Multiply(right),
            left.Divide(right),
            left.Like(right),
            left.ILike(right),
            left.Not(),
            left.IsNull(),
            left.IsNotNull(),
            left.Between(Sql.Lit(0), right),
            left.NotBetween(Sql.Lit(0), right),
            left.In(Sql.Lit(1), Sql.Lit(2)),
            left.NotIn(Sql.Lit(1), Sql.Lit(2)),
            Sql.Star(),
            Sql.Count(left),
            Sql.Count(left, distinct: true),
            Sql.Cast(left, "DECIMAL", 10, 2),
        };

        Assert.Equal(25, expressions.Length);
        Assert.Equal(BinaryOperator.NotEqual, Assert.IsType<BinaryExpression>(expressions[1]).Operator);
        Assert.True(Assert.IsType<BetweenExpression>(expressions[18]).IsNegated);
        Assert.True(Assert.IsType<InExpression>(expressions[20]).IsNegated);
        Assert.True(Assert.IsType<FunctionCallExpression>(expressions[23]).IsDistinct);
    }

    [Fact]
    public void Dialect_registry_covers_lookup_registration_and_removal()
    {
        Assert.Same(SqlDialects.TSql, SqlDialectRegistry.Get("mssql"));
        Assert.Same(SqlDialects.PostgreSql, SqlDialectRegistry.Get("postgres"));
        Assert.True(SqlDialectRegistry.TryGet("mysql", out var mySql));
        Assert.Same(SqlDialects.MySql, mySql);
        Assert.False(SqlDialectRegistry.TryGet("missing", out _));
        Assert.Throws<KeyNotFoundException>(() => SqlDialectRegistry.Get("missing"));
        Assert.Throws<InvalidOperationException>(() => SqlDialectRegistry.Unregister("generic"));

        var name = $"coverage-{Guid.NewGuid():N}";
        var dialect = SqlDialectBuilder.Create(name)
            .BasedOn(SqlDialects.PostgreSql)
            .Register();

        try
        {
            Assert.Same(dialect, SqlDialectRegistry.Get(name));
            Assert.Contains(dialect, SqlDialectRegistry.All);
            Assert.Throws<InvalidOperationException>(() => SqlDialectRegistry.Register(dialect));
        }
        finally
        {
            Assert.True(SqlDialectRegistry.Unregister(name));
        }

        Assert.False(SqlDialectRegistry.Unregister(name));
    }
}
