namespace NKGGameFramework.Adapter.Godot;

public readonly record struct GodotObjectId(int Value)
{
    public static GodotObjectId Root { get; } = new(0);
}
