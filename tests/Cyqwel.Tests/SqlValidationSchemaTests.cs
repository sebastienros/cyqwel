using Cyqwel.Validation;

namespace Cyqwel.Tests;

public class SqlValidationSchemaTests
{
    private static readonly SqlSchemaCatalog Catalog = ValidationTestCatalog.Create();

    [Fact]
    public void Known_tables_and_columns_are_valid()
    {
        var result = SqlValidator.Validate(
            "SELECT id, name, email FROM users",
            Catalog);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Unknown_tables_are_errors_in_strict_mode()
    {
        var result = SqlValidator.Validate("SELECT * FROM missing", Catalog);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.UnknownTable
            && diagnostic.Severity == SqlValidationSeverity.Error);
    }

    [Fact]
    public void Unknown_columns_are_warnings_in_non_strict_mode()
    {
        var result = SqlValidator.Validate(
            "SELECT missing FROM users",
            Catalog,
            options: new SqlSchemaValidationOptions { Strict = false });

        Assert.True(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.UnknownColumn
            && diagnostic.Severity == SqlValidationSeverity.Warning);
    }

    [Fact]
    public void Qualified_columns_and_table_aliases_resolve()
    {
        var result = SqlValidator.Validate(
            "SELECT u.id, o.total FROM users u JOIN orders o ON u.id = o.user_id",
            Catalog);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Unknown_qualifiers_are_reported()
    {
        var result = SqlValidator.Validate(
            "SELECT q.id FROM users u",
            Catalog);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.UnresolvedReference);
    }

    [Fact]
    public void Explicit_schema_names_do_not_fall_back_to_another_schema()
    {
        var catalog = new SqlSchemaCatalog(
            new SqlTableSchema("users", [new("id", "integer")], Schema: "public"));

        var known = SqlValidator.Validate("SELECT id FROM public.users", catalog);
        var unknown = SqlValidator.Validate("SELECT id FROM private.users", catalog);

        Assert.True(known.IsValid);
        Assert.Contains(unknown.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.UnknownTable);
    }

    [Fact]
    public void Cte_projected_aliases_resolve()
    {
        var result = SqlValidator.Validate(
            "WITH selected AS (SELECT id AS user_id FROM users) SELECT user_id FROM selected",
            Catalog);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Unknown_cte_columns_are_reported()
    {
        var result = SqlValidator.Validate(
            "WITH selected AS (SELECT id AS user_id FROM users) SELECT missing FROM selected",
            Catalog);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.UnknownColumn);
    }

    [Fact]
    public void Cte_declared_column_count_is_checked()
    {
        var result = SqlValidator.Validate(
            "WITH selected(one, two) AS (SELECT id FROM users) SELECT one FROM selected",
            Catalog);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.CteColumnCountMismatch);
    }

    [Fact]
    public void Derived_table_columns_resolve()
    {
        var result = SqlValidator.Validate(
            "SELECT d.user_id FROM (SELECT id AS user_id FROM users) d",
            Catalog);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Recursive_ctes_without_declared_columns_self_resolve()
    {
        var result = SqlValidator.Validate(
            """
            WITH RECURSIVE numbers AS (
                SELECT 1 AS n
                UNION ALL
                SELECT n + 1 FROM numbers WHERE n < 3
            )
            SELECT n FROM numbers
            """,
            Catalog);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Scalar_subqueries_must_project_one_column()
    {
        var result = SqlValidator.Validate(
            "SELECT (SELECT id, name FROM users) FROM users",
            Catalog);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidScalarSubquery);
    }

    [Fact]
    public void Globally_known_columns_can_be_validated_without_from()
    {
        var catalog = new SqlSchemaCatalog(
            new SqlTableSchema("settings", [new("value", "varchar")]));

        var result = SqlValidator.Validate("SELECT value", catalog);

        Assert.True(result.IsValid);
    }
}
