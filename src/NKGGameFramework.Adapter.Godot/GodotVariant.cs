namespace NKGGameFramework.Adapter.Godot;

public readonly struct GodotVariant
{
    private GodotVariant(GodotVariantKind kind, GodotColor color, IReadOnlyList<GodotVector2>? vector2Array, string? text)
    {
        Kind = kind;
        Color = color;
        Vector2Array = vector2Array;
        Text = text;
    }

    public GodotVariantKind Kind { get; }

    public GodotColor Color { get; }

    public IReadOnlyList<GodotVector2>? Vector2Array { get; }

    public string? Text { get; }

    public static GodotVariant FromColor(GodotColor value)
    {
        return new GodotVariant(GodotVariantKind.Color, value, null, null);
    }

    public static GodotVariant FromPackedVector2Array(IReadOnlyList<GodotVector2> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new GodotVariant(GodotVariantKind.PackedVector2Array, default, value, null);
    }

    public static GodotVariant FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new GodotVariant(GodotVariantKind.String, default, null, value);
    }
}

public enum GodotVariantKind : byte
{
    Color = 1,
    PackedVector2Array = 2,
    String = 3
}
