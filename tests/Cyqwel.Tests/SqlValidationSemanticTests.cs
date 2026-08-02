using Cyqwel.Validation;

namespace Cyqwel.Tests;

public class SqlValidationSemanticTests
{
    [Fact]
    public void Semantic_checks_warn_for_select_star()
    {
        var result = SqlValidator.Validate(
            "SELECT * FROM users",
            options: new SqlValidationOptions { Semantic = true });

        Assert.True(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.SelectStar
            && diagnostic.Severity == SqlValidationSeverity.Warning);
    }

    [Fact]
    public void Semantic_checks_are_opt_in()
    {
        var result = SqlValidator.Validate("SELECT * FROM users");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Mixed_aggregate_projections_without_group_by_warn()
    {
        var result = SqlValidator.Validate(
            "SELECT user_id, SUM(total) FROM orders",
            options: new SqlValidationOptions { Semantic = true });

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.AggregateWithoutGroupBy);
    }

    [Fact]
    public void Grouped_aggregate_projections_do_not_warn()
    {
        var result = SqlValidator.Validate(
            "SELECT user_id, SUM(total) FROM orders GROUP BY user_id",
            options: new SqlValidationOptions { Semantic = true });

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.AggregateWithoutGroupBy);
    }

    [Fact]
    public void Distinct_order_by_non_projected_expression_warns()
    {
        var result = SqlValidator.Validate(
            "SELECT DISTINCT name FROM users ORDER BY age",
            options: new SqlValidationOptions { Semantic = true });

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.DistinctOrderBy);
    }

    [Fact]
    public void Distinct_order_by_projection_alias_does_not_warn()
    {
        var result = SqlValidator.Validate(
            "SELECT DISTINCT name AS display_name FROM users ORDER BY display_name",
            options: new SqlValidationOptions { Semantic = true });

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.DistinctOrderBy);
    }

    [Theory]
    [InlineData("SELECT id FROM users LIMIT 10")]
    [InlineData("SELECT id FROM users OFFSET 10")]
    public void Non_deterministic_row_limiting_warns(string sql)
    {
        var result = SqlValidator.Validate(
            sql,
            options: new SqlValidationOptions { Semantic = true });

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.LimitWithoutOrderBy);
    }

    [Fact]
    public void Ordered_row_limiting_does_not_warn()
    {
        var result = SqlValidator.Validate(
            "SELECT id FROM users ORDER BY id LIMIT 10",
            options: new SqlValidationOptions { Semantic = true });

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == SqlValidationCodes.LimitWithoutOrderBy);
    }
}
