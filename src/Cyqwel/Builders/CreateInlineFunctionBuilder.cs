using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;

namespace Cyqwel;

public sealed class CreateInlineFunctionBuilder
{
    private readonly TableName _name;
    private readonly List<ProcedureParameter> _parameters = [];
    private SqlQuery? _query;

    internal CreateInlineFunctionBuilder(string name) => _name = new(name);

    public CreateInlineFunctionBuilder Parameter(string name, SqlDataType type, SqlExpression? defaultValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);
        _parameters.Add(new ProcedureParameter(new(name.TrimStart('@')), type, Default: defaultValue));
        return this;
    }

    public CreateInlineFunctionBuilder Returns(SqlQuery query)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        return this;
    }

    public CreateInlineFunctionStatement Build() =>
        new(_name, _parameters.ToArray(), _query ?? throw new InvalidOperationException("An inline function requires a return query."));

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}
