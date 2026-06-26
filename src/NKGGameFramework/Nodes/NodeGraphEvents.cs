namespace NKGGameFramework.Nodes;

public sealed class NodeGraphEvents
{
    public event Action<NodeGraphNodeEvent>? NodeAdded;
    public event Action<NodeGraphNodeEvent>? NodeRemoved;
    public event Action<NodeGraphPortLineEvent>? PortConnected;
    public event Action<NodeGraphPortLineEvent>? PortDisconnected;

    internal void Publish(NodeGraphNodeEvent eventArgs)
    {
        switch (eventArgs.Kind)
        {
            case NodeGraphEventKind.NodeAdded:
                NodeAdded?.Invoke(eventArgs);
                break;
            case NodeGraphEventKind.NodeRemoved:
                NodeRemoved?.Invoke(eventArgs);
                break;
        }
    }

    internal void Publish(NodeGraphPortLineEvent eventArgs)
    {
        switch (eventArgs.Kind)
        {
            case NodeGraphEventKind.PortConnected:
                PortConnected?.Invoke(eventArgs);
                break;
            case NodeGraphEventKind.PortDisconnected:
                PortDisconnected?.Invoke(eventArgs);
                break;
        }
    }
}

public readonly record struct NodeGraphNodeEvent(NodeGraphEventKind Kind, Node Node);

public readonly record struct NodeGraphPortLineEvent(NodeGraphEventKind Kind, NodePortLine PortLine);

public enum NodeGraphEventKind
{
    NodeAdded,
    NodeRemoved,
    PortConnected,
    PortDisconnected,
}
