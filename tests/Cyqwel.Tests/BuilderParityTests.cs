using Cyqwel.Ast;
using Cyqwel.Dialects;

namespace Cyqwel.Tests;

public class BuilderParityTests
{
    [Fact]
    public void Builder_generated_concepts_are_parseable_by_a_supported_dialect()
    {
        var advancedQuery = Sql.SelectItems(
                new SelectItem(Sql.Star("sales")),
                new SelectItem(
                    Sql.Case(Sql.Col("status"))
                        .When(Sql.Lit("active"), 1)
                        .Else(0)
                        .Build(),
                    "status_code"),
                new SelectItem(
                    Sql.Func("PERCENTILE_CONT", Sql.Lit(0.5m))
                        .WithinGroupBy(Sql.Order(Sql.Col("amount")))
                        .FilterWhere(Sql.Col("amount").GreaterThan(Sql.Lit(0)))
                        .Over(
                            "regional",
                            partitionBy: null,
                            orderBy: [Sql.Order(Sql.Col("created_at"))]),
                    "regional_percentile"),
                new SelectItem(Sql.Interval(1, "DAY"), "one_day"),
                new SelectItem(Sql.Row(Sql.Lit(1), Sql.Lit(2)), "pair"),
                new SelectItem(Sql.NextValue("report_sequence"), "report_id"))
            .From("sales")
            .With(
                "seed",
                Sql.Select(Sql.Lit(1)).Build(),
                CteMaterialization.Materialized,
                "id")
            .Recursive()
            .Window("base_window", partitionBy: [Sql.Col("region")])
            .Window(
                "regional",
                baseWindow: "base_window",
                orderBy: [Sql.Order(Sql.Col("amount"), OrderDirection.Descending)])
            .Qualify(Sql.Col("regional_percentile").GreaterThan(Sql.Lit(10)))
            .ConnectBy(
                Sql.Col("id").Prior().EqualTo(Sql.Col("parent_id")),
                Sql.Col("parent_id").IsNull(),
                noCycle: true)
            .OrderSiblingsBy(Sql.Col("id"))
            .Build();

        var groupedSet = Sql.Values(1)
            .Limit(1)
            .Union(Sql.Values(2).Build())
            .With("set_seed", Sql.Values(0).Build())
            .Build();
        var source = Sql.Select("id", "name").From("incoming").Build();
        var createTable = Sql.CreateTable("children")
            .Column("id", "INT")
            .Column("parent_id", "INT")
            .Constraint(Sql.ForeignKey(
                ["parent_id"],
                "parents",
                ["id"],
                ReferentialAction.SetNull,
                ReferentialAction.Cascade,
                "fk_children_parent"))
            .Build();

        var genericSamples = new (string Name, SqlNode Node)[]
        {
            ("qualified star", Sql.Select(Sql.Star("sales")).Build()),
            ("simple case", Sql.Select(
                Sql.Case(Sql.Col("status")).When(Sql.Lit("active"), 1).Else(0).Build()).Build()),
            ("within group", Sql.Select(
                Sql.Func("PERCENTILE_CONT", Sql.Lit(0.5m))
                    .WithinGroupBy(Sql.Order(Sql.Col("amount")))).Build()),
            ("function filter", Sql.Select(
                Sql.Func("SUM", Sql.Col("amount"))
                    .FilterWhere(Sql.Col("amount").GreaterThan(Sql.Lit(0)))).Build()),
            ("named window", Sql.Select(Sql.Func("SUM", Sql.Col("amount")).Over("regional"))
                .Window("regional", partitionBy: [Sql.Col("region")])
                .Build()),
            ("augmented window", Sql.Select(
                    Sql.Func("SUM", Sql.Col("amount")).Over(
                        "regional",
                        partitionBy: null,
                        orderBy: [Sql.Order(Sql.Col("created_at"))]))
                .Window("regional", partitionBy: [Sql.Col("region")])
                .Build()),
            ("advanced function", Sql.Select(
                Sql.Func("PERCENTILE_CONT", Sql.Lit(0.5m))
                    .WithinGroupBy(Sql.Order(Sql.Col("amount")))
                    .FilterWhere(Sql.Col("amount").GreaterThan(Sql.Lit(0)))
                    .Over(
                        "regional",
                        partitionBy: null,
                        orderBy: [Sql.Order(Sql.Col("created_at"))]))
                .Window("regional", partitionBy: [Sql.Col("region")])
                .Build()),
            ("interval", Sql.Select(Sql.Interval(1, "DAY")).Build()),
            ("row", Sql.Select(Sql.Row(Sql.Lit(1), Sql.Lit(2))).Build()),
            ("sequence value", Sql.Select(Sql.NextValue("report_sequence")).Build()),
            ("nested unary", Sql.Select(Sql.Col("amount").Negate().Negate()).Build()),
            ("nested predicate", Sql.Select(Sql.Col("amount").IsNull().IsNull()).Build()),
            ("base window", Sql.Select(Sql.Func("ROW_NUMBER").Over("regional"))
                .Window("base_window", partitionBy: [Sql.Col("region")])
                .Window("regional", baseWindow: "base_window", orderBy: [Sql.Order(Sql.Col("amount"))])
                .Build()),
            ("hierarchy", Sql.Select("id")
                .From("nodes")
                .ConnectBy(
                    Sql.Col("id").Prior().EqualTo(Sql.Col("parent_id")),
                    Sql.Col("parent_id").IsNull(),
                    noCycle: true)
                .OrderSiblingsBy(Sql.Col("id"))
                .Build()),
            ("materialized cte", Sql.Select(Sql.Lit(1))
                .With(
                    "seed",
                    Sql.Select(Sql.Lit(1)).Build(),
                    CteMaterialization.Materialized,
                    "id")
                .Recursive()
                .Build()),
            ("advanced query", advancedQuery),
            ("grouped set", groupedSet),
            ("explain", Sql.Explain(groupedSet).Analyze().Parenthesized().Build()),
            ("document", Sql.Document(
                Sql.Select(Sql.Lit(1)).Build(),
                Sql.Select(Sql.Lit(2)).Build())),
            ("insert", Sql.InsertInto("users")
                .Columns("id", "name")
                .From(source)
                .Returning(Sql.Col("id"))
                .ReturningInto(Sql.Param("inserted_id"))
                .Build()),
            ("update", Sql.Update("users", "u")
                .Set("name", Sql.Col("i.name"))
                .From(source, "i")
                .Where(Sql.Col("u.id").EqualTo(Sql.Col("i.id")))
                .Returning(Sql.Col("u.id"))
                .ReturningInto(Sql.Param("updated_id"))
                .Build()),
            ("delete", Sql.DeleteFrom("users", "u")
                .Using("expired", "e")
                .Where(Sql.Col("u.id").EqualTo(Sql.Col("e.id")))
                .Returning(Sql.Col("u.id"))
                .ReturningInto(Sql.Param("deleted_id"))
                .Build()),
            ("merge", Sql.MergeInto("users", "u")
                .Using("incoming", "i")
                .On(Sql.Col("u.id").EqualTo(Sql.Col("i.id")))
                .WhenMatchedUpdate(Sql.Assign("name", Sql.Col("i.name")))
                .WhenNotMatchedInsert(["id", "name"], Sql.Col("i.id"), Sql.Col("i.name"))
                .WhenNotMatchedBySourceDelete()
                .Build()),
            ("create table", createTable),
            ("alter table", Sql.AlterTable("children")
                .AddColumn("name", "VARCHAR", 100)
                .AlterColumn("name", nullability: Nullability.NotNull)
                .RenameColumn("name", "display_name")
                .Build()),
            ("drop schema", Sql.Drop(SchemaObjectKind.Schema, "archive").IfExists().Cascade().Build()),
            ("truncate", Sql.Truncate("children").RestartIdentity().Cascade().Build()),
            ("create view", Sql.CreateView("child_view").As(source).Build()),
            ("create index", Sql.CreateIndex("ix_children_parent", "children")
                .Column("parent_id")
                .Where(Sql.Col("parent_id").IsNotNull())
                .Build()),
            ("create sequence", Sql.CreateSequence("child_ids")
                .StartWith(1)
                .IncrementBy(1)
                .Cycle(false)
                .Build()),
            ("alter sequence", Sql.AlterSequence("child_ids").IncrementBy(2).Cycle().Build()),
        };

        foreach (var (name, node) in genericSamples)
        {
            AssertParseable(name, node, SqlDialects.Generic);
        }

        AssertParseable(
            "top percent with ties",
            Sql.Select("id")
                .From("users")
                .OrderBy(Sql.Col("id"))
                .Top(10, percent: true, withTies: true)
                .Build(),
            SqlDialects.TSql);
        AssertParseable(
            "view security",
            Sql.CreateView("secure_view")
                .Security(ViewSecurity.Definer)
                .As(source)
                .Build(),
            SqlDialects.MySql);

        var parsedQuery = Assert.IsType<SelectStatement>(
            Assert.Single(SqlDialects.Generic.Parse(advancedQuery.ToSql()).Statements));
        Assert.NotNull(Assert.IsType<CaseExpression>(parsedQuery.Projections[1].Expression).Operand);
        Assert.Single(Assert.IsType<StarExpression>(parsedQuery.Projections[0].Expression).Qualifier!);
        Assert.Equal(
            "regional",
            Assert.IsType<WindowExpression>(parsedQuery.Projections[2].Expression).WindowName!.Value);
        Assert.Equal("base_window", parsedQuery.Windows![1].BaseWindow!.Value);

        var parsedCreate = Assert.IsType<CreateTableStatement>(
            Assert.Single(SqlDialects.Generic.Parse(createTable.ToSql()).Statements));
        var foreignKey = Assert.Single(parsedCreate.Elements.OfType<ForeignKeyConstraint>());
        Assert.Equal(ReferentialAction.SetNull, foreignKey.OnDelete);
        Assert.Equal(ReferentialAction.Cascade, foreignKey.OnUpdate);

        var parsedTruncate = Assert.IsType<TruncateStatement>(
            Assert.Single(SqlDialects.Generic.Parse(
                Sql.Truncate("children").RestartIdentity().Build().ToSql()).Statements));
        Assert.True(parsedTruncate.RestartIdentity);
    }

    [Fact]
    public void Select_builder_covers_advanced_query_clauses()
    {
        var cte = Sql.Select("id").From("source").Build();
        var frame = Sql.Frame(
            WindowFrameUnit.Rows,
            Sql.UnboundedPreceding(),
            Sql.CurrentRow());
        var rowNumber = Sql.Func("ROW_NUMBER").Over("regional");

        var query = Sql.SelectItems(new SelectItem(rowNumber, "position"))
            .From("sales")
            .With("regional_sales", cte, CteMaterialization.NotMaterialized, "id")
            .Recursive()
            .Window(
                "regional",
                partitionBy: [Sql.Col("region")],
                orderBy: [Sql.Order(Sql.Col("amount"), OrderDirection.Descending)],
                frame: frame)
            .Qualify(Sql.Col("position").LessThanOrEqualTo(Sql.Lit(3)))
            .Build();

        Assert.True(query.IsRecursive);
        Assert.Equal(CteMaterialization.NotMaterialized, query.CommonTableExpressions![0].Materialization);
        Assert.Single(query.Windows!);
        Assert.NotNull(query.Qualify);
        Assert.Equal("regional", Assert.IsType<WindowExpression>(query.Projections[0].Expression).WindowName!.Value);

        var sql = query.ToSql();
        Assert.Contains("WITH RECURSIVE regional_sales(id) AS NOT MATERIALIZED", sql);
        Assert.Contains("WINDOW regional AS (PARTITION BY region ORDER BY amount DESC ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)", sql);
        Assert.Contains("QUALIFY position <= 3", sql);
    }

    [Fact]
    public void Select_builder_covers_advanced_joins_hierarchies_and_top()
    {
        var query = Sql.Select("e.id")
            .From(Sql.Table("employees", "e"))
            .JoinUsing("departments", ["department_id"], "d", JoinKind.Left)
            .NaturalJoin("locations")
            .CommaJoin(Sql.Derived(Sql.Select("id").From("roles").Build(), "r"))
            .ConnectBy(
                new UnaryExpression(UnaryOperator.Prior, Sql.Col("e.id"))
                    .EqualTo(Sql.Col("e.manager_id")),
                Sql.Col("e.manager_id").IsNull(),
                noCycle: true)
            .OrderSiblingsBy(Sql.Col("e.id"))
            .Top(10, percent: true, withTies: true)
            .Build();

        Assert.True(query.IsTopPercent);
        Assert.True(query.WithTies);
        Assert.True(query.OrderSiblings);
        Assert.NotNull(query.ConnectBy);

        var genericSql = (query with
        {
            Top = null,
            IsTopPercent = false,
            WithTies = false,
        }).ToSql();
        Assert.Contains("LEFT JOIN departments AS d USING (department_id)", genericSql);
        Assert.Contains("NATURAL INNER JOIN locations", genericSql);
        Assert.Contains(", (SELECT id FROM roles) AS r", genericSql);
        Assert.Contains("CONNECT BY NOCYCLE PRIOR e.id = e.manager_id", genericSql);
        Assert.Contains("ORDER SIBLINGS BY e.id ASC", genericSql);

        var topSql = Sql.Select("id")
            .From("users")
            .OrderBy(Sql.Col("id"))
            .Top(10, percent: true, withTies: true)
            .ToSql(SqlDialects.TSql);
        Assert.Contains("TOP (10) PERCENT WITH TIES", topSql);
    }

    [Fact]
    public void Values_set_and_explain_builders_are_composable()
    {
        var set = Sql.Values(1, "one")
            .Row(2, "two")
            .Union(Sql.Select(Sql.Lit(3), Sql.Lit("three")).Build(), all: true)
            .Intersect(Sql.Values(3, "three").Build())
            .Except(Sql.Select(Sql.Lit(4), Sql.Lit("four")).Build())
            .OrderBy(Sql.Col("1"))
            .ThenBy(Sql.Col("2"), OrderDirection.Descending)
            .Limit(Sql.Param("limit"))
            .Offset(Sql.Lit(1))
            .With("seed", Sql.Values(0, "zero").Build())
            .Recursive()
            .Build();

        Assert.Equal(SetOperator.Except, set.Operator);
        Assert.True(set.IsRecursive);
        Assert.Equal(2, set.OrderBy!.Count);
        Assert.IsType<ParameterExpression>(set.Limit);
        Assert.Contains("UNION ALL", set.ToSql());
        Assert.Contains("INTERSECT", set.ToSql());
        Assert.Contains("EXCEPT", set.ToSql());

        var grouped = Sql.Values(1)
            .Limit(1)
            .Union(Sql.Values(2).Build())
            .ToSql();
        Assert.StartsWith("(VALUES (1) LIMIT 1) UNION", grouped);

        var nestedCtes = Sql.Select("id")
            .From("source")
            .With("inner_cte", Sql.Select("id").From("inner_source").Build())
            .Union(Sql.Select("id").From("other"))
            .With("outer_cte", Sql.Select("id").From("outer_source").Build())
            .ToSql();
        Assert.DoesNotContain(") WITH inner_cte", nestedCtes);
        Assert.Contains("(WITH inner_cte", nestedCtes);

        var explain = Sql.Explain(set)
            .Analyze()
            .Parenthesized()
            .Build();

        Assert.True(explain.Analyze);
        Assert.True(explain.IsQueryParenthesized);
    }

    [Fact]
    public void Dml_builders_cover_sources_returning_into_and_merge()
    {
        var source = Sql.Select("id", "name").From("incoming").Build();
        var insert = Sql.InsertInto("users")
            .Columns("id", "name")
            .From(source)
            .Returning(Sql.Col("id"))
            .ReturningInto(Sql.Param("inserted_id"))
            .Build();
        var update = Sql.Update("users", "u")
            .Set("name", Sql.Col("i.name"))
            .From(source, "i")
            .Where(Sql.Col("u.id").EqualTo(Sql.Col("i.id")))
            .Returning(Sql.Col("u.id"))
            .ReturningInto(Sql.Param("updated_id"))
            .Build();
        var delete = Sql.DeleteFrom("users", "u")
            .Using("expired", "e")
            .Where(Sql.Col("u.id").EqualTo(Sql.Col("e.id")))
            .Returning(Sql.Col("u.id"))
            .ReturningInto(Sql.Param("deleted_id"))
            .Build();
        var merge = Sql.MergeInto("users", "u")
            .Using("incoming", "i")
            .On(Sql.Col("u.id").EqualTo(Sql.Col("i.id")))
            .WhenMatchedUpdate(
                condition: null,
                deleteWhere: Sql.Col("i.deleted").EqualTo(Sql.Lit(true)),
                Sql.Assign("name", Sql.Col("i.name")))
            .WhenNotMatchedInsert(["id", "name"], Sql.Col("i.id"), Sql.Col("i.name"))
            .WhenNotMatchedBySourceDelete(Sql.Col("u.inactive").EqualTo(Sql.Lit(true)))
            .Returning(Sql.Col("u.id"))
            .Build();

        Assert.NotNull(insert.ReturningInto);
        Assert.IsType<DerivedTable>(update.From);
        Assert.IsType<NamedTable>(delete.Using);
        Assert.Equal(3, merge.WhenClauses.Count);
        Assert.NotNull(Assert.IsType<MergeUpdateAction>(merge.WhenClauses[0].Action).DeleteWhere);
        Assert.Contains("MERGE INTO users AS u", merge.ToSql());
        Assert.Contains("WHEN NOT MATCHED BY SOURCE", merge.ToSql());
    }

    [Fact]
    public void Ddl_builders_cover_every_supported_statement()
    {
        var createTable = Sql.CreateTable("users")
            .Temporary()
            .IfNotExists()
            .Column(Sql.DefineColumn(
                "id",
                new SqlDataType("INT"),
                Nullability.NotNull,
                identity: IdentityGeneration.Always,
                primaryKey: true))
            .Column("name", "VARCHAR", 100)
            .Constraint(Sql.Unique(["name"], "uq_users_name"))
            .Build();
        var alterTable = Sql.AlterTable("users")
            .AddColumn("email", "VARCHAR", 200)
            .AlterColumn("name", nullability: Nullability.NotNull)
            .AddConstraint(Sql.Check(Sql.Col("id").GreaterThan(Sql.Lit(0)), "ck_users_id"))
            .RenameColumn("email", "email_address")
            .Build();
        var drop = Sql.DropTable("users").And("archived_users").IfExists().Cascade().Build();
        var truncate = Sql.Truncate("users").And("audit").RestartIdentity().Cascade().Build();
        var view = Sql.CreateView("active_users")
            .OrReplace()
            .Columns("id")
            .As(Sql.Select("id").From("users").Build())
            .Build();
        var index = Sql.CreateIndex("ix_users_name", "users")
            .Unique()
            .IfNotExists()
            .Column("name", OrderDirection.Descending, NullOrder.Last)
            .Where(Sql.Col("name").IsNotNull())
            .Build();
        var createSequence = Sql.CreateSequence("user_ids")
            .IfNotExists()
            .StartWith(1)
            .IncrementBy(2)
            .MinValue(1)
            .MaxValue(1000)
            .Cache(20)
            .Cycle(false)
            .Build();
        var alterSequence = Sql.AlterSequence("user_ids")
            .IncrementBy(5)
            .Cycle()
            .Build();

        Assert.Contains("CREATE TEMPORARY TABLE IF NOT EXISTS users", createTable.ToSql());
        Assert.Equal(4, alterTable.Actions.Count);
        Assert.Equal(2, drop.Names.Count);
        Assert.True(truncate.RestartIdentity);
        Assert.True(view.OrReplace);
        Assert.True(index.IsUnique);
        Assert.False(createSequence.Options.Cycle);
        Assert.True(alterSequence.Options.Cycle);
    }

    [Fact]
    public void New_builders_reject_incomplete_statements()
    {
        Assert.Throws<InvalidOperationException>(() => Sql.CreateTable("empty").Build());
        Assert.Throws<InvalidOperationException>(() => Sql.AlterTable("users").Build());
        Assert.Throws<InvalidOperationException>(() => Sql.CreateView("users_view").Build());
        Assert.Throws<InvalidOperationException>(() => Sql.CreateIndex("ix", "users").Build());
        Assert.Throws<InvalidOperationException>(() => Sql.AlterSequence("sequence").Build());
        Assert.Throws<InvalidOperationException>(() => Sql.MergeInto("users").Build());
        Assert.Throws<ArgumentException>(() => Sql.Values(1).Row(2, 3));
        Assert.Throws<ArgumentException>(() => Sql.InsertInto("users").Values(1).Values(2, 3));
        Assert.Throws<ArgumentException>(() => Sql.InsertInto("users").Values(1).Columns("a", "b"));
        Assert.Throws<ArgumentException>(() => Sql.InsertInto("users").Values());
        Assert.Throws<ArgumentException>(() => Sql.AlterTable("users").AlterColumn("name"));
        Assert.Throws<ArgumentException>(() =>
            Sql.AlterTable("users").AlterColumn(
                "name",
                new SqlDataType("VARCHAR", 100),
                Nullability.NotNull));
        Assert.Throws<ArgumentException>(() => Sql.PrimaryKey([]));
        Assert.Throws<ArgumentException>(() =>
            Sql.ForeignKey(["tenant_id", "id"], "parent", ["id"]));
        Assert.Throws<InvalidOperationException>(() =>
            Sql.Select("id").Limit(5).Top(10, percent: true));
        Assert.Throws<InvalidOperationException>(() =>
            Sql.Select("id").Top(10, percent: true).Limit(5));
        Assert.Throws<InvalidOperationException>(() =>
            Sql.Select("id").Offset(5).Top(10, percent: true));
        Assert.Throws<InvalidOperationException>(() =>
            Sql.Select("id").Top(10, percent: true).Offset(5));
        Assert.Throws<InvalidOperationException>(() =>
            Sql.Select("id").Top(10, percent: false, withTies: true).Build());
        Assert.Throws<ArgumentException>(() =>
            Sql.DefineColumn(
                "id",
                new SqlDataType("INT"),
                defaultValue: Sql.Lit(1),
                identity: IdentityGeneration.Always));
        Assert.Throws<InvalidOperationException>(() =>
            Sql.InsertInto("users").Values(1).ReturningInto(Sql.Param("id")).Build());
        Assert.Throws<InvalidOperationException>(() =>
            Sql.Update("users")
                .Set("name", "Ada")
                .Returning(Sql.Col("id"), Sql.Col("name"))
                .ReturningInto(Sql.Param("id"))
                .Build());
        Assert.Throws<ArgumentException>(() =>
            Sql.Frame(
                WindowFrameUnit.Rows,
                Sql.UnboundedFollowing(),
                Sql.CurrentRow()));
        Assert.Throws<ArgumentException>(() =>
            Sql.Frame(
                WindowFrameUnit.Rows,
                Sql.CurrentRow(),
                Sql.UnboundedPreceding()));
        Assert.Throws<NotSupportedException>(() =>
            Sql.Values(1)
                .Limit(1)
                .Union(Sql.Values(2).Build())
                .ToSql(SqlDialects.Sqlite));
        Assert.Throws<ArgumentException>(() =>
            Sql.MergeInto("users")
                .Using("source")
                .On(Sql.Lit(true))
                .WhenNotMatchedInsert(["id"], 1, 2));
        Assert.Throws<ArgumentException>(() => Sql.Document());
        Assert.Throws<ArgumentException>(() => Sql.Row());
        Assert.Throws<ArgumentException>(() => Sql.Interval(true, "DAY"));
        Assert.Throws<ArgumentException>(() => Sql.Interval(double.NaN, "DAY"));
        Assert.Throws<ArgumentException>(() => Sql.Interval(double.PositiveInfinity, "DAY"));
        Assert.Throws<ArgumentException>(() => Sql.Lit(double.NaN));
        Assert.Throws<ArgumentException>(() => Sql.Lit(float.NegativeInfinity));
        Assert.Throws<ArgumentException>(() => Sql.Lit(1e100));
        Assert.Throws<ArgumentException>(() => Sql.Lit(Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => Sql.Lit(TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentException>(() => Sql.Interval(1e100, "SECOND"));
        Assert.Throws<ArgumentException>(() => Sql.Col("id").In());
        Assert.Throws<ArgumentException>(() => Sql.Col("id").NotIn());
        Assert.Throws<ArgumentException>(() => Sql.Star("catalog.schema.table"));
    }

    private static void AssertParseable(string name, SqlNode node, SqlDialect dialect)
    {
        var sql = node.ToSql(dialect);
        var parsed = dialect.TryParse(sql, out _, out var error);
        Assert.True(parsed, $"{name} generated SQL that {dialect.Name} cannot parse: {sql}{Environment.NewLine}{error}");
    }
}
