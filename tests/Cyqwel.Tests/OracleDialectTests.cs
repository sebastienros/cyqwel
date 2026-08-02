using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Visitors;

namespace Cyqwel.Tests;

public class OracleDialectTests
{
    [Fact]
    public void Registers_oracle_as_a_builtin_dialect()
    {
        Assert.Same(SqlDialects.Oracle, SqlDialectRegistry.Get("oracle"));
        Assert.Contains(SqlDialects.Oracle, SqlDialects.BuiltIn);
    }

    [Fact]
    public void Parses_oracle_hierarchical_queries_sequences_and_row_limits()
    {
        const string sql = """
            SELECT employee_seq.NEXTVAL, e.employee_id
            FROM employees e
            START WITH e.manager_id IS NULL
            CONNECT BY NOCYCLE PRIOR e.employee_id = e.manager_id
            ORDER SIBLINGS BY e.employee_id
            OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY
            """;
        var document = SqlDialects.Oracle.Parse(sql);
        var select = Assert.IsType<SelectStatement>(Assert.Single(document.Statements));

        Assert.Single(document.FindAll<SequenceValueExpression>());
        Assert.NotNull(select.ConnectBy);
        Assert.True(select.ConnectBy.NoCycle);
        Assert.True(select.OrderSiblings);
        Assert.Equal(
            "SELECT employee_seq.NEXTVAL, e.employee_id FROM employees e START WITH e.manager_id IS NULL CONNECT BY NOCYCLE PRIOR e.employee_id = e.manager_id ORDER SIBLINGS BY e.employee_id OFFSET 5 ROWS FETCH FIRST 10 ROWS ONLY",
            document.ToSql(SqlDialects.Oracle));
    }

    [Fact]
    public void Parses_oracle_returning_into_and_bind_variables()
    {
        var document = SqlDialects.Oracle.Parse(
            "UPDATE users u SET u.name = :name WHERE u.id = :id RETURNING u.id INTO :result");
        var update = Assert.IsType<UpdateStatement>(Assert.Single(document.Statements));

        Assert.Single(update.Returning!);
        Assert.Single(update.ReturningInto!);
        Assert.All(document.FindAll<ParameterExpression>(), value => Assert.Equal(':', value.Prefix));
        Assert.Equal(
            "UPDATE users u SET u.name = :name WHERE u.id = :id RETURNING u.id INTO :result",
            document.ToSql(SqlDialects.Oracle));
    }

    [Fact]
    public void Generates_oracle_set_operators_functions_and_aliases()
    {
        var set = new SetOperationStatement(
            new SelectStatement(
                [new SelectItem(new FunctionCallExpression("COALESCE", new ColumnExpression("name"), new LiteralExpression("n/a")))],
                new NamedTable("current_users", "u")),
            SetOperator.Except,
            new SelectStatement(
                [new SelectItem(new ColumnExpression("name"))],
                new NamedTable("archived_users", "a")));

        Assert.Equal(
            "SELECT NVL(name, 'n/a') FROM current_users u MINUS SELECT name FROM archived_users a",
            set.ToSql(SqlDialects.Oracle));

        Assert.Equal(
            "SELECT id FROM users WHERE id = :id",
            SqlDialects.Generic.Transpile(
                "SELECT id FROM users WHERE id = @id",
                SqlDialects.Oracle));
    }

    [Fact]
    public void Parses_and_generates_oracle_sequences()
    {
        var document = SqlDialects.Oracle.Parse(
            "CREATE SEQUENCE account_seq START WITH 100 INCREMENT BY 10 CACHE 50 NO CYCLE");

        Assert.IsType<CreateSequenceStatement>(Assert.Single(document.Statements));
        Assert.Equal(
            "CREATE SEQUENCE account_seq START WITH 100 INCREMENT BY 10 CACHE 50 NO CYCLE",
            document.ToSql(SqlDialects.Oracle));
    }
}
