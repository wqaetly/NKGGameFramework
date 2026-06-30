using System.Globalization;
using System.Text;

namespace NKGGameFramework.Adapter.Godot;

public sealed class GodotHostCommandBuffer
{
    private readonly StringBuilder _builder;
    private bool _ended;

    public GodotHostCommandBuffer(int capacity = 1024)
    {
        _builder = new StringBuilder(capacity);
    }

    public void BeginFrame(int frame, int score, int lives, bool isTerminal)
    {
        ThrowIfEnded();
        _builder.Append(
            CultureInfo.InvariantCulture,
            $"FRAME {frame} {score} {lives} {(isTerminal ? 1 : 0)}\n");
    }

    public void UpsertNode2D(string kind, long id, double x, double y)
    {
        ThrowIfEnded();
        if (string.IsNullOrWhiteSpace(kind) || kind.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Godot host node kind must be non-empty and must not contain whitespace.", nameof(kind));
        }

        _builder.Append(
            CultureInfo.InvariantCulture,
            $"NODE2D {kind} {id} {x:0.###} {y:0.###}\n");
    }

    public string Build()
    {
        if (!_ended)
        {
            _builder.Append("END");
            _ended = true;
        }

        return _builder.ToString();
    }

    private void ThrowIfEnded()
    {
        if (_ended)
        {
            throw new InvalidOperationException("The Godot host command buffer has already been built.");
        }
    }
}
