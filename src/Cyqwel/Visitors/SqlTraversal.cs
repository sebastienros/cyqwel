using Cyqwel.Ast;

namespace Cyqwel.Visitors;

public static class SqlTraversal
{
    public static IEnumerable<SqlNode> DescendantsAndSelf(this SqlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var stack = new Stack<SqlNode>();
        var children = new List<SqlNode>(8);
        stack.Push(node);

        while (stack.TryPop(out var current))
        {
            yield return current;
            children.Clear();
            children.AddRange(SqlNodeChildren.Get(current));
            for (var i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
    }

    public static IEnumerable<SqlNode> Descendants(this SqlNode node) =>
        node.DescendantsAndSelf().Skip(1);

    public static IEnumerable<SqlNode> BreadthFirst(this SqlNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var queue = new Queue<SqlNode>();
        queue.Enqueue(node);

        while (queue.TryDequeue(out var current))
        {
            yield return current;
            foreach (var child in SqlNodeChildren.Get(current)) queue.Enqueue(child);
        }
    }

    public static IEnumerable<TNode> FindAll<TNode>(this SqlNode node) where TNode : SqlNode =>
        node.DescendantsAndSelf().OfType<TNode>();

    public static bool Contains<TNode>(this SqlNode node) where TNode : SqlNode =>
        node.DescendantsAndSelf().Any(static candidate => candidate is TNode);

    public static IReadOnlyList<string> GetTableNames(this SqlNode node) =>
        node.FindAll<TableName>()
            .Select(static table => string.Join('.', table.Parts.Select(static part => part.Value)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<string> GetColumnNames(this SqlNode node) =>
        node.FindAll<ColumnExpression>()
            .Select(static column => string.Join('.', column.Parts.Select(static part => part.Value)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
