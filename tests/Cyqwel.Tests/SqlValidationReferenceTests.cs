using Cyqwel.Validation;

namespace Cyqwel.Tests;

public class SqlValidationReferenceTests
{
    [Fact]
    public void Valid_foreign_key_metadata_is_accepted()
    {
        var result = SqlValidator.Validate(
            "SELECT 1",
            ValidationTestCatalog.Create(withRelationship: true),
            options: new SqlSchemaValidationOptions { CheckReferences = true });

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Invalid_foreign_key_targets_are_reported()
    {
        var catalog = new SqlSchemaCatalog(
            new SqlTableSchema(
                "orders",
                [
                    new(
                        "user_id",
                        "integer",
                        References: new SqlColumnReference("missing_users", "id")),
                ]));

        var result = SqlValidator.Validate(
            "SELECT 1",
            catalog,
            options: new SqlSchemaValidationOptions { CheckReferences = true });

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidForeignKeyReference);
    }

    [Fact]
    public void Invalid_foreign_keys_warn_in_non_strict_mode()
    {
        var catalog = new SqlSchemaCatalog(
            new SqlTableSchema(
                "orders",
                [
                    new(
                        "user_id",
                        "integer",
                        References: new SqlColumnReference("missing_users", "id")),
                ]));

        var result = SqlValidator.Validate(
            "SELECT 1",
            catalog,
            options: new SqlSchemaValidationOptions
            {
                CheckReferences = true,
                Strict = false,
            });

        Assert.True(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.WeakReferenceIntegrity
            && diagnostic.Severity == SqlValidationSeverity.Warning);
    }

    [Fact]
    public void Ambiguous_unqualified_columns_are_reported()
    {
        var result = SqlValidator.Validate(
            "SELECT id FROM users JOIN orders ON users.id = orders.user_id",
            ValidationTestCatalog.Create(),
            options: new SqlSchemaValidationOptions { CheckReferences = true });

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.AmbiguousColumnReference);
    }

    [Fact]
    public void Cartesian_joins_warn()
    {
        var result = SqlValidator.Validate(
            "SELECT users.id FROM users CROSS JOIN orders",
            ValidationTestCatalog.Create(),
            options: new SqlSchemaValidationOptions { CheckReferences = true });

        Assert.True(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.CartesianJoin);
    }

    [Fact]
    public void Joins_using_declared_relationships_do_not_warn()
    {
        var result = SqlValidator.Validate(
            "SELECT u.id FROM users u JOIN orders o ON u.id = o.user_id",
            ValidationTestCatalog.Create(withRelationship: true),
            options: new SqlSchemaValidationOptions { CheckReferences = true });

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.JoinNotUsingDeclaredReference);
    }

    [Fact]
    public void Joins_ignoring_declared_relationships_warn()
    {
        var result = SqlValidator.Validate(
            "SELECT u.id FROM users u JOIN orders o ON u.age = o.total",
            ValidationTestCatalog.Create(withRelationship: true),
            options: new SqlSchemaValidationOptions { CheckReferences = true });

        Assert.True(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.JoinNotUsingDeclaredReference);
    }
}
