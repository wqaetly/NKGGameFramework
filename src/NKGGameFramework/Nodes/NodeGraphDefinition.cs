namespace NKGGameFramework.Nodes;

public sealed class NodeGraphDefinition
{
    public string? EntryNodeId { get; init; }

    public List<NodeDefinition> Nodes { get; init; } = [];

    public List<NodeLinkDefinition> Links { get; init; } = [];

    public NodeGraphValidationResult Validate()
    {
        return NodeGraphValidator.Validate(this);
    }

    public bool TryValidate(out NodeGraphValidationResult result)
    {
        result = Validate();
        return result.IsValid;
    }

    public NodeGraphIndex CreateIndex()
    {
        var validation = Validate();
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.ToString());
        }

        return new NodeGraphIndex(this);
    }
}

public sealed class NodeDefinition
{
    public required string Id { get; init; }

    public string Type { get; init; } = NodeTypes.Default;

    public string? Name { get; init; }

    public NodePosition Position { get; init; }

    public Dictionary<string, string> Parameters { get; init; } = [];

    public List<NodePortDefinition> Ports { get; init; } = [];

    public IEnumerable<NodePortDefinition> Inputs => Ports.Where(static port => port.Direction == NodePortDirection.Input);

    public IEnumerable<NodePortDefinition> Outputs => Ports.Where(static port => port.Direction == NodePortDirection.Output);
}

public readonly record struct NodePosition(float X, float Y);

public sealed class NodePortDefinition
{
    public const int UnlimitedConnections = -1;

    public required string Id { get; init; }

    public string? Name { get; init; }

    public NodePortDirection Direction { get; init; }

    public string? ValueType { get; init; }

    public int MaxConnections { get; init; } = UnlimitedConnections;

    public static NodePortDefinition Input(
        string id,
        string? name = null,
        string? valueType = null,
        int maxConnections = 1)
    {
        return new NodePortDefinition
        {
            Id = id,
            Name = name,
            Direction = NodePortDirection.Input,
            ValueType = valueType,
            MaxConnections = maxConnections,
        };
    }

    public static NodePortDefinition Output(
        string id,
        string? name = null,
        string? valueType = null,
        int maxConnections = UnlimitedConnections)
    {
        return new NodePortDefinition
        {
            Id = id,
            Name = name,
            Direction = NodePortDirection.Output,
            ValueType = valueType,
            MaxConnections = maxConnections,
        };
    }
}

public enum NodePortDirection
{
    Input,
    Output,
}

public sealed class NodeLinkDefinition
{
    public string Id { get; init; } = string.Empty;

    public required string FromNodeId { get; init; }

    public required string FromPortId { get; init; }

    public required string ToNodeId { get; init; }

    public required string ToPortId { get; init; }
}

public static class NodeTypes
{
    public const string Default = "node";
    public const string Entry = "entry";
    public const string Action = "action";
    public const string Composite = "composite";
    public const string Condition = "condition";
}
