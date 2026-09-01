using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;

namespace Cyqwel;

public abstract class ProcedureDefinitionBuilder<TBuilder> : ProceduralBuilder<TBuilder>
    where TBuilder : ProcedureDefinitionBuilder<TBuilder>
{
    private readonly TableName _name;
    private readonly List<ProcedureParameter> _parameters = [];

    protected ProcedureDefinitionBuilder(string name) => _name = new TableName(name);

    protected TableName Name => _name;
    protected IReadOnlyList<ProcedureParameter> Parameters => _parameters.ToArray();
    protected ProceduralBlock Body => Block;

    public TBuilder Parameter(ProcedureParameter parameter)
    {
        _parameters.Add(parameter ?? throw new ArgumentNullException(nameof(parameter)));
        return (TBuilder)this;
    }

    public TBuilder Parameter(
        string name,
        string dataType,
        ProcedureParameterMode mode = ProcedureParameterMode.In,
        SqlExpression? defaultValue = null,
        params int[] dataTypeArguments) =>
        Parameter(new ProcedureParameter(
            new SqlIdentifier(name),
            new SqlDataType(dataType, dataTypeArguments),
            mode,
            defaultValue));

    public TBuilder Parameter(
        string name,
        SqlDataType dataType,
        ProcedureParameterMode mode = ProcedureParameterMode.In,
        SqlExpression? defaultValue = null) =>
        Parameter(new ProcedureParameter(
            new SqlIdentifier(name),
            dataType ?? throw new ArgumentNullException(nameof(dataType)),
            mode,
            defaultValue));

}

public sealed class CreateProcedureBuilder(string name)
    : ProcedureDefinitionBuilder<CreateProcedureBuilder>(name)
{
    public CreateProcedureStatement Build() => new(Name, Parameters, Body);
    protected override SqlStatement BuildStatement() => Build();
}

public sealed class ReplaceProcedureBuilder(string name)
    : ProcedureDefinitionBuilder<ReplaceProcedureBuilder>(name)
{
    public ReplaceProcedureStatement Build() => new(Name, Parameters, Body);
    protected override SqlStatement BuildStatement() => Build();
}

public sealed class DropProcedureBuilder
{
    private readonly TableName _name;
    private readonly List<SqlDataType> _parameterTypes = [];
    private bool _ifExists;

    internal DropProcedureBuilder(string name) => _name = new TableName(name);

    public DropProcedureBuilder ParameterType(string dataType, params int[] arguments)
    {
        _parameterTypes.Add(new SqlDataType(dataType, arguments));
        return this;
    }

    public DropProcedureBuilder IfExists(bool value = true)
    {
        _ifExists = value;
        return this;
    }

    public DropProcedureStatement Build() =>
        new(_name, _parameterTypes.Count == 0 ? null : _parameterTypes.ToArray(), _ifExists);

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}

public sealed class CallProcedureBuilder
{
    private readonly TableName _name;
    private readonly List<ProcedureArgument> _arguments = [];

    internal CallProcedureBuilder(string name) => _name = new TableName(name);

    public CallProcedureBuilder Argument(SqlExpression value, bool output = false)
    {
        _arguments.Add(new ProcedureArgument(
            value ?? throw new ArgumentNullException(nameof(value)),
            IsOutput: output));
        return this;
    }

    public CallProcedureBuilder Argument(object? value, bool output = false) =>
        Argument(Sql.Coerce(value), output);

    public CallProcedureBuilder NamedArgument(string name, SqlExpression value, bool output = false)
    {
        _arguments.Add(new ProcedureArgument(
            value ?? throw new ArgumentNullException(nameof(value)),
            new SqlIdentifier(name),
            output));
        return this;
    }

    public CallProcedureBuilder NamedArgument(string name, object? value, bool output = false) =>
        NamedArgument(name, Sql.Coerce(value), output);

    public CallProcedureStatement Build() => new(_name, _arguments.ToArray());

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}
