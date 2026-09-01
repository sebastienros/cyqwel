using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;

namespace Cyqwel;

public abstract class ProceduralBuilder<TBuilder> where TBuilder : ProceduralBuilder<TBuilder>
{
    private readonly List<LocalVariable> _variables = [];
    private readonly List<SqlStatement> _statements = [];

    protected ProceduralBlock Block => new(_variables.ToArray(), _statements.ToArray());

    public TBuilder Variable(LocalVariable variable)
    {
        _variables.Add(variable ?? throw new ArgumentNullException(nameof(variable)));
        return (TBuilder)this;
    }

    public TBuilder Variable(
        string name,
        string dataType,
        SqlExpression? initializer = null,
        params int[] dataTypeArguments) =>
        Variable(new LocalVariable(
            new SqlIdentifier(name),
            new SqlDataType(dataType, dataTypeArguments),
            initializer));

    public TBuilder Variable(string name, SqlDataType dataType, SqlExpression? initializer = null) =>
        Variable(new LocalVariable(
            new SqlIdentifier(name),
            dataType ?? throw new ArgumentNullException(nameof(dataType)),
            initializer));

    public TBuilder Statement(SqlStatement statement)
    {
        _statements.Add(statement ?? throw new ArgumentNullException(nameof(statement)));
        return (TBuilder)this;
    }

    public TBuilder If(
        SqlExpression condition,
        IReadOnlyList<SqlStatement> thenStatements,
        IReadOnlyList<SqlStatement>? elseStatements = null) =>
        Statement(new ProceduralIfStatement(condition, thenStatements, elseStatements));

    public TBuilder While(SqlExpression condition, IReadOnlyList<SqlStatement> statements) =>
        Statement(new ProceduralWhileStatement(condition, statements));

    public TBuilder Break() => Statement(new ProceduralBreakStatement());

    public TBuilder Continue() => Statement(new ProceduralContinueStatement());

    public TBuilder Return() => Statement(new ProceduralReturnStatement());

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        BuildStatement().ToSql(dialect, options);

    protected abstract SqlStatement BuildStatement();
}

public sealed class ProceduralBlockBuilder : ProceduralBuilder<ProceduralBlockBuilder>
{
    public ProceduralBlock Build() => Block;

    protected override SqlStatement BuildStatement() => Build();
}
