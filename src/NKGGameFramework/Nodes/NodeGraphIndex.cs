namespace NKGGameFramework.Nodes;

public sealed class NodeGraphIndex
{
    private readonly Dictionary<string, NodeDefinition> _nodes;
    private readonly Dictionary<NodePortKey, NodePortDefinition> _ports;
    private readonly Dictionary<string, List<NodeLinkDefinition>> _outgoingLinks;
    private readonly Dictionary<string, List<NodeLinkDefinition>> _incomingLinks;

    internal NodeGraphIndex(NodeGraphDefinition graph)
    {
        Graph = graph;
        _nodes = graph.Nodes.ToDictionary(static node => node.Id, StringComparer.Ordinal);
        _ports = graph.Nodes
            .SelectMany(static node => node.Ports.Select(port => new KeyValuePair<NodePortKey, NodePortDefinition>(
                new NodePortKey(node.Id, port.Id),
                port)))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value);
        _outgoingLinks = BuildLinkLookup(graph.Links, static link => link.FromNodeId);
        _incomingLinks = BuildLinkLookup(graph.Links, static link => link.ToNodeId);
    }

    public NodeGraphDefinition Graph { get; }

    public IReadOnlyDictionary<string, NodeDefinition> Nodes => _nodes;

    public NodeDefinition GetNode(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var node))
        {
            throw new KeyNotFoundException($"Node '{nodeId}' does not exist.");
        }

        return node;
    }

    public bool TryGetNode(string nodeId, out NodeDefinition node)
    {
        return _nodes.TryGetValue(nodeId, out node!);
    }

    public NodePortDefinition GetPort(string nodeId, string portId)
    {
        if (!_ports.TryGetValue(new NodePortKey(nodeId, portId), out var port))
        {
            throw new KeyNotFoundException($"Port '{nodeId}.{portId}' does not exist.");
        }

        return port;
    }

    public IReadOnlyList<NodeLinkDefinition> GetOutgoingLinks(string nodeId)
    {
        return _outgoingLinks.TryGetValue(nodeId, out var links) ? links : [];
    }

    public IReadOnlyList<NodeLinkDefinition> GetIncomingLinks(string nodeId)
    {
        return _incomingLinks.TryGetValue(nodeId, out var links) ? links : [];
    }

    public IEnumerable<NodeDefinition> GetDownstreamNodes(string nodeId)
    {
        foreach (var link in GetOutgoingLinks(nodeId))
        {
            yield return GetNode(link.ToNodeId);
        }
    }

    public bool TryGetTopologicalOrder(
        out IReadOnlyList<NodeDefinition> nodes,
        out string? cycleNodeId)
    {
        var indegrees = _nodes.Keys.ToDictionary(static id => id, _ => 0, StringComparer.Ordinal);
        foreach (var link in Graph.Links)
        {
            indegrees[link.ToNodeId]++;
        }

        var queue = new Queue<string>(_nodes.Keys.Where(id => indegrees[id] == 0));
        var ordered = new List<NodeDefinition>(_nodes.Count);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            ordered.Add(_nodes[nodeId]);

            foreach (var link in GetOutgoingLinks(nodeId))
            {
                indegrees[link.ToNodeId]--;
                if (indegrees[link.ToNodeId] == 0)
                {
                    queue.Enqueue(link.ToNodeId);
                }
            }
        }

        if (ordered.Count == _nodes.Count)
        {
            nodes = ordered;
            cycleNodeId = null;
            return true;
        }

        nodes = ordered;
        cycleNodeId = indegrees.First(static pair => pair.Value > 0).Key;
        return false;
    }

    private static Dictionary<string, List<NodeLinkDefinition>> BuildLinkLookup(
        IEnumerable<NodeLinkDefinition> links,
        Func<NodeLinkDefinition, string> keySelector)
    {
        var lookup = new Dictionary<string, List<NodeLinkDefinition>>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            var key = keySelector(link);
            if (!lookup.TryGetValue(key, out var group))
            {
                group = [];
                lookup.Add(key, group);
            }

            group.Add(link);
        }

        return lookup;
    }
}
