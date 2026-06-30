namespace NKGGameFramework.Adapter.Godot;

public sealed class GodotHostCommands
{
    private readonly GodotHostCommandBuffer _buffer;

    public GodotHostCommands(GodotHostCommandBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    public GodotNode CreateNode(int id, string typeName, string name = "")
    {
        var objectId = new GodotObjectId(id);
        _buffer.CreateNode(objectId.Value, typeName, name);
        return new GodotNode(objectId, this);
    }

    public GodotNode GetNode(GodotObjectId id)
    {
        return new GodotNode(id, this);
    }

    public void Destroy(GodotObjectId id)
    {
        _buffer.DestroyObject(id.Value);
    }

    internal void SetParent(GodotObjectId child, GodotObjectId parent)
    {
        _buffer.SetParent(child.Value, parent.Value);
    }

    internal void SetTransform2D(GodotObjectId id, double x, double y, double rotation, double scaleX, double scaleY)
    {
        _buffer.SetTransform2D(id.Value, x, y, rotation, scaleX, scaleY);
    }

    internal void SetVisible(GodotObjectId id, bool visible)
    {
        _buffer.SetVisible(id.Value, visible);
    }

    internal void SetProperty(GodotObjectId id, string propertyName, GodotVariant value)
    {
        _buffer.SetProperty(id.Value, propertyName, value);
    }
}
