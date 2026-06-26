using System.Text;

namespace NKGGameFramework.Nodes;

public sealed class NodeGraphValidationResult
{
    public NodeGraphValidationResult(IReadOnlyList<NodeGraphValidationIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<NodeGraphValidationIssue> Issues { get; }

    public bool IsValid => Issues.Count == 0;

    public override string ToString()
    {
        if (IsValid)
        {
            return "Node graph is valid.";
        }

        var builder = new StringBuilder("Node graph is invalid:");
        foreach (var issue in Issues)
        {
            builder.AppendLine();
            builder.Append("- ").Append(issue);
        }

        return builder.ToString();
    }
}

public readonly record struct NodeGraphValidationIssue(
    NodeGraphValidationSeverity Severity,
    string Code,
    string Message,
    string? NodeId = null,
    string? PortId = null,
    string? LinkId = null)
{
    public override string ToString()
    {
        var target = LinkId is { Length: > 0 }
            ? $" link='{LinkId}'"
            : NodeId is { Length: > 0 }
                ? PortId is { Length: > 0 }
                    ? $" node='{NodeId}' port='{PortId}'"
                    : $" node='{NodeId}'"
                : string.Empty;

        return $"{Severity} {Code}{target}: {Message}";
    }
}

public enum NodeGraphValidationSeverity
{
    Error,
    Warning,
}

internal static class NodeGraphValidator
{
    public static NodeGraphValidationResult Validate(NodeGraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var issues = new List<NodeGraphValidationIssue>();
        var nodes = new Dictionary<string, NodeDefinition>(StringComparer.Ordinal);
        var ports = new Dictionary<NodePortKey, NodePortDefinition>();

        ValidateNodes(graph, nodes, ports, issues);
        ValidateEntry(graph, nodes, issues);
        ValidateLinks(graph, nodes, ports, issues);

        return new NodeGraphValidationResult(issues);
    }

    private static void ValidateNodes(
        NodeGraphDefinition graph,
        Dictionary<string, NodeDefinition> nodes,
        Dictionary<NodePortKey, NodePortDefinition> ports,
        List<NodeGraphValidationIssue> issues)
    {
        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                issues.Add(Error("node_id_empty", "Node id cannot be empty."));
                continue;
            }

            if (!nodes.TryAdd(node.Id, node))
            {
                issues.Add(Error("node_id_duplicate", $"Node id '{node.Id}' is duplicated.", node.Id));
                continue;
            }

            var nodePortIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var port in node.Ports)
            {
                if (string.IsNullOrWhiteSpace(port.Id))
                {
                    issues.Add(Error("port_id_empty", "Port id cannot be empty.", node.Id));
                    continue;
                }

                if (!nodePortIds.Add(port.Id))
                {
                    issues.Add(Error("port_id_duplicate", $"Port id '{port.Id}' is duplicated on node '{node.Id}'.", node.Id, port.Id));
                    continue;
                }

                if (port.MaxConnections < NodePortDefinition.UnlimitedConnections)
                {
                    issues.Add(Error(
                        "port_max_connections_invalid",
                        "Port MaxConnections must be -1 for unlimited or a non-negative value.",
                        node.Id,
                        port.Id));
                }

                ports[new NodePortKey(node.Id, port.Id)] = port;
            }
        }
    }

    private static void ValidateEntry(
        NodeGraphDefinition graph,
        Dictionary<string, NodeDefinition> nodes,
        List<NodeGraphValidationIssue> issues)
    {
        if (graph.EntryNodeId is not { Length: > 0 })
        {
            return;
        }

        if (!nodes.ContainsKey(graph.EntryNodeId))
        {
            issues.Add(Error("entry_node_missing", $"Entry node '{graph.EntryNodeId}' does not exist.", graph.EntryNodeId));
        }
    }

    private static void ValidateLinks(
        NodeGraphDefinition graph,
        Dictionary<string, NodeDefinition> nodes,
        Dictionary<NodePortKey, NodePortDefinition> ports,
        List<NodeGraphValidationIssue> issues)
    {
        var connectionCounts = new Dictionary<NodePortKey, int>();

        foreach (var link in graph.Links)
        {
            var hasFromNode = nodes.ContainsKey(link.FromNodeId);
            var hasToNode = nodes.ContainsKey(link.ToNodeId);
            if (!hasFromNode)
            {
                issues.Add(Error("link_from_node_missing", $"Link source node '{link.FromNodeId}' does not exist.", linkId: LinkId(link)));
            }

            if (!hasToNode)
            {
                issues.Add(Error("link_to_node_missing", $"Link target node '{link.ToNodeId}' does not exist.", linkId: LinkId(link)));
            }

            if (!hasFromNode || !hasToNode)
            {
                continue;
            }

            var fromKey = new NodePortKey(link.FromNodeId, link.FromPortId);
            var toKey = new NodePortKey(link.ToNodeId, link.ToPortId);
            if (!ports.TryGetValue(fromKey, out var fromPort))
            {
                issues.Add(Error("link_from_port_missing", $"Link source port '{link.FromPortId}' does not exist.", link.FromNodeId, link.FromPortId, LinkId(link)));
                continue;
            }

            if (!ports.TryGetValue(toKey, out var toPort))
            {
                issues.Add(Error("link_to_port_missing", $"Link target port '{link.ToPortId}' does not exist.", link.ToNodeId, link.ToPortId, LinkId(link)));
                continue;
            }

            if (fromPort.Direction != NodePortDirection.Output)
            {
                issues.Add(Error("link_from_port_not_output", "Link source port must be an output port.", link.FromNodeId, link.FromPortId, LinkId(link)));
            }

            if (toPort.Direction != NodePortDirection.Input)
            {
                issues.Add(Error("link_to_port_not_input", "Link target port must be an input port.", link.ToNodeId, link.ToPortId, LinkId(link)));
            }

            Increment(connectionCounts, fromKey);
            Increment(connectionCounts, toKey);
        }

        foreach (var (key, count) in connectionCounts)
        {
            var port = ports[key];
            if (port.MaxConnections != NodePortDefinition.UnlimitedConnections && count > port.MaxConnections)
            {
                issues.Add(Error(
                    "port_connection_limit_exceeded",
                    $"Port allows {port.MaxConnections} connection(s), but has {count}.",
                    key.NodeId,
                    key.PortId));
            }
        }
    }

    private static void Increment(Dictionary<NodePortKey, int> counts, NodePortKey key)
    {
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    private static string LinkId(NodeLinkDefinition link)
    {
        return string.IsNullOrWhiteSpace(link.Id)
            ? $"{link.FromNodeId}.{link.FromPortId}->{link.ToNodeId}.{link.ToPortId}"
            : link.Id;
    }

    private static NodeGraphValidationIssue Error(
        string code,
        string message,
        string? nodeId = null,
        string? portId = null,
        string? linkId = null)
    {
        return new NodeGraphValidationIssue(NodeGraphValidationSeverity.Error, code, message, nodeId, portId, linkId);
    }
}

internal readonly record struct NodePortKey(string NodeId, string PortId);
