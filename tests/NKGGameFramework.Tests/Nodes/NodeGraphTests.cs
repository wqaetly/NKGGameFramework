using NKGGameFramework.Nodes;

namespace NKGGameFramework.Tests.Nodes;

public sealed class NodeGraphTests
{
    [Fact]
    public void Runtime_graph_builds_static_ports_from_attributes_and_reads_values()
    {
        var graph = new NodeGraph();
        var source = graph.AddNode<IntSourceNode>();
        var sink = graph.AddNode<IntSinkNode>();

        var line = graph.Connect(source.GetOutputPort("value")!, sink.GetInputPort("input")!);
        var value = 0;
        sink.TryGetInputValue<int>("input", ref value);

        Assert.Equal(42, value);
        Assert.Equal(source.Id, line.OutputNodeId);
        Assert.Equal(sink.Id, line.InputNodeId);
        Assert.Same(source, Assert.Single(sink.GetInputNodes()));
        Assert.Same(sink, Assert.Single(source.GetOutputNodes()));
    }

    [Fact]
    public void Runtime_graph_supports_dynamic_ports()
    {
        var graph = new NodeGraph();
        var source = graph.AddNode(new Node());
        var sink = graph.AddNode(new Node());

        var output = source.AddDynamicOutput("payload", typeof(string), fieldName: "payload");
        var input = sink.AddDynamicInput("payload", typeof(string), fieldName: "payload");
        graph.Connect(output, input);

        Assert.True(output.IsDynamic);
        Assert.True(input.IsDynamic);
        Assert.True(input.IsConnected);

        sink.RemoveDynamicPort(input);

        Assert.False(input.IsConnected);
        Assert.Empty(graph.PortLines);
        Assert.False(sink.HasPort("payload"));
    }

    [Fact]
    public void Runtime_graph_enforces_type_constraints_and_same_node_rules()
    {
        var graph = new NodeGraph();
        var source = graph.AddNode<IntSourceNode>();
        var stringSink = graph.AddNode<StringSinkNode>();

        Assert.Throws<InvalidOperationException>(() =>
            graph.Connect(source.GetOutputPort("value")!, stringSink.GetInputPort("input")!));

        var sameNode = graph.AddNode<LoopbackNode>();

        Assert.Throws<InvalidOperationException>(() =>
            graph.Connect(sameNode.GetOutputPort("output")!, sameNode.GetInputPort("input")!));
    }

    [Fact]
    public void Runtime_graph_override_input_replaces_existing_connection()
    {
        var graph = new NodeGraph();
        var first = graph.AddNode<IntSourceNode>();
        var second = graph.AddNode<IntSourceNode>();
        var sink = graph.AddNode<OverrideSinkNode>();

        var oldLine = graph.Connect(first.GetOutputPort("value")!, sink.GetInputPort("input")!);
        var oldLineId = oldLine.Id;
        var newLine = graph.Connect(second.GetOutputPort("value")!, sink.GetInputPort("input")!);

        Assert.DoesNotContain(oldLineId, graph.PortLines.Keys);
        Assert.Contains(newLine.Id, graph.PortLines.Keys);
        Assert.Same(second, Assert.Single(sink.GetInputNodes()));
    }

    [Fact]
    public void Runtime_graph_reuses_nodes_from_pool_after_non_undo_delete()
    {
        var graph = new NodeGraph();
        var first = graph.AddNode<IntSourceNode>();
        var firstId = first.Id;

        Assert.True(graph.RemoveNode(first));
        var second = graph.AddNode<IntSourceNode>();

        Assert.Same(first, second);
        Assert.NotEqual(firstId, second.Id);
        Assert.Contains(second.Id, graph.Nodes.Keys);
    }

    [Fact]
    public void Runtime_graph_reuses_port_lines_from_pool_after_non_undo_disconnect()
    {
        var graph = new NodeGraph();
        var source = graph.AddNode<IntSourceNode>();
        var sink = graph.AddNode<IntSinkNode>();

        var firstLine = graph.Connect(source.GetOutputPort("value")!, sink.GetInputPort("input")!);
        var firstLineId = firstLine.Id;

        Assert.True(graph.Disconnect(firstLine));
        Assert.Equal(0, firstLine.Id);

        var secondLine = graph.Connect(source.GetOutputPort("value")!, sink.GetInputPort("input")!);

        Assert.Same(firstLine, secondLine);
        Assert.NotEqual(firstLineId, secondLine.Id);
        Assert.Contains(secondLine.Id, graph.PortLines.Keys);
    }

    [Fact]
    public void Runtime_graph_releases_detached_node_when_redo_history_is_discarded()
    {
        var graph = new NodeGraph();
        var created = graph.AddNode<IntSourceNode>(recordUndo: true);

        Assert.True(graph.UndoRedo.Undo());
        graph.AddNode<IntSinkNode>(recordUndo: true);

        var reused = graph.AddNode<IntSourceNode>();

        Assert.Same(created, reused);
        Assert.Contains(reused.Id, graph.Nodes.Keys);
    }

    [Fact]
    public void Runtime_graph_releases_detached_port_line_when_redo_history_is_discarded()
    {
        var graph = new NodeGraph();
        var source = graph.AddNode<IntSourceNode>();
        var sink = graph.AddNode<IntSinkNode>();
        var created = graph.Connect(source.GetOutputPort("value")!, sink.GetInputPort("input")!, recordUndo: true);

        Assert.True(graph.UndoRedo.Undo());
        graph.AddNode<IntSinkNode>(recordUndo: true);

        var reused = graph.Connect(source.GetOutputPort("value")!, sink.GetInputPort("input")!);

        Assert.Same(created, reused);
        Assert.Contains(reused.Id, graph.PortLines.Keys);
    }

    [Fact]
    public void Runtime_graph_publishes_connection_events()
    {
        var graph = new NodeGraph();
        var source = graph.AddNode<IntSourceNode>();
        var sink = graph.AddNode<IntSinkNode>();
        var connected = 0;
        var disconnected = 0;
        graph.Events.PortConnected += _ => connected++;
        graph.Events.PortDisconnected += _ => disconnected++;

        var line = graph.Connect(source.GetOutputPort("value")!, sink.GetInputPort("input")!);
        graph.Disconnect(line);

        Assert.Equal(1, connected);
        Assert.Equal(1, disconnected);
    }

    [Fact]
    public void Runtime_graph_can_undo_and_redo_connect_operations()
    {
        var graph = new NodeGraph();
        var source = graph.AddNode<IntSourceNode>();
        var sink = graph.AddNode<IntSinkNode>();

        graph.Connect(source.GetOutputPort("value")!, sink.GetInputPort("input")!, recordUndo: true);

        Assert.Single(graph.PortLines);
        Assert.True(graph.UndoRedo.Undo());
        Assert.Empty(graph.PortLines);
        Assert.True(graph.UndoRedo.Redo());
        Assert.Single(graph.PortLines);
    }

    [Fact]
    public void Runtime_graph_can_undo_node_delete_with_connections()
    {
        var graph = new NodeGraph();
        var source = graph.AddNode<IntSourceNode>();
        var sink = graph.AddNode<IntSinkNode>();
        graph.Connect(source.GetOutputPort("value")!, sink.GetInputPort("input")!);

        graph.RemoveNode(sink, recordUndo: true);

        Assert.DoesNotContain(sink.Id, graph.Nodes.Keys);
        Assert.Empty(graph.PortLines);

        Assert.True(graph.UndoRedo.Undo());

        Assert.Contains(sink.Id, graph.Nodes.Keys);
        Assert.Single(graph.PortLines);
        Assert.Same(sink, Assert.Single(source.GetOutputNodes()));
    }

    [Fact]
    public void Valid_graph_creates_index_and_resolves_links()
    {
        var graph = CreateLinearGraph();

        var index = graph.CreateIndex();

        Assert.True(index.TryGetNode("start", out var start));
        Assert.Equal(NodeTypes.Entry, start.Type);
        Assert.Equal("value", index.GetPort("start", "next").ValueType);
        Assert.Equal(["process"], index.GetDownstreamNodes("start").Select(static node => node.Id));
        Assert.Equal("link-1", Assert.Single(index.GetOutgoingLinks("start")).Id);
        Assert.Equal("link-1", Assert.Single(index.GetIncomingLinks("process")).Id);
    }

    [Fact]
    public void Validation_reports_missing_and_wrong_direction_ports()
    {
        var graph = new NodeGraphDefinition
        {
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "a",
                    Ports = [NodePortDefinition.Input("in")],
                },
                new NodeDefinition
                {
                    Id = "b",
                    Ports = [NodePortDefinition.Output("out")],
                },
            ],
            Links =
            [
                new NodeLinkDefinition
                {
                    FromNodeId = "a",
                    FromPortId = "in",
                    ToNodeId = "b",
                    ToPortId = "out",
                },
                new NodeLinkDefinition
                {
                    FromNodeId = "missing",
                    FromPortId = "out",
                    ToNodeId = "b",
                    ToPortId = "missing",
                },
            ],
        };

        var result = graph.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, static issue => issue.Code == "link_from_port_not_output");
        Assert.Contains(result.Issues, static issue => issue.Code == "link_to_port_not_input");
        Assert.Contains(result.Issues, static issue => issue.Code == "link_from_node_missing");
    }

    [Fact]
    public void Validation_enforces_port_connection_limit()
    {
        var graph = new NodeGraphDefinition
        {
            Nodes =
            [
                Source("a"),
                Source("b"),
                new NodeDefinition
                {
                    Id = "target",
                    Ports = [NodePortDefinition.Input("in")],
                },
            ],
            Links =
            [
                Link("a", "out", "target", "in"),
                Link("b", "out", "target", "in"),
            ],
        };

        var result = graph.Validate();

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues, static issue => issue.Code == "port_connection_limit_exceeded");
        Assert.Equal("target", issue.NodeId);
        Assert.Equal("in", issue.PortId);
    }

    [Fact]
    public void Topological_order_returns_dependency_order()
    {
        var index = CreateLinearGraph().CreateIndex();

        var acyclic = index.TryGetTopologicalOrder(out var nodes, out var cycleNodeId);

        Assert.True(acyclic);
        Assert.Null(cycleNodeId);
        Assert.Equal(["start", "process", "finish"], nodes.Select(static node => node.Id));
    }

    [Fact]
    public void Topological_order_detects_cycle()
    {
        var graph = new NodeGraphDefinition
        {
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "a",
                    Ports =
                    [
                        NodePortDefinition.Input("in"),
                        NodePortDefinition.Output("out"),
                    ],
                },
                new NodeDefinition
                {
                    Id = "b",
                    Ports =
                    [
                        NodePortDefinition.Input("in"),
                        NodePortDefinition.Output("out"),
                    ],
                },
            ],
            Links =
            [
                Link("a", "out", "b", "in"),
                Link("b", "out", "a", "in"),
            ],
        };
        var index = graph.CreateIndex();

        var acyclic = index.TryGetTopologicalOrder(out var nodes, out var cycleNodeId);

        Assert.False(acyclic);
        Assert.Empty(nodes);
        Assert.Contains(cycleNodeId, new[] { "a", "b" });
    }

    [Fact]
    public void Create_index_throws_for_invalid_graph()
    {
        var graph = new NodeGraphDefinition
        {
            EntryNodeId = "missing",
        };

        var exception = Assert.Throws<InvalidOperationException>(graph.CreateIndex);

        Assert.Contains("entry_node_missing", exception.Message);
    }

    private static NodeGraphDefinition CreateLinearGraph()
    {
        return new NodeGraphDefinition
        {
            EntryNodeId = "start",
            Nodes =
            [
                new NodeDefinition
                {
                    Id = "start",
                    Type = NodeTypes.Entry,
                    Ports = [NodePortDefinition.Output("next", valueType: "value")],
                },
                new NodeDefinition
                {
                    Id = "process",
                    Type = NodeTypes.Action,
                    Ports =
                    [
                        NodePortDefinition.Input("in", valueType: "value"),
                        NodePortDefinition.Output("out", valueType: "value"),
                    ],
                },
                new NodeDefinition
                {
                    Id = "finish",
                    Ports = [NodePortDefinition.Input("in", valueType: "value")],
                },
            ],
            Links =
            [
                Link("start", "next", "process", "in", "link-1"),
                Link("process", "out", "finish", "in", "link-2"),
            ],
        };
    }

    private static NodeDefinition Source(string id)
    {
        return new NodeDefinition
        {
            Id = id,
            Ports = [NodePortDefinition.Output("out")],
        };
    }

    private static NodeLinkDefinition Link(
        string fromNodeId,
        string fromPortId,
        string toNodeId,
        string toPortId,
        string id = "")
    {
        return new NodeLinkDefinition
        {
            Id = id,
            FromNodeId = fromNodeId,
            FromPortId = fromPortId,
            ToNodeId = toNodeId,
            ToPortId = toPortId,
        };
    }

    private sealed class IntSourceNode : Node
    {
        [NodeOutput(typeConstraint: NodeTypeConstraint.Strict)]
        private readonly int value = 42;

        public override void TryGetOutputValue<T>(NodePort outputPort, NodePort inputPort, ref T outputValue)
        {
            outputValue = (T)(object)value;
        }
    }

    private sealed class IntSinkNode : Node
    {
        [NodeInput(typeConstraint: NodeTypeConstraint.Strict)]
        private readonly int input = 0;

        public int Input => input;
    }

    private sealed class OverrideSinkNode : Node
    {
        [NodeInput(connectionType: NodeConnectionType.Override, typeConstraint: NodeTypeConstraint.Strict)]
        private readonly int input = 0;

        public int Input => input;
    }

    private sealed class StringSinkNode : Node
    {
        [NodeInput(typeConstraint: NodeTypeConstraint.Strict)]
        private readonly string input = string.Empty;

        public string Input => input;
    }

    private sealed class LoopbackNode : Node
    {
        [NodeInput(typeConstraint: NodeTypeConstraint.Strict)]
        private readonly int input = 0;

        [NodeOutput(typeConstraint: NodeTypeConstraint.Strict)]
        private readonly int output = 0;

        public int Input => input;

        public int Output => output;
    }
}
