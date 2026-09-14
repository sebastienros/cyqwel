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
        ArgumentNullException.ThrowIfNull(variable);
        if (_statements.Count == 0) _variables.Add(variable);
        else _statements.Add(new DeclareStatement([variable]));
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

    public TBuilder Return(SqlExpression? value = null) => Statement(new ProceduralReturnStatement(value));

    public TBuilder Print(SqlExpression value) =>
        Statement(new PrintStatement(value ?? throw new ArgumentNullException(nameof(value))));

    public TBuilder ExecuteSql(SqlExpression command) =>
        Statement(new ExecuteSqlStatement(command ?? throw new ArgumentNullException(nameof(command))));

    public TBuilder Declare(params LocalVariable[] variables) =>
        Statement(new DeclareStatement(variables ?? throw new ArgumentNullException(nameof(variables))));

    public TBuilder TableVariable(string name, params TableElement[] elements) =>
        Statement(new TableVariableDeclarationStatement(Sql.TableVariable(name).Name.Parts[0], elements));

    public TBuilder SetVariable(string name, SqlExpression value, SqlAssignmentOperator @operator = SqlAssignmentOperator.Assign) =>
        Statement(new SetVariableStatement(new SqlIdentifier(name), value, @operator));

    public TBuilder Transaction(
        TransactionKind kind,
        SqlExpression? name = null,
        bool isWork = false,
        bool hasMark = false,
        SqlExpression? mark = null,
        bool? delayedDurability = null) =>
        Statement(new TransactionStatement(kind, name, isWork, hasMark, mark)
        {
            DelayedDurability = delayedDurability,
        });

    public TBuilder SetOption(ApplicationSetOption option, bool enabled) =>
        Statement(new SetStatement([new SqlIdentifier(option switch
        {
            ApplicationSetOption.NoCount => "NOCOUNT",
            ApplicationSetOption.XactAbort => "XACT_ABORT",
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        })], []) { ToggleValue = enabled });

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        BuildStatement().ToSql(dialect, options);

    protected abstract SqlStatement BuildStatement();
}

public sealed class ProceduralBlockBuilder : ProceduralBuilder<ProceduralBlockBuilder>
{
    public ProceduralBlock Build() => Block;

    protected override SqlStatement BuildStatement() => Build();
}
