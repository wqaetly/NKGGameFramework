using System.Reflection;
using NKGGameFramework.Core;

namespace NKGGameFramework.Nodes;

public sealed class NodeGraph
{
    private long _nextId = 1;

    public long Id { get; private set; }

    public string Name { get; set; } = string.Empty;

    public string SavePath { get; set; } = string.Empty;

    public NodePoint Position { get; set; } = NodePoint.Zero;

    public NodePoint Scale { get; set; } = new(1, 1);

    public Dictionary<long, Node> Nodes { get; } = [];

    public Dictionary<long, NodePortLine> PortLines { get; } = [];

    public NodeGraphEvents Events { get; } = new();

    public NodeGraphUndoRedo UndoRedo { get; }

    public NodeGraph()
    {
        Id = GenerateId();
        UndoRedo = new NodeGraphUndoRedo(this);
    }

    public T AddNode<T>(string? name = null, bool recordUndo = false)
        where T : Node, new()
    {
        return (T)AddNode(NodePool<T>.Acquire(), name, regenerateId: true, recordUndo);
    }

    public Node AddNode(Node node, string? name = null, bool regenerateId = false, bool recordUndo = false)
    {
        ArgumentNullException.ThrowIfNull(node);

        InitializeNode(node, name, regenerateId || node.Id == 0);
        if (Nodes.ContainsKey(node.Id))
        {
            throw new InvalidOperationException($"Node id '{node.Id}' already exists.");
        }

        Nodes.Add(node.Id, node);
        Events.Publish(new NodeGraphNodeEvent(NodeGraphEventKind.NodeAdded, node));

        if (recordUndo)
        {
            UndoRedo.Record(CreateNodeCommand.Acquire(node));
        }

        return node;
    }

    public bool RemoveNode(long nodeId, bool recordUndo = false)
    {
        return Nodes.TryGetValue(nodeId, out var node) && RemoveNode(node, recordUndo);
    }

    public bool RemoveNode(Node node, bool recordUndo = false)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!Nodes.ContainsKey(node.Id))
        {
            return false;
        }

        if (recordUndo)
        {
            UndoRedo.Record(DeleteNodeCommand.Acquire(node));
        }

        RemoveNodeCore(node, releaseToPool: !recordUndo);
        return true;
    }

    public NodePortLine Connect(NodePort first, NodePort second, bool recordUndo = false)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var (input, output) = ResolveConnectionPorts(first, second);
        var canConnect = output.CanConnectTo(input, out var reason);
        if (!canConnect)
        {
            throw new InvalidOperationException(reason);
        }

        if (input.ConnectionType == NodeConnectionType.Override)
        {
            input.ClearConnections(recordUndo);
        }

        if (output.ConnectionType == NodeConnectionType.Override)
        {
            output.ClearConnections(recordUndo);
        }

        if (output.IsConnectedTo(input))
        {
            throw new InvalidOperationException("Ports are already connected.");
        }

        var line = NodePortLine.Acquire();
        InitializePortLine(line, input, output);
        AttachPortLine(line);

        if (recordUndo)
        {
            UndoRedo.Record(ConnectPortCommand.Acquire(line));
        }

        return line;
    }

    public bool Disconnect(long portLineId, bool recordUndo = false)
    {
        return PortLines.TryGetValue(portLineId, out var line) && Disconnect(line, recordUndo);
    }

    public bool Disconnect(NodePortLine line, bool recordUndo = false)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!PortLines.ContainsKey(line.Id))
        {
            return false;
        }

        if (recordUndo)
        {
            UndoRedo.Record(DisconnectPortCommand.Acquire(line));
        }

        DetachPortLine(line, releaseToPool: !recordUndo);
        return true;
    }

    public void RebuildRuntimeLinks()
    {
        foreach (var node in Nodes.Values)
        {
            InitializeNode(node, node.Name, regenerateId: node.Id == 0);
        }

        var failed = new List<long>();
        foreach (var line in PortLines.Values)
        {
            if (!ReconnectPortLine(line))
            {
                failed.Add(line.Id);
            }
        }

        foreach (var id in failed)
        {
            PortLines.Remove(id);
        }
    }

    public void Clear()
    {
        foreach (var line in PortLines.Values.ToArray())
        {
            DetachPortLine(line, releaseToPool: true);
        }

        foreach (var node in Nodes.Values)
        {
            ReleaseNode(node);
        }

        Nodes.Clear();
        Position = NodePoint.Zero;
        Scale = new NodePoint(1, 1);
        UndoRedo.Clear();
    }

    internal long GenerateId()
    {
        return _nextId++;
    }

    internal void RestoreNode(Node node)
    {
        InitializeNode(node, node.Name, regenerateId: false);
        Nodes[node.Id] = node;
        foreach (var port in node.Ports)
        {
            port.AttachToNode(node);
        }

        Events.Publish(new NodeGraphNodeEvent(NodeGraphEventKind.NodeAdded, node));
    }

    internal void RemoveNodeCore(Node node, bool releaseToPool = false)
    {
        node.OnPreDeleted();
        var lines = node.Ports
            .SelectMany(static port => port.ConnectionLines)
            .Distinct()
            .ToArray();
        foreach (var line in lines)
        {
            DetachPortLine(line, releaseToPool);
        }

        Nodes.Remove(node.Id);
        node.Graph = null;
        Events.Publish(new NodeGraphNodeEvent(NodeGraphEventKind.NodeRemoved, node));

        if (releaseToPool)
        {
            ReleaseNode(node);
        }
    }

    internal void RestorePortLine(NodePortLine line)
    {
        if (!ReconnectPortLine(line))
        {
            throw new InvalidOperationException($"Port line '{line.Id}' cannot be restored.");
        }
    }

    internal void AttachPortLine(NodePortLine line)
    {
        PortLines[line.Id] = line;
        line.InputPort.AttachLine(line);
        line.OutputPort.AttachLine(line);
        line.OutputNode.OnCreateConnection(line.OutputPort, line.InputPort);
        line.InputNode.OnCreateConnection(line.OutputPort, line.InputPort);
        Events.Publish(new NodeGraphPortLineEvent(NodeGraphEventKind.PortConnected, line));
    }

    internal void DetachPortLine(NodePortLine line, bool releaseToPool = false)
    {
        line.InputPort.DetachLine(line);
        line.OutputPort.DetachLine(line);
        PortLines.Remove(line.Id);
        line.InputNode.OnRemoveConnection(line.InputPort);
        line.OutputNode.OnRemoveConnection(line.OutputPort);
        Events.Publish(new NodeGraphPortLineEvent(NodeGraphEventKind.PortDisconnected, line));

        if (releaseToPool)
        {
            NodePortLine.Release(line);
        }
    }

    private void InitializeNode(Node node, string? name, bool regenerateId)
    {
        if (regenerateId || node.Id == 0)
        {
            node.Id = GenerateId();
        }

        node.Name = string.IsNullOrWhiteSpace(name)
            ? string.IsNullOrWhiteSpace(node.Name) ? node.GetType().Name : node.Name
            : name;
        node.Graph = this;
        NodeDataCache.UpdatePorts(node);
        foreach (var port in node.Ports)
        {
            if (port.Id == 0)
            {
                port.Id = GenerateId();
            }
        }
    }

    private void InitializePortLine(NodePortLine line, NodePort input, NodePort output)
    {
        if (line.Id == 0)
        {
            line.Id = GenerateId();
        }

        line.InputNode = input.Node;
        line.InputNodeId = input.Node.Id;
        line.InputFieldName = input.FieldName;
        line.InputPort = input;
        line.OutputNode = output.Node;
        line.OutputNodeId = output.Node.Id;
        line.OutputFieldName = output.FieldName;
        line.OutputPort = output;
    }

    private bool ReconnectPortLine(NodePortLine line)
    {
        if (!Nodes.TryGetValue(line.InputNodeId, out var inputNode) ||
            !Nodes.TryGetValue(line.OutputNodeId, out var outputNode) ||
            !inputNode.TryGetPort(line.InputFieldName, out var inputPort) ||
            !outputNode.TryGetPort(line.OutputFieldName, out var outputPort))
        {
            return false;
        }

        line.InputNode = inputNode;
        line.InputPort = inputPort;
        line.OutputNode = outputNode;
        line.OutputPort = outputPort;

        if (!outputPort.CanConnectTo(inputPort, out _))
        {
            return false;
        }

        if (!PortLines.ContainsKey(line.Id))
        {
            PortLines[line.Id] = line;
        }

        if (!inputPort.ConnectionLines.Contains(line))
        {
            inputPort.AttachLine(line);
        }

        if (!outputPort.ConnectionLines.Contains(line))
        {
            outputPort.AttachLine(line);
        }

        return true;
    }

    internal static void ReleaseNode(Node node)
    {
        if (node.PoolOwner is not null)
        {
            node.PoolOwner.Release(node);
            return;
        }

        node.Clear();
    }

    private static (NodePort Input, NodePort Output) ResolveConnectionPorts(NodePort first, NodePort second)
    {
        if (first.Direction == second.Direction)
        {
            throw new InvalidOperationException($"Cannot connect two {first.Direction} ports.");
        }

        return first.Direction == NodePortDirection.Input
            ? (first, second)
            : (second, first);
    }
}

public class Node : IPoolItem
{
    private readonly Dictionary<string, NodePort> _ports = new(StringComparer.Ordinal);

    public long Id { get; internal set; }

    internal INodePool? PoolOwner { get; set; }

    public NodeGraph? Graph { get; internal set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public NodePoint Position { get; set; }

    public NodePoint Size { get; set; }

    public bool IsCustomExpanded { get; set; }

    public bool IsHidden { get; set; }

    public IEnumerable<NodePort> Ports => _ports.Values;

    public IEnumerable<NodePort> Inputs => Ports.Where(static port => port.IsInput);

    public IEnumerable<NodePort> Outputs => Ports.Where(static port => port.IsOutput);

    public IEnumerable<NodePort> DynamicPorts => Ports.Where(static port => port.IsDynamic);

    public IEnumerable<NodePort> DynamicInputs => DynamicPorts.Where(static port => port.IsInput);

    public IEnumerable<NodePort> DynamicOutputs => DynamicPorts.Where(static port => port.IsOutput);

    public IEnumerable<Node> GetInputNodes()
    {
        return Inputs.SelectMany(static port => port.ConnectionLines).Select(static line => line.OutputNode);
    }

    public IEnumerable<Node> GetOutputNodes()
    {
        return Outputs.SelectMany(static port => port.ConnectionLines).Select(static line => line.InputNode);
    }

    public NodePort AddDynamicInput(
        string portName,
        Type valueType,
        NodeConnectionType connectionType = NodeConnectionType.Multiple,
        NodeTypeConstraint typeConstraint = NodeTypeConstraint.None,
        string? fieldName = null,
        NodePortPosition position = NodePortPosition.Left)
    {
        return AddDynamicPort(portName, valueType, NodePortDirection.Input, position, connectionType, typeConstraint, fieldName);
    }

    public NodePort AddDynamicOutput(
        string portName,
        Type valueType,
        NodeConnectionType connectionType = NodeConnectionType.Multiple,
        NodeTypeConstraint typeConstraint = NodeTypeConstraint.None,
        string? fieldName = null,
        NodePortPosition position = NodePortPosition.Right)
    {
        return AddDynamicPort(portName, valueType, NodePortDirection.Output, position, connectionType, typeConstraint, fieldName);
    }

    public bool RemoveDynamicPort(string fieldName)
    {
        return TryGetPort(fieldName, out var port) && RemoveDynamicPort(port);
    }

    public bool RemoveDynamicPort(NodePort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        if (!port.IsDynamic)
        {
            throw new ArgumentException("Static ports cannot be removed.", nameof(port));
        }

        port.ClearConnections(recordUndo: false);
        if (!_ports.Remove(port.FieldName))
        {
            return false;
        }

        NodePort.Release(port);
        return true;
    }

    public void ClearDynamicPorts()
    {
        foreach (var port in DynamicPorts.ToArray())
        {
            RemoveDynamicPort(port);
        }
    }

    public NodePort GetPort(string fieldName)
    {
        if (!TryGetPort(fieldName, out var port))
        {
            throw new KeyNotFoundException($"Port '{fieldName}' does not exist on node '{Name}'.");
        }

        return port;
    }

    public bool TryGetPort(string fieldName, out NodePort port)
    {
        return _ports.TryGetValue(fieldName, out port!);
    }

    public NodePort? GetInputPort(string fieldName)
    {
        return TryGetPort(fieldName, out var port) && port.IsInput ? port : null;
    }

    public NodePort? GetOutputPort(string fieldName)
    {
        return TryGetPort(fieldName, out var port) && port.IsOutput ? port : null;
    }

    public bool HasPort(string fieldName)
    {
        return _ports.ContainsKey(fieldName);
    }

    public IEnumerable<T> TryGetAllInputValues<T>(string fieldName)
    {
        var inputPort = GetInputPort(fieldName)
            ?? throw new InvalidOperationException($"Input port '{fieldName}' does not exist.");
        foreach (var outputPort in inputPort.GetConnectedPorts())
        {
            T value = default!;
            outputPort.GetOutputValue(inputPort, ref value);
            yield return value;
        }
    }

    public void TryGetInputValue<T>(string fieldName, ref T value)
    {
        var inputPort = GetInputPort(fieldName)
            ?? throw new InvalidOperationException($"Input port '{fieldName}' does not exist.");
        inputPort.GetInputValue(ref value);
    }

    public virtual void TryGetOutputValue<T>(NodePort outputPort, NodePort inputPort, ref T value)
    {
        throw new InvalidOperationException($"{GetType().Name} does not override TryGetOutputValue.");
    }

    public virtual void OnCreateConnection(NodePort output, NodePort input)
    {
    }

    public virtual void OnRemoveConnection(NodePort port)
    {
    }

    public virtual void OnCutFrom(NodeGraph copyFromNodeGraph, Node cutFromNode)
    {
    }

    public virtual void OnCopyFrom(NodeGraph copyFromNodeGraph, Node copyFromNode)
    {
    }

    public virtual void OnPreDeleted()
    {
    }

    public virtual void OnPreRecover(NodeGraph targetNodeGraph)
    {
    }

    public virtual void OnPaste(NodeGraph targetNodeGraph)
    {
    }

    public virtual void Clear()
    {
        foreach (var port in Ports.ToArray())
        {
            port.ClearConnections(recordUndo: false);
            NodePort.Release(port);
        }

        _ports.Clear();
        Id = 0;
        Graph = null;
        Name = string.Empty;
        Description = string.Empty;
        Position = NodePoint.Zero;
        Size = NodePoint.Zero;
        IsCustomExpanded = false;
        IsHidden = false;
    }

    public virtual void OnAcquire()
    {
    }

    public virtual void OnRelease()
    {
        Clear();
        PoolOwner = null;
    }

    internal void AddStaticPort(NodePort port)
    {
        if (_ports.TryGetValue(port.FieldName, out var existing) && !existing.IsDynamic)
        {
            NodePort.Release(port);
            return;
        }

        if (existing is not null)
        {
            existing.ClearConnections(recordUndo: false);
            NodePort.Release(existing);
        }

        port.AttachToNode(this);
        _ports[port.FieldName] = port;
    }

    private NodePort AddDynamicPort(
        string portName,
        Type valueType,
        NodePortDirection direction,
        NodePortPosition position,
        NodeConnectionType connectionType,
        NodeTypeConstraint typeConstraint,
        string? fieldName)
    {
        ArgumentNullException.ThrowIfNull(valueType);

        fieldName ??= NextDynamicFieldName(direction);
        if (_ports.TryGetValue(fieldName, out var existing))
        {
            return existing;
        }

        var port = NodePort.Acquire(
            Graph?.GenerateId() ?? 0,
            portName,
            fieldName,
            valueType,
            direction,
            position,
            connectionType,
            typeConstraint,
            showName: true,
            isDynamic: true);
        port.AttachToNode(this);
        _ports.Add(fieldName, port);
        return port;
    }

    private string NextDynamicFieldName(NodePortDirection direction)
    {
        var prefix = direction == NodePortDirection.Input ? "dynamicInput" : "dynamicOutput";
        var index = 0;
        var fieldName = $"{prefix}_{index}";
        while (_ports.ContainsKey(fieldName))
        {
            fieldName = $"{prefix}_{++index}";
        }

        return fieldName;
    }
}

public sealed class NodePort : IPoolItem
{
    private static readonly MemoryPool<NodePort> Pool = new();
    private readonly List<NodePortLine> _connectionLines = [];

    public long Id { get; internal set; }

    public Node Node { get; private set; } = null!;

    public string Name { get; private set; } = string.Empty;

    public string FieldName { get; private set; } = string.Empty;

    public Type ValueType { get; private set; } = typeof(object);

    public NodePortDirection Direction { get; private set; }

    public NodePortPosition Position { get; private set; } = NodePortPosition.Left;

    public NodeConnectionType ConnectionType { get; private set; } = NodeConnectionType.Multiple;

    public NodeTypeConstraint TypeConstraint { get; private set; }

    public bool ShowName { get; private set; } = true;

    public bool IsDynamic { get; private set; }

    public bool IsStatic => !IsDynamic;

    public bool IsInput => Direction == NodePortDirection.Input;

    public bool IsOutput => Direction == NodePortDirection.Output;

    public bool IsConnected => _connectionLines.Count != 0;

    public int ConnectionLineCount => _connectionLines.Count;

    public IReadOnlyList<NodePortLine> ConnectionLines => _connectionLines;

    public NodePort? FirstConnectedPort => _connectionLines.Count == 0 ? null : GetTargetConnectionPort(0);

    internal static NodePort Acquire(
        long id,
        string name,
        string fieldName,
        Type valueType,
        NodePortDirection direction,
        NodePortPosition position,
        NodeConnectionType connectionType,
        NodeTypeConstraint typeConstraint,
        bool showName,
        bool isDynamic)
    {
        var port = Pool.Acquire();
        port.Id = id;
        port.Name = name;
        port.FieldName = fieldName;
        port.ValueType = valueType;
        port.Direction = direction;
        port.Position = position;
        port.ConnectionType = connectionType;
        port.TypeConstraint = typeConstraint;
        port.ShowName = showName;
        port.IsDynamic = isDynamic;
        return port;
    }

    internal static void Release(NodePort port)
    {
        Pool.Release(port);
    }

    public bool CanConnectTo(NodePort other, out string reason)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (ReferenceEquals(this, other))
        {
            reason = "Cannot connect port to itself.";
            return false;
        }

        if (Node == other.Node)
        {
            reason = "Cannot connect ports on the same node.";
            return false;
        }

        if (Direction == other.Direction)
        {
            reason = $"Cannot connect two {Direction} ports.";
            return false;
        }

        if (IsConnectedTo(other))
        {
            reason = "Ports are already connected.";
            return false;
        }

        var input = IsInput ? this : other;
        var output = IsOutput ? this : other;
        if (!MatchesTypeConstraint(input.TypeConstraint, input.ValueType, output.ValueType) ||
            !MatchesTypeConstraint(output.TypeConstraint, input.ValueType, output.ValueType))
        {
            reason = $"Port type constraint failed between '{output.ValueType.Name}' and '{input.ValueType.Name}'.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public IEnumerable<NodePort> GetConnectedPorts()
    {
        for (var i = 0; i < _connectionLines.Count; i++)
        {
            var port = GetTargetConnectionPort(i);
            if (port is not null)
            {
                yield return port;
            }
        }
    }

    public NodePort? GetTargetConnectionPort(int index)
    {
        if (index < 0 || index >= _connectionLines.Count)
        {
            return null;
        }

        var line = _connectionLines[index];
        return IsInput ? line.OutputPort : line.InputPort;
    }

    public int GetConnectionPortIndex(NodePort port)
    {
        for (var i = 0; i < _connectionLines.Count; i++)
        {
            if (GetTargetConnectionPort(i)?.Id == port.Id)
            {
                return i;
            }
        }

        return -1;
    }

    public bool IsConnectedTo(NodePort port)
    {
        return GetConnectionPortIndex(port) >= 0;
    }

    public void GetInputValue<T>(ref T value)
    {
        if (!IsInput)
        {
            throw new InvalidOperationException("Only input ports can read input values.");
        }

        if (_connectionLines.Count > 0)
        {
            _connectionLines[0].OutputPort.GetOutputValue(this, ref value);
        }
    }

    public void GetOutputValue<T>(NodePort inputPort, ref T value)
    {
        if (!IsOutput)
        {
            throw new InvalidOperationException("Only output ports can produce output values.");
        }

        Node.TryGetOutputValue(this, inputPort, ref value);
    }

    public void ClearConnections(bool recordUndo = false)
    {
        if (Node.Graph is null)
        {
            _connectionLines.Clear();
            return;
        }

        foreach (var line in _connectionLines.ToArray())
        {
            Node.Graph.Disconnect(line, recordUndo);
        }
    }

    internal void AttachToNode(Node node)
    {
        Node = node;
    }

    internal void AttachLine(NodePortLine line)
    {
        if (!_connectionLines.Contains(line))
        {
            _connectionLines.Add(line);
        }
    }

    internal void DetachLine(NodePortLine line)
    {
        _connectionLines.Remove(line);
    }

    public void OnAcquire()
    {
    }

    public void OnRelease()
    {
        _connectionLines.Clear();
        Id = 0;
        Node = null!;
        Name = string.Empty;
        FieldName = string.Empty;
        ValueType = typeof(object);
        Direction = NodePortDirection.Input;
        Position = NodePortPosition.Left;
        ConnectionType = NodeConnectionType.Multiple;
        TypeConstraint = NodeTypeConstraint.None;
        ShowName = true;
        IsDynamic = false;
    }

    private static bool MatchesTypeConstraint(NodeTypeConstraint constraint, Type inputType, Type outputType)
    {
        return constraint switch
        {
            NodeTypeConstraint.None => true,
            NodeTypeConstraint.Inherited => inputType.IsAssignableFrom(outputType),
            NodeTypeConstraint.Strict => inputType == outputType,
            NodeTypeConstraint.InheritedInverse => outputType.IsAssignableFrom(inputType),
            NodeTypeConstraint.InheritedAny => inputType.IsAssignableFrom(outputType) || outputType.IsAssignableFrom(inputType),
            _ => false,
        };
    }
}

public sealed class NodePortLine : IPoolItem
{
    private static readonly MemoryPool<NodePortLine> Pool = new();

    public long Id { get; internal set; }

    public string InputFieldName { get; internal set; } = string.Empty;

    public string OutputFieldName { get; internal set; } = string.Empty;

    public long InputNodeId { get; internal set; }

    public long OutputNodeId { get; internal set; }

    public Node InputNode { get; internal set; } = null!;

    public Node OutputNode { get; internal set; } = null!;

    public NodePort InputPort { get; internal set; } = null!;

    public NodePort OutputPort { get; internal set; } = null!;

    public List<NodePoint> ReroutePoints { get; init; } = [];

    public bool IsConnected => InputNode is not null && OutputNode is not null;

    public bool IsDynamic => InputPort.IsDynamic || OutputPort.IsDynamic;

    internal static NodePortLine Acquire()
    {
        return Pool.Acquire();
    }

    internal static void Release(NodePortLine line)
    {
        Pool.Release(line);
    }

    public void OnAcquire()
    {
    }

    public void OnRelease()
    {
        Id = 0;
        InputFieldName = string.Empty;
        OutputFieldName = string.Empty;
        InputNodeId = 0;
        OutputNodeId = 0;
        InputNode = null!;
        OutputNode = null!;
        InputPort = null!;
        OutputPort = null!;
        ReroutePoints.Clear();
    }
}

public readonly record struct NodePoint(float X, float Y)
{
    public static readonly NodePoint Zero = new(0, 0);
}

public enum NodePortPosition
{
    Top,
    Down,
    Left,
    Right,
}

public enum NodeConnectionType
{
    Multiple,
    Override,
}

public enum NodeTypeConstraint
{
    None,
    Inherited,
    Strict,
    InheritedInverse,
    InheritedAny,
}

public enum NodeBackingValue
{
    Never,
    Unconnected,
    Always,
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class NodeInputAttribute : Attribute
{
    public NodeInputAttribute(
        string name = "",
        NodePortPosition position = NodePortPosition.Left,
        NodeBackingValue backingValue = NodeBackingValue.Unconnected,
        NodeConnectionType connectionType = NodeConnectionType.Multiple,
        NodeTypeConstraint typeConstraint = NodeTypeConstraint.None,
        bool dynamicPortList = false,
        bool showPortName = true)
    {
        Name = name;
        Position = position;
        BackingValue = backingValue;
        ConnectionType = connectionType;
        TypeConstraint = typeConstraint;
        DynamicPortList = dynamicPortList;
        ShowPortName = showPortName;
    }

    public string Name { get; }

    public NodePortPosition Position { get; }

    public NodeBackingValue BackingValue { get; }

    public NodeConnectionType ConnectionType { get; }

    public NodeTypeConstraint TypeConstraint { get; }

    public bool DynamicPortList { get; }

    public bool ShowPortName { get; }
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class NodeOutputAttribute : Attribute
{
    public NodeOutputAttribute(
        string name = "",
        NodePortPosition position = NodePortPosition.Right,
        NodeBackingValue backingValue = NodeBackingValue.Never,
        NodeConnectionType connectionType = NodeConnectionType.Multiple,
        NodeTypeConstraint typeConstraint = NodeTypeConstraint.None,
        bool dynamicPortList = false,
        bool showPortName = true)
    {
        Name = name;
        Position = position;
        BackingValue = backingValue;
        ConnectionType = connectionType;
        TypeConstraint = typeConstraint;
        DynamicPortList = dynamicPortList;
        ShowPortName = showPortName;
    }

    public string Name { get; }

    public NodePortPosition Position { get; }

    public NodeBackingValue BackingValue { get; }

    public NodeConnectionType ConnectionType { get; }

    public NodeTypeConstraint TypeConstraint { get; }

    public bool DynamicPortList { get; }

    public bool ShowPortName { get; }
}

public sealed class NodeCreateMenuAttribute : Attribute
{
    public NodeCreateMenuAttribute(string menuName, int order = 0)
    {
        MenuName = menuName;
        Order = order;
    }

    public string MenuName { get; }

    public int Order { get; }
}

public sealed class NodeHideInMenuAttribute : Attribute;

public sealed class NodeDisallowMultipleAttribute : Attribute
{
    public NodeDisallowMultipleAttribute(int max = 1)
    {
        Max = max;
    }

    public int Max { get; }
}

internal static class NodeDataCache
{
    private static readonly Dictionary<Type, IReadOnlyList<NodePortTemplate>> PortTemplates = [];

    public static void UpdatePorts(Node node)
    {
        var nodeType = node.GetType();
        if (!PortTemplates.TryGetValue(nodeType, out var templates))
        {
            templates = BuildTemplates(nodeType);
            PortTemplates[nodeType] = templates;
        }

        foreach (var template in templates)
        {
            node.AddStaticPort(NodePort.Acquire(
                0,
                template.Name,
                template.FieldName,
                template.ValueType,
                template.Direction,
                template.Position,
                template.ConnectionType,
                template.TypeConstraint,
                template.ShowName,
                isDynamic: false));
        }
    }

    private static IReadOnlyList<NodePortTemplate> BuildTemplates(Type nodeType)
    {
        var ports = new List<NodePortTemplate>();
        foreach (var field in EnumerateFields(nodeType))
        {
            if (field.GetCustomAttribute<NodeInputAttribute>() is { } input)
            {
                ports.Add(CreateTemplate(field, NodePortDirection.Input, input.Name, input.Position, input.ConnectionType, input.TypeConstraint, input.ShowPortName));
            }

            if (field.GetCustomAttribute<NodeOutputAttribute>() is { } output)
            {
                ports.Add(CreateTemplate(field, NodePortDirection.Output, output.Name, output.Position, output.ConnectionType, output.TypeConstraint, output.ShowPortName));
            }
        }

        return ports;
    }

    private static NodePortTemplate CreateTemplate(
        FieldInfo field,
        NodePortDirection direction,
        string name,
        NodePortPosition position,
        NodeConnectionType connectionType,
        NodeTypeConstraint typeConstraint,
        bool showPortName)
    {
        return new NodePortTemplate(
            string.IsNullOrWhiteSpace(name) ? field.Name : name,
            field.Name,
            field.FieldType,
            direction,
            position,
            connectionType,
            typeConstraint,
            showPortName);
    }

    private static IEnumerable<FieldInfo> EnumerateFields(Type type)
    {
        var stack = new Stack<Type>();
        for (var current = type; current is not null && current != typeof(Node); current = current.BaseType)
        {
            stack.Push(current);
        }

        while (stack.Count > 0)
        {
            foreach (var field in stack.Pop().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                yield return field;
            }
        }
    }
}

internal interface INodePool
{
    void Release(Node node);
}

internal static class NodePool<TNode>
    where TNode : Node, new()
{
    private static readonly TypedNodePool Pool = new();

    public static TNode Acquire()
    {
        var node = Pool.Acquire();
        node.PoolOwner = Pool;
        return node;
    }

    private sealed class TypedNodePool : INodePool
    {
        private readonly MemoryPool<TNode> _pool = new(static () => new TNode());

        public TNode Acquire()
        {
            return _pool.Acquire();
        }

        public void Release(Node node)
        {
            _pool.Release((TNode)node);
        }
    }
}

internal readonly record struct NodePortTemplate(
    string Name,
    string FieldName,
    Type ValueType,
    NodePortDirection Direction,
    NodePortPosition Position,
    NodeConnectionType ConnectionType,
    NodeTypeConstraint TypeConstraint,
    bool ShowName);
