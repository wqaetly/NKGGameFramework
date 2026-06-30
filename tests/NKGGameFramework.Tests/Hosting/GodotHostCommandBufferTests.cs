using NKGGameFramework.Adapter.Godot;

namespace NKGGameFramework.Tests.Hosting;

public sealed class GodotHostCommandBufferTests
{
    [Fact]
    public void Command_buffer_formats_frame_and_node2d_commands_in_invariant_text()
    {
        var buffer = new GodotHostCommandBuffer();

        buffer.BeginFrame(12, 30, 4, isTerminal: false);
        buffer.UpsertNode2D("PLAYER", 7, 10.1256, 20);

        Assert.Equal(
            "FRAME 12 30 4 0\nNODE2D PLAYER 7 10.126 20\nEND",
            buffer.BuildText());
    }

    [Fact]
    public void Command_buffer_builds_binary_envelope()
    {
        var buffer = new GodotHostCommandBuffer();

        buffer.BeginFrame(12, 30, 4, isTerminal: true);
        buffer.UpsertNode2D("PLAYER", 7, 10.1256, 20);

        var encoded = buffer.Build();
        Assert.StartsWith(GodotHostCommandBuffer.BinaryEnvelopePrefix, encoded, StringComparison.Ordinal);

        var payload = Convert.FromBase64String(encoded[GodotHostCommandBuffer.BinaryEnvelopePrefix.Length..]);
        using var reader = new BinaryReader(new MemoryStream(payload));
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(12, reader.ReadInt32());
        Assert.Equal(30, reader.ReadInt32());
        Assert.Equal(4, reader.ReadInt32());
        Assert.Equal(1, reader.ReadByte());

        Assert.Equal(2, reader.ReadByte());
        var kindLength = reader.ReadInt32();
        Assert.Equal("PLAYER", System.Text.Encoding.UTF8.GetString(reader.ReadBytes(kindLength)));
        Assert.Equal(7, reader.ReadInt32());
        Assert.Equal(10.1256, reader.ReadDouble(), 4);
        Assert.Equal(20, reader.ReadDouble());
        Assert.Equal(255, reader.ReadByte());
    }

    [Fact]
    public void Command_buffer_can_return_raw_binary_payload()
    {
        var buffer = new GodotHostCommandBuffer();

        buffer.BeginFrame(12, 30, 4, isTerminal: false);

        var payload = buffer.BuildBytes();
        Assert.Equal(1, payload[0]);
        Assert.Equal(255, payload[^1]);
    }

    [Fact]
    public void Command_buffer_rejects_node_kinds_with_whitespace()
    {
        var buffer = new GodotHostCommandBuffer();

        Assert.Throws<ArgumentException>(() => buffer.UpsertNode2D("BAD KIND", 1, 0, 0));
    }

    [Fact]
    public void Command_buffer_writes_generic_object_commands()
    {
        var buffer = new GodotHostCommandBuffer();

        buffer.CreateNode(10, "Node2D", "Player");
        buffer.SetParent(10, 0);
        buffer.SetTransform2D(10, 12.5, 30.25, 0.5, 2, 3);
        buffer.SetVisible(10, visible: true);
        buffer.DestroyObject(10);

        Assert.Equal(
            "CREATE_NODE 10 Node2D Player\nSET_PARENT 10 0\nSET_TRANSFORM2D 10 12.5 30.25 0.5 2 3\nSET_VISIBLE 10 1\nDESTROY_OBJECT 10\nEND",
            buffer.BuildText());
    }

    [Fact]
    public void Command_buffer_encodes_generic_object_commands()
    {
        var buffer = new GodotHostCommandBuffer();

        buffer.CreateNode(10, "Node2D", "Player");
        buffer.SetParent(10, 0);
        buffer.SetTransform2D(10, 12.5, 30.25, 0.5, 2, 3);
        buffer.SetVisible(10, visible: false);
        buffer.DestroyObject(10);

        using var reader = new BinaryReader(new MemoryStream(buffer.BuildBytes()));

        Assert.Equal(3, reader.ReadByte());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal("Node2D", ReadString(reader));
        Assert.Equal("Player", ReadString(reader));

        Assert.Equal(5, reader.ReadByte());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal(0, reader.ReadInt32());

        Assert.Equal(6, reader.ReadByte());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal(12.5, reader.ReadDouble());
        Assert.Equal(30.25, reader.ReadDouble());
        Assert.Equal(0.5, reader.ReadDouble());
        Assert.Equal(2, reader.ReadDouble());
        Assert.Equal(3, reader.ReadDouble());

        Assert.Equal(7, reader.ReadByte());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal(0, reader.ReadByte());

        Assert.Equal(4, reader.ReadByte());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal(255, reader.ReadByte());
    }

    [Fact]
    public void Godot_host_commands_facade_writes_node_commands()
    {
        var buffer = new GodotHostCommandBuffer();
        var commands = new GodotHostCommands(buffer);

        var node = commands.CreateNode(10, "Node2D", "Player");
        node.SetParent(GodotObjectId.Root);
        node.SetTransform2D(12.5, 30.25, rotation: 0.5, scaleX: 2, scaleY: 3);
        node.SetVisible(true);
        node.SetProperty("color", GodotVariant.FromColor(new GodotColor(0.2, 0.7, 0.9)));
        node.Call("set_z_index", GodotVariant.FromInteger(2));
        node.Destroy();

        Assert.Equal(
            "CREATE_NODE 10 Node2D Player\nSET_PARENT 10 0\nSET_TRANSFORM2D 10 12.5 30.25 0.5 2 3\nSET_VISIBLE 10 1\nSET_PROPERTY 10 color COLOR 0.2 0.7 0.9 1\nCALL_METHOD 10 set_z_index 1 INTEGER 2\nDESTROY_OBJECT 10\nEND",
            buffer.BuildText());
    }

    [Fact]
    public void Godot_host_commands_can_reference_existing_nodes()
    {
        var buffer = new GodotHostCommandBuffer();
        var commands = new GodotHostCommands(buffer);

        var node = commands.GetNode(new GodotObjectId(10));
        node.SetTransform2D(12.5, 30.25);

        Assert.Equal("SET_TRANSFORM2D 10 12.5 30.25 0 1 1\nEND", buffer.BuildText());
    }

    [Fact]
    public void Command_buffer_writes_property_variants()
    {
        var buffer = new GodotHostCommandBuffer();

        buffer.SetProperty(10, "color", GodotVariant.FromColor(new GodotColor(0.2, 0.7, 0.9, 0.5)));
        buffer.SetProperty(
            10,
            "polygon",
            GodotVariant.FromPackedVector2Array(
            [
                new GodotVector2(0, -36),
                new GodotVector2(-10, 8)
            ]));
        buffer.SetProperty(11, "text", GodotVariant.FromString("Hello HUD"));
        buffer.SetProperty(12, "disabled", GodotVariant.FromBool(true));
        buffer.SetProperty(12, "count", GodotVariant.FromInteger(42));
        buffer.SetProperty(12, "ratio", GodotVariant.FromFloat(0.625));
        buffer.SetProperty(12, "offset", GodotVariant.FromVector2(new GodotVector2(4, -8)));

        Assert.Equal(
            "SET_PROPERTY 10 color COLOR 0.2 0.7 0.9 0.5\nSET_PROPERTY 10 polygon PACKED_VECTOR2_ARRAY 2 0 -36 -10 8\nSET_PROPERTY 11 text STRING SGVsbG8gSFVE\nSET_PROPERTY 12 disabled BOOL 1\nSET_PROPERTY 12 count INTEGER 42\nSET_PROPERTY 12 ratio FLOAT 0.625\nSET_PROPERTY 12 offset VECTOR2 4 -8\nEND",
            buffer.BuildText());
    }

    [Fact]
    public void Command_buffer_encodes_property_variants()
    {
        var buffer = new GodotHostCommandBuffer();

        buffer.SetProperty(10, "color", GodotVariant.FromColor(new GodotColor(0.2, 0.7, 0.9, 0.5)));
        buffer.SetProperty(
            10,
            "polygon",
            GodotVariant.FromPackedVector2Array(
            [
                new GodotVector2(0, -36),
                new GodotVector2(-10, 8)
            ]));
        buffer.SetProperty(11, "text", GodotVariant.FromString("Hello HUD"));
        buffer.SetProperty(12, "disabled", GodotVariant.FromBool(true));
        buffer.SetProperty(12, "count", GodotVariant.FromInteger(42));
        buffer.SetProperty(12, "ratio", GodotVariant.FromFloat(0.625));
        buffer.SetProperty(12, "offset", GodotVariant.FromVector2(new GodotVector2(4, -8)));

        using var reader = new BinaryReader(new MemoryStream(buffer.BuildBytes()));

        Assert.Equal(8, reader.ReadByte());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal("color", ReadString(reader));
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(0.2, reader.ReadDouble());
        Assert.Equal(0.7, reader.ReadDouble());
        Assert.Equal(0.9, reader.ReadDouble());
        Assert.Equal(0.5, reader.ReadDouble());

        Assert.Equal(8, reader.ReadByte());
        Assert.Equal(10, reader.ReadInt32());
        Assert.Equal("polygon", ReadString(reader));
        Assert.Equal(2, reader.ReadByte());
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(0, reader.ReadDouble());
        Assert.Equal(-36, reader.ReadDouble());
        Assert.Equal(-10, reader.ReadDouble());
        Assert.Equal(8, reader.ReadDouble());

        Assert.Equal(8, reader.ReadByte());
        Assert.Equal(11, reader.ReadInt32());
        Assert.Equal("text", ReadString(reader));
        Assert.Equal(3, reader.ReadByte());
        Assert.Equal("Hello HUD", ReadString(reader));

        Assert.Equal(8, reader.ReadByte());
        Assert.Equal(12, reader.ReadInt32());
        Assert.Equal("disabled", ReadString(reader));
        Assert.Equal(4, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());

        Assert.Equal(8, reader.ReadByte());
        Assert.Equal(12, reader.ReadInt32());
        Assert.Equal("count", ReadString(reader));
        Assert.Equal(5, reader.ReadByte());
        Assert.Equal(42, reader.ReadInt64());

        Assert.Equal(8, reader.ReadByte());
        Assert.Equal(12, reader.ReadInt32());
        Assert.Equal("ratio", ReadString(reader));
        Assert.Equal(6, reader.ReadByte());
        Assert.Equal(0.625, reader.ReadDouble());

        Assert.Equal(8, reader.ReadByte());
        Assert.Equal(12, reader.ReadInt32());
        Assert.Equal("offset", ReadString(reader));
        Assert.Equal(7, reader.ReadByte());
        Assert.Equal(4, reader.ReadDouble());
        Assert.Equal(-8, reader.ReadDouble());

        Assert.Equal(255, reader.ReadByte());
    }

    [Fact]
    public void Command_buffer_writes_call_method_commands()
    {
        var buffer = new GodotHostCommandBuffer();

        buffer.CallMethod(
            11,
            "set_text",
            [GodotVariant.FromString("Hello method"), GodotVariant.FromBool(false)]);

        Assert.Equal(
            "CALL_METHOD 11 set_text 2 STRING SGVsbG8gbWV0aG9k BOOL 0\nEND",
            buffer.BuildText());
    }

    [Fact]
    public void Command_buffer_encodes_call_method_commands()
    {
        var buffer = new GodotHostCommandBuffer();

        buffer.CallMethod(11, "set_text", [GodotVariant.FromString("Hello method")]);

        using var reader = new BinaryReader(new MemoryStream(buffer.BuildBytes()));

        Assert.Equal(9, reader.ReadByte());
        Assert.Equal(11, reader.ReadInt32());
        Assert.Equal("set_text", ReadString(reader));
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(3, reader.ReadByte());
        Assert.Equal("Hello method", ReadString(reader));
        Assert.Equal(255, reader.ReadByte());
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        return System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
    }
}
