# Builders / CreateInlineFunctionBuilder.cs

## CreateInlineFunctionBuilder (sealed class)

`Sql.CreateInlineFunction(name)`. Builds a T-SQL inline table-valued function.
- `Parameter(name, type, defaultValue?)`: adds a parameter (a leading `@` is trimmed).
- `Returns(query)`: the returned query (required; `Build()` throws `InvalidOperationException` otherwise).
- `Build()` returns a `CreateInlineFunctionStatement`; `ToSql(dialect?, options?)`.
