using Cyqwel.Dialects;
using Cyqwel.Validation;

namespace Cyqwel.Tests;

public class SqlValidationSyntaxTests
{
    [Fact]
    public void Valid_sql_has_no_diagnostics()
    {
        var result = SqlValidator.Validate(
            "SELECT id, name FROM users WHERE id > 0 ORDER BY name LIMIT 10");

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parser_errors_are_structured_diagnostics_with_locations()
    {
        var result = SqlValidator.Validate("SELECT 1 +");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsValid);
        Assert.Equal(SqlValidationCodes.SyntaxError, diagnostic.Code);
        Assert.Equal(SqlValidationSeverity.Error, diagnostic.Severity);
        Assert.NotNull(diagnostic.Location);
        Assert.True(diagnostic.Location!.Span.Start >= 0);
        Assert.True(diagnostic.Location.Line >= 1);
        Assert.True(diagnostic.Location.Column >= 1);
    }

    [Fact]
    public void Strict_syntax_rejects_trailing_projection_commas()
    {
        var result = SqlValidator.Validate(
            "SELECT name, FROM employees",
            options: new SqlValidationOptions { StrictSyntax = true });

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsValid);
        Assert.Equal(SqlValidationCodes.StrictSyntax, diagnostic.Code);
        Assert.Equal(11, diagnostic.Location!.Span.Start);
    }

    [Fact]
    public void Permissive_syntax_accepts_trailing_projection_commas()
    {
        var result = SqlValidator.Validate("SELECT name, FROM employees");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Strict_syntax_takes_precedence_over_semantic_checks()
    {
        var result = SqlValidator.Validate(
            "SELECT *, FROM employees",
            options: new SqlValidationOptions
            {
                StrictSyntax = true,
                Semantic = true,
            });

        Assert.Collection(
            result.Diagnostics,
            diagnostic => Assert.Equal(SqlValidationCodes.StrictSyntax, diagnostic.Code));
    }

    [Fact]
    public void Dialect_parser_diagnostics_are_preserved()
    {
        var result = SqlValidator.Validate(
            "SELECT TOP 1 id FROM users",
            SqlDialects.PostgreSql);

        Assert.False(result.IsValid);
        Assert.Equal(
            SqlValidationCodes.SyntaxError,
            Assert.Single(result.Diagnostics).Code);
    }
}
