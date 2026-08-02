using Cyqwel.Validation;

namespace Cyqwel.Tests;

public class SqlValidationTypeTests
{
    private static readonly SqlSchemaCatalog Catalog = ValidationTestCatalog.Create();
    private static readonly SqlSchemaValidationOptions Options = new() { CheckTypes = true };

    [Theory]
    [InlineData("INT4", SqlTypeFamily.Integer)]
    [InlineData("double precision", SqlTypeFamily.Numeric)]
    [InlineData("VARCHAR(255)", SqlTypeFamily.String)]
    [InlineData("Nullable(Int64)", SqlTypeFamily.Integer)]
    [InlineData("Array(String)", SqlTypeFamily.Array)]
    [InlineData("STRUCT<a INT>", SqlTypeFamily.Struct)]
    public void Schema_types_are_classified(string dataType, SqlTypeFamily expected)
    {
        Assert.Equal(expected, SqlTypeFamilies.Classify(dataType));
    }

    [Fact]
    public void Incompatible_comparisons_are_reported()
    {
        var result = SqlValidator.Validate(
            "SELECT id FROM users WHERE age = 'old'",
            Catalog,
            options: Options);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.IncompatibleComparisonTypes);
    }

    [Fact]
    public void Invalid_arithmetic_is_reported()
    {
        var result = SqlValidator.Validate(
            "SELECT age + name FROM users",
            Catalog,
            options: Options);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidArithmeticType);
    }

    [Fact]
    public void Non_boolean_predicates_are_reported()
    {
        var result = SqlValidator.Validate(
            "SELECT id FROM users WHERE age + 1",
            Catalog,
            options: Options);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidPredicateType);
    }

    [Fact]
    public void Function_argument_types_and_arity_are_checked()
    {
        var typeResult = SqlValidator.Validate(
            "SELECT ABS(name) FROM users",
            Catalog,
            options: Options);
        var arityResult = SqlValidator.Validate(
            "SELECT ABS(age, id) FROM users",
            Catalog,
            options: Options);

        Assert.Contains(typeResult.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidFunctionArgumentType);
        Assert.Contains(arityResult.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidFunctionArity);
    }

    [Theory]
    [InlineData("INSERT INTO users (age) VALUES ('old')")]
    [InlineData("INSERT INTO users (age) SELECT name FROM users")]
    [InlineData("UPDATE users SET age = name")]
    public void Dml_assignment_types_are_checked(string sql)
    {
        var result = SqlValidator.Validate(sql, Catalog, options: Options);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidAssignmentType);
    }

    [Fact]
    public void Merge_insert_assignment_types_are_checked()
    {
        var result = SqlValidator.Validate(
            """
            MERGE INTO users u
            USING orders o
            ON u.id = o.user_id
            WHEN NOT MATCHED THEN INSERT (age) VALUES (o.description)
            """,
            Catalog,
            options: Options);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidAssignmentType);
    }

    [Fact]
    public void Intervals_are_not_assignable_to_timestamp_columns()
    {
        var catalog = new SqlSchemaCatalog(
            new SqlTableSchema(
                "events",
                [
                    new("started_at", "timestamp"),
                    new("duration", "interval"),
                ]));

        var result = SqlValidator.Validate(
            "UPDATE events SET started_at = INTERVAL '1' DAY",
            catalog,
            options: Options);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.InvalidAssignmentType);
    }

    [Fact]
    public void Set_operation_arity_and_types_are_checked()
    {
        var arity = SqlValidator.Validate(
            "SELECT id FROM users UNION SELECT id, total FROM orders",
            Catalog,
            options: Options);
        var types = SqlValidator.Validate(
            "SELECT age FROM users UNION SELECT name FROM users",
            Catalog,
            options: Options);

        Assert.Contains(arity.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.SetOperationArityMismatch);
        Assert.Contains(types.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.SetOperationTypeMismatch);
    }

    [Fact]
    public void Non_strict_type_issues_are_warnings()
    {
        var result = SqlValidator.Validate(
            "UPDATE users SET age = name",
            Catalog,
            options: new SqlSchemaValidationOptions
            {
                CheckTypes = true,
                Strict = false,
            });

        Assert.True(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.ImplicitAssignmentCast
            && diagnostic.Severity == SqlValidationSeverity.Warning);
    }
}
