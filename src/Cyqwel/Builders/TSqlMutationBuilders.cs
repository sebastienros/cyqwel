using Cyqwel.Ast;

namespace Cyqwel;

public sealed partial class InsertBuilder
{
    private readonly List<CommonTableExpression> _ctes = [];

    public InsertBuilder With(CommonTableExpression cte)
    {
        _ctes.Add(cte ?? throw new ArgumentNullException(nameof(cte)));
        return this;
    }
    private bool _defaultValues;
    private SqlExpression? _top;
    private bool _topPercent;
    private TSqlOutputClause? _output;
    private IReadOnlyList<TSqlQueryOption>? _queryOptions;

    public InsertBuilder DefaultValues()
    {
        if (_values.Count > 0 || _source is not null)
            throw new InvalidOperationException("DEFAULT VALUES cannot be combined with another source.");
        _defaultValues = true;
        return this;
    }

    public InsertBuilder Top(SqlExpression value, bool percent = false)
    {
        _top = value ?? throw new ArgumentNullException(nameof(value));
        _topPercent = percent;
        return this;
    }

    public InsertBuilder Output(TSqlOutputClause output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        return this;
    }

    public InsertBuilder Option(params TSqlQueryOption[] options)
    {
        _queryOptions = options.ToArray();
        return this;
    }
}

public sealed partial class UpdateBuilder
{
    private readonly List<CommonTableExpression> _ctes = [];

    public UpdateBuilder With(CommonTableExpression cte)
    {
        _ctes.Add(cte ?? throw new ArgumentNullException(nameof(cte)));
        return this;
    }
    private SqlExpression? _top;
    private bool _topPercent;
    private TSqlOutputClause? _output;
    private IReadOnlyList<TSqlQueryOption>? _queryOptions;

    public UpdateBuilder Set(string column, SqlAssignmentOperator @operator, object? value)
    {
        _assignments.Add(new Assignment(Sql.Col(column), Sql.Coerce(value)) { Operator = @operator });
        return this;
    }

    public UpdateBuilder Top(SqlExpression value, bool percent = false)
    {
        _top = value ?? throw new ArgumentNullException(nameof(value));
        _topPercent = percent;
        return this;
    }

    public UpdateBuilder Output(TSqlOutputClause output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        return this;
    }

    public UpdateBuilder Option(params TSqlQueryOption[] options)
    {
        _queryOptions = options.ToArray();
        return this;
    }
}

public sealed partial class DeleteBuilder
{
    private readonly List<CommonTableExpression> _ctes = [];

    public DeleteBuilder With(CommonTableExpression cte)
    {
        _ctes.Add(cte ?? throw new ArgumentNullException(nameof(cte)));
        return this;
    }
    private SqlExpression? _top;
    private bool _topPercent;
    private TSqlOutputClause? _output;
    private TableSource? _from;
    private IReadOnlyList<TSqlQueryOption>? _queryOptions;

    public DeleteBuilder From(TableSource from)
    {
        _from = from ?? throw new ArgumentNullException(nameof(from));
        return this;
    }

    public DeleteBuilder Top(SqlExpression value, bool percent = false)
    {
        _top = value ?? throw new ArgumentNullException(nameof(value));
        _topPercent = percent;
        return this;
    }

    public DeleteBuilder Output(TSqlOutputClause output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        return this;
    }

    public DeleteBuilder Option(params TSqlQueryOption[] options)
    {
        _queryOptions = options.ToArray();
        return this;
    }
}

public sealed partial class MergeBuilder
{
    private readonly List<CommonTableExpression> _ctes = [];

    public MergeBuilder With(CommonTableExpression cte)
    {
        _ctes.Add(cte ?? throw new ArgumentNullException(nameof(cte)));
        return this;
    }
    private SqlExpression? _top;
    private bool _topPercent;
    private TSqlOutputClause? _output;
    private IReadOnlyList<TSqlQueryOption>? _queryOptions;

    public MergeBuilder Top(SqlExpression value, bool percent = false)
    {
        _top = value ?? throw new ArgumentNullException(nameof(value));
        _topPercent = percent;
        return this;
    }

    public MergeBuilder Output(TSqlOutputClause output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        return this;
    }

    public MergeBuilder Option(params TSqlQueryOption[] options)
    {
        _queryOptions = options.ToArray();
        return this;
    }
}
