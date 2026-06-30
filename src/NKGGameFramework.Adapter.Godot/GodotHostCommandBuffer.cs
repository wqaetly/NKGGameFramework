using System.Globalization;
using System.Text;

namespace NKGGameFramework.Adapter.Godot;

public sealed class GodotHostCommandBuffer
{
    public const string BinaryEnvelopePrefix = "NKGCB1\nbase64\n";

    private const byte FrameCommand = 1;
    private const byte Node2DCommand = 2;
    private const byte EndCommand = 255;

    private readonly MemoryStream _binaryStream;
    private readonly BinaryWriter _binaryWriter;
    private readonly StringBuilder _textBuilder;
    private bool _ended;

    public GodotHostCommandBuffer(int capacity = 1024)
    {
        _binaryStream = new MemoryStream(capacity);
        _binaryWriter = new BinaryWriter(_binaryStream, Encoding.UTF8, leaveOpen: true);
        _textBuilder = new StringBuilder(capacity);
    }

    public void BeginFrame(int frame, int score, int lives, bool isTerminal)
    {
        ThrowIfEnded();
        _binaryWriter.Write(FrameCommand);
        _binaryWriter.Write(frame);
        _binaryWriter.Write(score);
        _binaryWriter.Write(lives);
        _binaryWriter.Write((byte)(isTerminal ? 1 : 0));

        _textBuilder.Append(
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

        var kindBytes = Encoding.UTF8.GetBytes(kind);
        _binaryWriter.Write(Node2DCommand);
        _binaryWriter.Write(kindBytes.Length);
        _binaryWriter.Write(kindBytes);
        _binaryWriter.Write(checked((int)id));
        _binaryWriter.Write(x);
        _binaryWriter.Write(y);

        _textBuilder.Append(
            CultureInfo.InvariantCulture,
            $"NODE2D {kind} {id} {x:0.###} {y:0.###}\n");
    }

    public string Build()
    {
        EnsureEnded();
        return BinaryEnvelopePrefix + Convert.ToBase64String(_binaryStream.ToArray());
    }

    public string BuildText()
    {
        EnsureEnded();
        return _textBuilder.ToString();
    }

    private void EnsureEnded()
    {
        if (!_ended)
        {
            _binaryWriter.Write(EndCommand);
            _textBuilder.Append("END");
            _ended = true;
        }
    }

    private void ThrowIfEnded()
    {
        if (_ended)
        {
            throw new InvalidOperationException("The Godot host command buffer has already been built.");
        }
    }
}
