namespace NKGGameFramework.Adapter.Godot;

public readonly struct GodotVariant
{
    private GodotVariant(GodotVariantKind kind, GodotColor color, IReadOnlyList<GodotVector2>? vector2Array)
    {
        Kind = kind;
        Color = color;
        Vector2Array = vector2Array;
    }

    public GodotVariantKind Kind { get; }

    public GodotColor Color { get; }

    public IReadOnlyList<GodotVector2>? Vector2Array { get; }

    public static GodotVariant FromColor(GodotColor value)
    {
        return new GodotVariant(GodotVariantKind.Color, value, null);
    }

    public static GodotVariant FromPackedVector2Array(IReadOnlyList<GodotVector2> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new GodotVariant(GodotVariantKind.PackedVector2Array, default, value);
    }
}

public enum GodotVariantKind : byte
{
    Color = 1,
    PackedVector2Array = 2
}
