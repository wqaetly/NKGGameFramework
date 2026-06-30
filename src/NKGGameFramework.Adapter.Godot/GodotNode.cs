namespace NKGGameFramework.Adapter.Godot;

public readonly struct GodotNode
{
    private readonly GodotHostCommands _commands;

    internal GodotNode(GodotObjectId id, GodotHostCommands commands)
    {
        Id = id;
        _commands = commands;
    }

    public GodotObjectId Id { get; }

    public void SetParent(GodotObjectId parent)
    {
        _commands.SetParent(Id, parent);
    }

    public void SetTransform2D(double x, double y, double rotation = 0, double scaleX = 1, double scaleY = 1)
    {
        _commands.SetTransform2D(Id, x, y, rotation, scaleX, scaleY);
    }

    public void SetVisible(bool visible)
    {
        _commands.SetVisible(Id, visible);
    }

    public void SetProperty(string propertyName, GodotVariant value)
    {
        _commands.SetProperty(Id, propertyName, value);
    }

    public void Call(string methodName, params GodotVariant[] arguments)
    {
        _commands.CallMethod(Id, methodName, arguments);
    }

    public void Destroy()
    {
        _commands.Destroy(Id);
    }
}
