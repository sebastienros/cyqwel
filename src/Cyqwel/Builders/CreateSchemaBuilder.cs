using Cyqwel.Ast;
using Cyqwel.Dialects;
using Cyqwel.Generation;

namespace Cyqwel;

public sealed class CreateSchemaBuilder
{
    private readonly SqlIdentifier _name;

    internal CreateSchemaBuilder(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = new(name);
    }

    public CreateSchemaStatement Build() => new(_name);

    public string ToSql(SqlDialect? dialect = null, SqlGenerationOptions? options = null) =>
        Build().ToSql(dialect, options);
}
