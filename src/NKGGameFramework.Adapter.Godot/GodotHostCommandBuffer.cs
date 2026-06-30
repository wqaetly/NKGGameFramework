using System.Globalization;
using System.Text;

namespace NKGGameFramework.Adapter.Godot;

public sealed class GodotHostCommandBuffer
{
    public const string BinaryEnvelopePrefix = "NKGCB1\nbase64\n";

    private const byte FrameCommand = 1;
    private const byte Node2DCommand = 2;
    private const byte CreateNodeCommand = 3;
    private const byte DestroyObjectCommand = 4;
    private const byte SetParentCommand = 5;
    private const byte SetTransform2DCommand = 6;
    private const byte SetVisibleCommand = 7;
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

        _binaryWriter.Write(Node2DCommand);
        WriteBinaryString(kind);
        _binaryWriter.Write(checked((int)id));
        _binaryWriter.Write(x);
        _binaryWriter.Write(y);

        _textBuilder.Append(
            CultureInfo.InvariantCulture,
            $"NODE2D {kind} {id} {x:0.###} {y:0.###}\n");
    }

    public void CreateNode(int id, string typeName, string name)
    {
        ThrowIfEnded();
        ValidateToken(typeName, nameof(typeName));
        ValidateToken(name, nameof(name), allowEmpty: true);

        _binaryWriter.Write(CreateNodeCommand);
        _binaryWriter.Write(id);
        WriteBinaryString(typeName);
        WriteBinaryString(name);

        _textBuilder.Append(
            CultureInfo.InvariantCulture,
            $"CREATE_NODE {id} {typeName} {name}\n");
    }

    public void DestroyObject(int id)
    {
        ThrowIfEnded();
        _binaryWriter.Write(DestroyObjectCommand);
        _binaryWriter.Write(id);

        _textBuilder.Append(
            CultureInfo.InvariantCulture,
            $"DESTROY_OBJECT {id}\n");
    }

    public void SetParent(int childId, int parentId)
    {
        ThrowIfEnded();
        _binaryWriter.Write(SetParentCommand);
        _binaryWriter.Write(childId);
        _binaryWriter.Write(parentId);

        _textBuilder.Append(
            CultureInfo.InvariantCulture,
            $"SET_PARENT {childId} {parentId}\n");
    }

    public void SetTransform2D(int id, double x, double y, double rotation, double scaleX, double scaleY)
    {
        ThrowIfEnded();
        _binaryWriter.Write(SetTransform2DCommand);
        _binaryWriter.Write(id);
        _binaryWriter.Write(x);
        _binaryWriter.Write(y);
        _binaryWriter.Write(rotation);
        _binaryWriter.Write(scaleX);
        _binaryWriter.Write(scaleY);

        _textBuilder.Append(
            CultureInfo.InvariantCulture,
            $"SET_TRANSFORM2D {id} {x:0.###} {y:0.###} {rotation:0.###} {scaleX:0.###} {scaleY:0.###}\n");
    }

    public void SetVisible(int id, bool visible)
    {
        ThrowIfEnded();
        _binaryWriter.Write(SetVisibleCommand);
        _binaryWriter.Write(id);
        _binaryWriter.Write((byte)(visible ? 1 : 0));

        _textBuilder.Append(
            CultureInfo.InvariantCulture,
            $"SET_VISIBLE {id} {(visible ? 1 : 0)}\n");
    }

    public string Build()
    {
        EnsureEnded();
        return BinaryEnvelopePrefix + Convert.ToBase64String(_binaryStream.ToArray());
    }

    public byte[] BuildBytes()
    {
        EnsureEnded();
        return _binaryStream.ToArray();
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

    private void WriteBinaryString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        _binaryWriter.Write(bytes.Length);
        _binaryWriter.Write(bytes);
    }

    private static void ValidateToken(string value, string paramName, bool allowEmpty = false)
    {
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Godot host command tokens must be non-empty and must not contain whitespace.", paramName);
        }
    }
}
