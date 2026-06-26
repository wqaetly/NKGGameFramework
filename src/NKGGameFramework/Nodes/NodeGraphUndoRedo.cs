using NKGGameFramework.Core;
using static NKGGameFramework.Nodes.NodeGraphCommandPoolRelease;

namespace NKGGameFramework.Nodes;

public sealed class NodeGraphUndoRedo
{
    private readonly NodeGraph _graph;
    private readonly Stack<INodeGraphCommand> _undo = [];
    private readonly Stack<INodeGraphCommand> _redo = [];
    private int _lastSavedUndoCount;

    internal NodeGraphUndoRedo(NodeGraph graph)
    {
        _graph = graph;
    }

    public bool HasSomethingToSave => _undo.Count != _lastSavedUndoCount;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public void MarkAsSaved()
    {
        _lastSavedUndoCount = _undo.Count;
    }

    public void Record(INodeGraphCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _undo.Push(command);
        ReleaseCommands(_redo);
    }

    public bool Undo()
    {
        if (!_undo.TryPop(out var command))
        {
            return false;
        }

        command.Undo(_graph);
        _redo.Push(command);
        return true;
    }

    public bool Redo()
    {
        if (!_redo.TryPop(out var command))
        {
            return false;
        }

        command.Redo(_graph);
        _undo.Push(command);
        return true;
    }

    public void Clear()
    {
        ReleaseCommands(_undo);
        ReleaseCommands(_redo);
        _lastSavedUndoCount = 0;
    }

    private void ReleaseCommands(Stack<INodeGraphCommand> commands)
    {
        while (commands.TryPop(out var command))
        {
            command.Release(_graph);
        }
    }
}

public interface INodeGraphCommand : IPoolItem
{
    string Tag { get; }

    void Undo(NodeGraph graph);

    void Redo(NodeGraph graph);

    void Release(NodeGraph graph);
}

public sealed class ConnectPortCommand : INodeGraphCommand
{
    private static readonly MemoryPool<ConnectPortCommand> Pool = new();
    private NodePortLine _line = null!;

    public static ConnectPortCommand Acquire(NodePortLine line)
    {
        var command = Pool.Acquire();
        command._line = line;
        return command;
    }

    public string Tag => nameof(ConnectPortCommand);

    public void Undo(NodeGraph graph)
    {
        graph.DetachPortLine(_line, releaseToPool: false);
    }

    public void Redo(NodeGraph graph)
    {
        graph.RestorePortLine(_line);
    }

    public void Release(NodeGraph graph)
    {
        ReleasePortLineIfDetached(graph, _line);
        Pool.Release(this);
    }

    public void OnAcquire()
    {
    }

    public void OnRelease()
    {
        _line = null!;
    }
}

public sealed class DisconnectPortCommand : INodeGraphCommand
{
    private static readonly MemoryPool<DisconnectPortCommand> Pool = new();
    private NodePortLine _line = null!;

    public static DisconnectPortCommand Acquire(NodePortLine line)
    {
        var command = Pool.Acquire();
        command._line = line;
        return command;
    }

    public string Tag => nameof(DisconnectPortCommand);

    public void Undo(NodeGraph graph)
    {
        graph.RestorePortLine(_line);
    }

    public void Redo(NodeGraph graph)
    {
        graph.DetachPortLine(_line, releaseToPool: false);
    }

    public void Release(NodeGraph graph)
    {
        ReleasePortLineIfDetached(graph, _line);
        Pool.Release(this);
    }

    public void OnAcquire()
    {
    }

    public void OnRelease()
    {
        _line = null!;
    }
}

public sealed class CreateNodeCommand : INodeGraphCommand
{
    private static readonly MemoryPool<CreateNodeCommand> Pool = new();
    private Node _node = null!;

    public static CreateNodeCommand Acquire(Node node)
    {
        var command = Pool.Acquire();
        command._node = node;
        return command;
    }

    public string Tag => nameof(CreateNodeCommand);

    public void Undo(NodeGraph graph)
    {
        graph.RemoveNodeCore(_node);
    }

    public void Redo(NodeGraph graph)
    {
        graph.RestoreNode(_node);
    }

    public void Release(NodeGraph graph)
    {
        ReleaseNodeIfDetached(graph, _node);
        Pool.Release(this);
    }

    public void OnAcquire()
    {
    }

    public void OnRelease()
    {
        _node = null!;
    }
}

public sealed class DeleteNodeCommand : INodeGraphCommand
{
    private static readonly MemoryPool<DeleteNodeCommand> Pool = new();
    private readonly List<NodePortLine> _lines = [];
    private Node _node = null!;

    public static DeleteNodeCommand Acquire(Node node)
    {
        var command = Pool.Acquire();
        command._node = node;
        command._lines.AddRange(node.Ports.SelectMany(static port => port.ConnectionLines).Distinct());
        return command;
    }

    public string Tag => nameof(DeleteNodeCommand);

    public void Undo(NodeGraph graph)
    {
        graph.RestoreNode(_node);
        foreach (var line in _lines)
        {
            graph.RestorePortLine(line);
        }
    }

    public void Redo(NodeGraph graph)
    {
        graph.RemoveNodeCore(_node);
    }

    public void Release(NodeGraph graph)
    {
        foreach (var line in _lines)
        {
            ReleasePortLineIfDetached(graph, line);
        }

        ReleaseNodeIfDetached(graph, _node);
        Pool.Release(this);
    }

    public void OnAcquire()
    {
    }

    public void OnRelease()
    {
        _node = null!;
        _lines.Clear();
    }
}

internal static class NodeGraphCommandPoolRelease
{
    public static void ReleaseNodeIfDetached(NodeGraph graph, Node node)
    {
        if (node.Id == 0 ||
            graph.Nodes.TryGetValue(node.Id, out var activeNode) && ReferenceEquals(activeNode, node))
        {
            return;
        }

        NodeGraph.ReleaseNode(node);
    }

    public static void ReleasePortLineIfDetached(NodeGraph graph, NodePortLine line)
    {
        if (line.Id == 0 ||
            graph.PortLines.TryGetValue(line.Id, out var activeLine) && ReferenceEquals(activeLine, line))
        {
            return;
        }

        NodePortLine.Release(line);
    }
}
