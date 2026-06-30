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
    public void Command_buffer_rejects_node_kinds_with_whitespace()
    {
        var buffer = new GodotHostCommandBuffer();

        Assert.Throws<ArgumentException>(() => buffer.UpsertNode2D("BAD KIND", 1, 0, 0));
    }
}
