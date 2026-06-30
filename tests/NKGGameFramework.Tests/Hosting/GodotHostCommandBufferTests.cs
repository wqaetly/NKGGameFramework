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
            buffer.Build());
    }

    [Fact]
    public void Command_buffer_rejects_node_kinds_with_whitespace()
    {
        var buffer = new GodotHostCommandBuffer();

        Assert.Throws<ArgumentException>(() => buffer.UpsertNode2D("BAD KIND", 1, 0, 0));
    }
}
